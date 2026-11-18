#!/usr/bin/env python3
"""Local pull deployment agent. Only successful push CI on develop is eligible."""
import argparse
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import urllib.request
import uuid


class Blocked(RuntimeError):
    pass


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def atomic(path, value):
    path = Path(path)
    temporary = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    try:
        with temporary.open("xb") as stream:
            os.chmod(temporary, 0o600)
            stream.write(value)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def write_json(path, value):
    atomic(path, json.dumps(value, indent=2).encode())


def command(args, *, cwd=None, log=None, env=None, timeout=900):
    # Command output can contain private Compose interpolation. Keep diagnostics local.
    result = subprocess.run(args, cwd=cwd, env=env, capture_output=True, timeout=timeout)
    if log:
        atomic(log, result.stdout + result.stderr)
    if result.returncode:
        raise Blocked("Command failed: " + args[0] + "; inspect private release log")
    return result.stdout


def http_json(url):
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json", "User-Agent": "aipms-local-deploy/1"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def sha(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value):
        raise Blocked("Invalid commit identifier")
    return value


def eligible(runs, repository, revision):
    # Check the latest attempt, never fall back to an older successful run.
    matches = [run for run in runs if run.get("head_sha") == revision
               and run.get("head_branch") == "develop" and run.get("event") == "push"
               and run.get("head_repository", {}).get("full_name") == repository]
    if not matches:
        return False
    latest = max(matches, key=lambda run: (run["id"], run.get("run_attempt", 1)))
    return latest.get("status") == "completed" and latest.get("conclusion") == "success"


def environment_update(content, updates):
    lines = content.decode().splitlines()
    for key, value in updates.items():
        positions = [i for i, line in enumerate(lines) if line.startswith(key + "=")]
        if len(positions) > 1:
            raise Blocked("Duplicate deployment variable: " + key)
        if positions:
            lines[positions[0]] = key + "=" + value
        else:
            lines.append(key + "=" + value)
    return ("\n".join(lines) + "\n").encode()


def validate_frontend(before, after, revision):
    if after.get("revision") != revision:
        raise Blocked("Frontend build revision mismatch")
    if after.get("apiBaseUrl") != before.get("apiBaseUrl") or after.get("flags") != before.get("flags"):
        raise Blocked("Frontend API or feature flags changed; review deployment profile first")


