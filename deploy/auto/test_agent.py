import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("deploy_agent", Path(__file__).with_name("agent.py"))
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)
SHA = "a" * 40
OTHER = "b" * 40
REPO = "aipms-sep490/ai-pms-backend"


class ReleaseGuards(unittest.TestCase):
    def run_record(self, **changes):
        return dict(id=20, run_attempt=1, head_sha=SHA, head_branch="develop", event="push",
                    head_repository={"full_name": REPO}, status="completed", conclusion="success", **changes)

    def test_only_successful_exact_develop_push_is_eligible(self):
        base = self.run_record()
        self.assertTrue(agent.eligible([base], REPO, SHA))
        for key, value in [("head_sha", OTHER), ("head_branch", "main"), ("event", "pull_request"),
                           ("head_repository", {"full_name": "someone/fork"}), ("status", "in_progress"),
                           ("conclusion", "failure"), ("conclusion", "cancelled")]:
            with self.subTest(key=key, value=value):
                changed = dict(base, **{key: value})
                self.assertFalse(agent.eligible([changed], REPO, SHA))

    def test_failed_newer_run_never_falls_back_to_previous_success(self):
        first = self.run_record()
        second = dict(first, id=21, conclusion="failure")
        self.assertFalse(agent.eligible([first, second], REPO, SHA))
        rerun = dict(first, run_attempt=2, status="in_progress", conclusion=None)
        self.assertFalse(agent.eligible([first, rerun], REPO, SHA))

    def test_environment_update_preserves_private_values_and_refuses_ambiguity(self):
        text = b"SECRET=preserve-this\nAIPMS_IMAGE_TAG=old\nCOMMIT_SHA=old\n"
        updated = agent.environment_update(text, {"AIPMS_IMAGE_TAG": "new", "COMMIT_SHA": SHA})
        self.assertIn(b"SECRET=preserve-this\n", updated)
        self.assertIn(b"AIPMS_IMAGE_TAG=new\n", updated)
        with self.assertRaises(agent.Blocked):
            agent.environment_update(text + b"AIPMS_IMAGE_TAG=duplicate\n", {"AIPMS_IMAGE_TAG": "new"})

    def test_frontend_profile_cannot_silently_lose_tabs_or_change_api(self):
        before = {"apiBaseUrl": "https://example.test/api/v1", "flags": {"RISK": "true", "PREVIEW": "false"}}
        after = dict(copy.deepcopy(before), revision=SHA)
        agent.validate_frontend(before, after, SHA)
        for changed in [dict(after, revision=OTHER), dict(after, apiBaseUrl="/api"),
                        dict(after, flags={"RISK": "false", "PREVIEW": "false"}),
                        dict(after, flags={"RISK": "true"})]:
            with self.assertRaises(agent.Blocked):
                agent.validate_frontend(before, changed, SHA)

    def test_commit_identifiers_cannot_inject_commands_or_refs(self):
        for value in ["develop", "a" * 7, "a" * 39 + ";", "a" * 40 + "\n", "$(whoami)"]:
            with self.assertRaises(agent.Blocked):
                agent.sha(value)

    def test_live_database_and_compose_changes_block_before_deploy(self):
        with tempfile.TemporaryDirectory() as temp:
            instance = agent.Agent({"stateDirectory": temp})
            calls = []
            def git(mirror, *args):
                calls.append(args)
                return b"db/changes/new-migration.sql\n" if args[0] == "diff" else b""
            instance.git = git
            with self.assertRaisesRegex(agent.Blocked, "manual release"):
                instance.source_guard({"manualReviewPaths": ["db/changes"]}, Path(temp), OTHER, SHA)
            self.assertEqual(calls[0][:2], ("merge-base", "--is-ancestor"))

    def test_failed_health_check_restores_previous_application(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            config = root / "private.json"
            config.write_text('{"FileStorage":{"Provider":"GoogleDrive"}}')
            compose = root / "compose.yaml"
            compose.write_text("services: {}")
            env = root / ".env"
            env.write_text("AIPMS_IMAGE_TAG=previous\nCOMMIT_SHA=" + OTHER + "\n")
            instance = agent.Agent({"stateDirectory": temp, "privateSettings": str(config)})
            instance.run = lambda *args, **kwargs: b""
            target = {"service": "api", "container": "api", "composeFile": str(compose),
                      "composeHash": agent.digest(compose), "environmentFile": str(env), "imageVariable": "AIPMS_IMAGE_TAG"}
            instance.current = lambda target: OTHER
            instance.inspect = lambda target: {"Id": "same-container", "Config": {"Image": "api:previous"}, "Mounts": []}
            def compose_call(target, *args, **kwargs):
                return json.dumps({"services": {"api": {"image": "api:candidate", "volumes": []}}}).encode()
            instance.compose = compose_call
            checks = iter([False, True])
            instance.ready = lambda *args: next(checks)
            release = root / "release"
            release.mkdir()
            with self.assertRaisesRegex(agent.Blocked, "previous application restored"):
                instance.deploy(target, SHA, "api:candidate", release, OTHER)
            self.assertIn("AIPMS_IMAGE_TAG=previous", env.read_text())
            self.assertFalse((root / "transaction.json").exists())

    def test_interrupted_deployment_is_recovered_on_next_tick(self):
        with tempfile.TemporaryDirectory() as temp:
            instance = agent.Agent({"stateDirectory": temp, "targets": []})
            journal = {"previousRevision": OTHER}
            agent.write_json(Path(temp) / "transaction.json", journal)
            recovered = []
            instance.restore = lambda value: recovered.append(value)
            instance.mirror = lambda target: None
            with self.assertRaises(StopIteration):
                instance.tick()
            self.assertEqual(recovered, [journal])

    def test_other_pending_or_failed_check_blocks_deployment(self):
        with tempfile.TemporaryDirectory() as temp:
            target = {"repository": REPO, "workflow": "backend-ci.yml"}
            check = {"status": "completed", "conclusion": "success"}
            def api(url):
                return {"workflow_runs": [self.run_record()]} if "/actions/" in url else {"total_count": 1, "check_runs": [check]}
            instance = agent.Agent({"stateDirectory": temp}, fetch_json=api)
            self.assertTrue(instance.ci(target, SHA))
            check["status"] = "in_progress"
            self.assertFalse(instance.ci(target, SHA))
            check.update(status="completed", conclusion="failure")
            self.assertFalse(instance.ci(target, SHA))


if __name__ == "__main__":
    unittest.main()
