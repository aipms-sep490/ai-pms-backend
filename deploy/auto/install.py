#!/usr/bin/env python3
"""Install the reviewed agent and freeze the existing host deployment profile."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument("--github-cli", default="/mnt/c/Program Files/GitHub CLI/gh.exe")
args = parser.parse_args()
os.umask(0o077)
home = Path.home()
source = Path(__file__).resolve().parent
settings = home / ".config/aipms/appsettings.Staging.json"
config_path = home / ".config/aipms/autodeploy.json"
if config_path.exists():
    raise SystemExit("Config already exists; review and update manually without replacing the saved profile")
if json.loads(settings.read_text())["FileStorage"]["Provider"] != "GoogleDrive":
    raise SystemExit("Expected active GoogleDrive configuration")
subprocess.run([args.github_cli, "api", "repos/aipms-sep490/ai-pms-backend", "--jq", ".default_branch"], check=True)
targets = []
for service, repository, container, workflow, image_var, metadata_url in [
    ("api", "ai-pms-backend", "aipms-api", "backend-ci.yml", "AIPMS_IMAGE_TAG", "http://127.0.0.1:5088/api/v1/system"),
    ("frontend", "ai-pms-frontend", "aipms-frontend", "frontend-ci.yml", "AIPMS_FE_IMAGE_TAG", "http://127.0.0.1:5180/deployment.json"),
]:
    inspected = json.loads(subprocess.check_output(["docker", "inspect", container]))[0]
    labels = inspected["Config"]["Labels"]
    compose_path = Path(labels["com.docker.compose.project.config_files"])
    env_file = Path(labels["com.docker.compose.project.working_dir"]) / ".env"
    if not compose_path.is_file() or not env_file.is_file():
        raise SystemExit("Current Compose inputs unavailable")
    targets.append({"service": service, "repository": "aipms-sep490/" + repository,
                    "container": container, "imagePrefix": container, "workflow": workflow,
                    "imageVariable": image_var, "composeFile": str(compose_path), "environmentFile": str(env_file),
                    "composeHash": hashlib.sha256(compose_path.read_bytes()).hexdigest(), "localMetadataUrl": metadata_url,
                    "manualReviewPaths": (["db/changes", "db/e2e/migrations.json", "deploy/wsl", "Dockerfile"]
                                          if service == "api" else ["deploy/staging/compose.yaml"])})
def fetch(url):
    with urllib.request.urlopen(url, timeout=15) as response:
        return json.load(response)
frontend_profile = fetch("http://127.0.0.1:5180/deployment.json")
config = {"stateDirectory": str(home / ".local/state/aipms-deploy"), "privateSettings": str(settings),
          "githubCli": args.github_cli, "composeBinary": "/usr/bin/docker-compose",
          "frontendBuilderImage": "aipms-deploy-node:24-pnpm11", "targets": targets,
          "frontendProfile": {"apiBaseUrl": frontend_profile["apiBaseUrl"], "flags": frontend_profile["flags"]},
          "uploadPolicyUrl": "http://127.0.0.1:5088/api/v1/upload-policies",
          "uploadPolicies": fetch("http://127.0.0.1:5088/api/v1/upload-policies")}
destination = home / ".local/lib/aipms-deploy"
destination.mkdir(parents=True, exist_ok=True, mode=0o700)
for name in ("agent.py", "verify-schema.ps1", "backup.ps1"):
    shutil.copyfile(source / name, destination / name)
    os.chmod(destination / name, 0o600)
units = home / ".config/systemd/user"
units.mkdir(parents=True, exist_ok=True)
for name in ("aipms-deploy.service", "aipms-deploy.timer"):
    shutil.copyfile(source / name, units / name)
config_path.write_text(json.dumps(config, indent=2))
os.chmod(config_path, 0o600)
print(json.dumps({"installed": True, "config": str(config_path), "pollSeconds": 60,
                  "branch": "develop", "frontendFlags": len(config["frontendProfile"]["flags"]),
                  "timerStarted": False}))