class Agent:
    def __init__(self, config, *, fetch_json=http_json, run=command):
        self.config, self.run = config, run
        self.fetch_json = fetch_json if fetch_json is not http_json else self.fetch
        self.root = Path(config["stateDirectory"]).resolve()
        self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.compose_binary = [config.get("composeBinary", "/usr/bin/docker-compose")]

    def fetch(self, url):
        if url.startswith("https://api.github.com/") and self.config.get("githubCli"):
            # Windows gh reads its existing OS credential store, including from WSL systemd.
            # Never copy the GitHub token into an env file or repository.
            return json.loads(self.run([self.config["githubCli"], "api", url[len("https://api.github.com/"):]], timeout=60))
        return http_json(url)

    def inspect(self, target):
        return json.loads(self.run(["docker", "inspect", target["container"]]))[0]

    def compose(self, target, *args, env=None, log=None):
        return self.run(self.compose_binary + ["--env-file", target["environmentFile"], "-f", target["composeFile"], *args],
                        cwd=str(Path(target["composeFile"]).parent), env=env, log=log, timeout=240)

    def mirror(self, target):
        path = self.root / (target["service"] + ".git")
        if not path.exists():
            self.run(["git", "clone", "--bare", "https://github.com/" + target["repository"] + ".git", str(path)])
        self.run(["git", "--git-dir", str(path), "fetch", "--no-tags", "origin", "develop:refs/heads/develop"])
        revision = sha(self.run(["git", "--git-dir", str(path), "rev-parse", "refs/heads/develop"]).decode().strip())
        return path, revision

    def git(self, mirror, *args):
        return self.run(["git", "--git-dir", str(mirror), *args])

    def current(self, target):
        metadata = self.fetch_json(target["localMetadataUrl"])
        return sha(metadata["commitSha" if target["service"] == "api" else "revision"])

    def ci(self, target, revision):
        url = ("https://api.github.com/repos/" + target["repository"] + "/actions/workflows/" + target["workflow"]
               + "/runs?branch=develop&event=push&head_sha=" + revision + "&per_page=20")
        if not eligible(self.fetch_json(url)["workflow_runs"], target["repository"], revision):
            return False
        checks = self.fetch_json("https://api.github.com/repos/" + target["repository"] + "/commits/" + revision + "/check-runs?per_page=100")
        return checks.get("total_count", 0) > 0 and checks["total_count"] <= 100 and all(
            check.get("status") == "completed" and check.get("conclusion") in ("success", "neutral", "skipped")
            for check in checks["check_runs"])

    def source_guard(self, target, mirror, previous, revision):
        sha(previous)
        self.git(mirror, "merge-base", "--is-ancestor", previous, revision)
        if previous != revision:
            paths = self.git(mirror, "diff", "--name-only", previous, revision, "--", *target["manualReviewPaths"]).decode().splitlines()
            if paths:
                raise Blocked("Database/Compose/deployment contract changed; manual release required: " + ", ".join(paths[:8]))

    def contracts(self, backend_mirror, backend_revision, frontend_mirror, frontend_revision):
        prefix = "docs/contracts/cib-v4/examples/"
        paths = self.git(backend_mirror, "ls-tree", "-r", "--name-only", backend_revision, prefix).decode().splitlines()
        expected = {path[len(prefix):]: hashlib.sha256(self.git(backend_mirror, "show", backend_revision + ":" + path)).hexdigest()
                    for path in paths if path.endswith(".json")}
        actual = json.loads(self.git(frontend_mirror, "show", frontend_revision + ":contract-lock.json"))["files"]
        if not expected or expected != actual:
            raise Blocked("Backend examples differ from frontend contract lock; coordinate release manually")

    def workspace(self, target, mirror, revision):
        base = self.root / "builds"
        base.mkdir(exist_ok=True)
        path = base / (target["service"] + "-" + revision[:12] + "-" + uuid.uuid4().hex[:8])
        path.mkdir(mode=0o700)
        archive = self.git(mirror, "archive", revision)
        # Git emits only tracked source. tar rejects unsafe paths by default.
        result = subprocess.run(["tar", "--no-same-owner", "--same-permissions", "-xf", "-", "-C", str(path)], input=archive, capture_output=True)
        if result.returncode:
            raise Blocked("Source extraction failed")
        return path

    def build(self, target, mirror, revision, release):
        workspace = self.workspace(target, mirror, revision)
        try:
            if target["service"] == "frontend":
                self.run(["docker", "run", "--rm", "--user", str(os.getuid()) + ":" + str(os.getgid()),
                          "--mount", "type=bind,source=" + str(workspace) + ",target=" + str(workspace),
                          "--mount", "type=bind,source=" + str(mirror) + ",target=" + str(mirror) + ",readonly",
                          "--env", "GIT_DIR=" + str(mirror), "--env", "GIT_WORK_TREE=" + str(workspace),
                          "--workdir", str(workspace), self.config["frontendBuilderImage"], "sh", "-c",
                          "pnpm install --frozen-lockfile && node scripts/build-staging.mjs"], log=release / "build.log", timeout=1200)
                metadata = json.loads((workspace / "dist/deployment.json").read_text())
                validate_frontend(self.config["frontendProfile"], metadata, revision)
                build_args = ["-f", str(workspace / "deploy/staging/Dockerfile")]
            else:
                self.run(["/usr/bin/pwsh", "-NoProfile", "-File", str(Path(__file__).with_name("verify-schema.ps1")),
                          "-Repository", str(workspace), "-PrivateSettings", self.config["privateSettings"]],
                         log=release / "schema.log", timeout=180)
                build_args = ["--build-arg", "COMMIT_SHA=" + revision]
            image = target["imagePrefix"] + ":auto-" + revision + "-" + release.name[-8:]
            self.run(["docker", "build", *build_args, "--label", "org.opencontainers.image.revision=" + revision,
                      "-t", image, str(workspace)], log=release / "image.log", timeout=1200)
            return image
        finally:
            # Only this newly created build directory is disposable; never touch user checkouts.
            if workspace.resolve().parent != (self.root / "builds").resolve() or workspace.is_symlink():
                raise Blocked("Refusing unexpected build cleanup path")
            shutil.rmtree(workspace)

    def ready(self, target, revision, expected_image=None, timeout=120):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                container = self.inspect(target)
                healthy = container["State"].get("Health", {}).get("Status") == "healthy"
                if not container["State"].get("Running"):
                    return False
                if expected_image and container["Config"]["Image"] != expected_image:
                    return False
                if healthy and self.current(target) == revision:
                    return True
            except Exception:
                pass
            time.sleep(2)
        return False

    def restore(self, journal):
        target = journal["target"]
        release = Path(journal["release"])
        if digest(target["composeFile"]) != journal["composeHash"]:
            raise Blocked("Compose changed; rollback requires operator review")
        actual_image = self.inspect(target)["Config"]["Image"]
        if actual_image not in (journal["previousImage"], journal["candidateImage"]):
            raise Blocked("Another release changed the image; manual recovery required")
        atomic(target["environmentFile"], (release / "environment.before.private").read_bytes())
        self.compose(target, "up", "-d", "--no-build", target["service"], log=release / "rollback.log")
        if not self.ready(target, journal["previousRevision"], journal["previousImage"]):
            raise Blocked("Rollback did not become healthy; immediate operator review required")
        (self.root / "transaction.json").unlink(missing_ok=True)

    def deploy(self, target, revision, image, release, expected_previous):
        container = self.inspect(target)
        before_revision = self.current(target)
        if before_revision != expected_previous:
            raise Blocked("Another release changed the running revision during build")
        compose_hash = digest(target["composeFile"])
        if compose_hash != target["composeHash"]:
            raise Blocked("Host Compose changed; review local deployment configuration")
        config_path = Path(self.config["privateSettings"])
        private_hash = digest(config_path)
        previous_env = Path(target["environmentFile"]).read_bytes()
        atomic(release / "environment.before.private", previous_env)
        journal = {"target": target, "release": str(release), "previousRevision": before_revision,
                   "previousImage": container["Config"]["Image"], "composeHash": compose_hash,
                   "candidateRevision": revision, "candidateImage": image}
        updates = {target["imageVariable"]: image.split(":", 1)[1]}
        if target["service"] == "api":
            updates["COMMIT_SHA"] = revision
        render_env = dict(os.environ, **updates)
        rendered = json.loads(self.compose(target, "config", "--format", "json", env=render_env))
        spec = rendered["services"][target["service"]]
        if spec["image"] != image:
            raise Blocked("Compose candidate image mismatch")
        # Preserve every host bind mount and the private config. Never replace Drive credentials.
        binds = {item["Destination"]: item["Source"] for item in container["Mounts"] if item["Type"] == "bind"}
        proposed = {item["target"]: item.get("source") for item in spec.get("volumes", []) if item.get("type") == "bind"}
        if binds != proposed:
            raise Blocked("Candidate changes persistent mounts")
        if target["service"] == "api":
            self.run(["/usr/bin/pwsh", "-NoProfile", "-File", str(Path(__file__).with_name("backup.ps1")),
                      "-PrivateSettings", str(config_path), "-ReleaseDirectory", str(release)],
                     log=release / "backup.log", timeout=900)
        if self.inspect(target)["Id"] != container["Id"] or Path(target["environmentFile"]).read_bytes() != previous_env:
            raise Blocked("Another deployment changed the service")
        write_json(self.root / "transaction.json", journal)
        try:
            atomic(target["environmentFile"], environment_update(previous_env, updates))
            self.compose(target, "up", "-d", "--no-build", target["service"], log=release / "deploy.log")
            if not self.ready(target, revision, image):
                raise Blocked("Candidate health/revision verification failed")
            if digest(config_path) != private_hash:
                raise Blocked("Private configuration changed during rollout")
            if target["service"] == "frontend":
                validate_frontend(self.config["frontendProfile"], self.fetch_json(target["localMetadataUrl"]), revision)
            else:
                policies = self.fetch_json(self.config["uploadPolicyUrl"])
                if policies != self.config["uploadPolicies"]:
                    raise Blocked("Upload policy changed; review required")
            write_json(release / "result.json", {"status": "deployed", "revision": revision, "image": image,
                                                 "previousRevision": before_revision, "checkedAt": now()})
            (self.root / "transaction.json").unlink()
        except Exception:
            self.restore(journal)
            raise Blocked("Candidate failed; previous application restored") from None

    def tick(self, *, redeploy=False, dry_run=False):
        journal_path = self.root / "transaction.json"
        if journal_path.exists():
            if dry_run:
                raise Blocked("Interrupted transaction requires recovery")
            self.restore(json.loads(journal_path.read_text()))
        status = {"checkedAt": now(), "branch": "develop", "services": {}}
        mirrors = {target["service"]: self.mirror(target) for target in self.config["targets"]}
        backend = next(target for target in self.config["targets"] if target["service"] == "api")
        frontend = next(target for target in self.config["targets"] if target["service"] == "frontend")
        for target in (backend, frontend):
            name = target["service"]
            mirror, revision = mirrors[name]
            try:
                previous = self.current(target)
                detail = {"candidate": revision, "running": previous}
                if previous == revision and not redeploy:
                    detail["status"] = "up_to_date"
                elif not self.ci(target, revision):
                    detail["status"] = "waiting_for_successful_ci"
                else:
                    if name == "frontend" and self.current(backend) != mirrors["api"][1]:
                        raise Blocked("Waiting for backend develop deployment")
                    self.source_guard(target, mirror, previous, revision)
                    be_revision = revision if name == "api" else self.current(backend)
                    fe_revision = self.current(frontend) if name == "api" else revision
                    self.contracts(mirrors["api"][0], be_revision, mirrors["frontend"][0], fe_revision)
                    if dry_run:
                        detail["status"] = "eligible_dry_run"
                    else:
                        if shutil.disk_usage(self.root).free < 5 * 1024 ** 3:
                            raise Blocked("Less than 5 GiB free; rotate old release artifacts before deploying")
                        attempts = self.root / (name + "-attempt.json")
                        attempt = json.loads(attempts.read_text()) if attempts.exists() else {}
                        if attempt.get("revision") == revision and attempt.get("failed"):
                            raise Blocked("Previous attempt failed; inspect logs and clear failure before retry")
                        release = self.root / "releases" / (name + "-" + revision[:12] + "-" + uuid.uuid4().hex[:8])
                        release.mkdir(parents=True, mode=0o700)
                        write_json(attempts, {"revision": revision, "failed": True, "startedAt": now(), "release": str(release)})
                        image = self.build(target, mirror, revision, release)
                        # Recheck the current branch and CI after the potentially long build.
                        if self.mirror(target)[1] != revision or not self.ci(target, revision):
                            raise Blocked("Candidate was superseded or CI changed; skip stale deployment")
                        counterpart = frontend if name == "api" else backend
                        counterpart_revision = fe_revision if name == "api" else be_revision
                        if self.current(counterpart) != counterpart_revision:
                            raise Blocked("Counterpart changed during build; recheck release compatibility")
                        self.deploy(target, revision, image, release, previous)
                        write_json(attempts, {"revision": revision, "failed": False, "completedAt": now()})
                        detail["status"] = "deployed"
                        detail["running"] = revision
                status["services"][name] = detail
            except Exception as error:
                status["services"][name] = {"status": "blocked", "candidate": revision,
                                             "reason": str(error) if isinstance(error, Blocked) else type(error).__name__}
        write_json(self.root / "status.json", status)
        if self.config.get("statusCopy"):
            write_json(self.config["statusCopy"], status)
        return status


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--redeploy", action="store_true", help="Rebuild current commit; CI and all guards still apply")
    options = parser.parse_args()
    os.umask(0o077)
    agent = Agent(json.loads(Path(options.config).read_text()))
    with (agent.root / "agent.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print(json.dumps({"status": "another_deployment_running"}))
            return
        result = agent.tick(redeploy=options.redeploy, dry_run=options.dry_run)
        print(json.dumps(result))
        if any(item["status"] == "blocked" for item in result["services"].values()):
            sys.exit(1)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # No raw subprocess output, connection strings or config content in the journal.
        print(json.dumps({"status": "error", "reason": str(error) if isinstance(error, Blocked) else type(error).__name__}))
        sys.exit(1)
