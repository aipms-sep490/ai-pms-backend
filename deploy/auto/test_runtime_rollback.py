"""Opt-in real Docker test, isolated from AIPMS containers, networks and SQL."""
import importlib.util
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import uuid

spec = importlib.util.spec_from_file_location("deploy_agent", Path(__file__).with_name("agent.py"))
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)
os.umask(0o077)
identifier = "aipms-auto-test-" + uuid.uuid4().hex[:10]
old_sha, new_sha = "a" * 40, "b" * 40
with socket.socket() as socket_probe:
    socket_probe.bind(("127.0.0.1", 0))
    port = socket_probe.getsockname()[1]
with tempfile.TemporaryDirectory(prefix=identifier) as temp:
    root = Path(temp)
    (root / "html").mkdir()
    (root / "html/deployment.json").write_text(json.dumps({"revision": old_sha, "apiBaseUrl": "https://fixture.invalid/api/v1", "flags": {"RISK": "true"}}))
    os.chmod(root / "html", 0o755)
    os.chmod(root / "html/deployment.json", 0o644)
    (root / "Dockerfile").write_text("FROM nginx:alpine\nCOPY html/ /usr/share/nginx/html/\n")
    (root / "Bad.Dockerfile").write_text('FROM nginx:alpine\nCMD ["sh", "-c", "exit 1"]\n')
    compose = root / "compose.yaml"
    compose.write_text("name: " + identifier + "\nservices:\n  frontend:\n    image: " + identifier + ":${TEST_TAG}\n    container_name: " + identifier
                       + '\n    ports: ["127.0.0.1:' + str(port) + ':80"]\n    healthcheck:\n      test: ["CMD", "wget", "-q", "-O", "/dev/null", "http://127.0.0.1/deployment.json"]\n      interval: 1s\n      timeout: 1s\n      retries: 3\n')
    env_path = root / ".env"
    env_path.write_text("TEST_TAG=old\nPRIVATE_SENTINEL=keep-this\n")
    private = root / "private.json"
    private.write_text('{"FileStorage":{"Provider":"GoogleDrive"}}')
    config = {"stateDirectory": str(root / "state"), "privateSettings": str(private),
              "frontendProfile": {"apiBaseUrl": "https://fixture.invalid/api/v1", "flags": {"RISK": "true"}}}
    target = {"service": "frontend", "container": identifier, "environmentFile": str(env_path),
              "composeFile": str(compose), "composeHash": agent.digest(compose), "imageVariable": "TEST_TAG",
              "localMetadataUrl": "http://127.0.0.1:" + str(port) + "/deployment.json"}
    instance = agent.Agent(config)
    compose_command = ["/usr/bin/docker-compose", "--env-file", str(env_path), "-f", str(compose)]
    try:
        agent.command(["docker", "build", "-t", identifier + ":old", str(root)], log=root / "old-build.log")
        agent.command(["docker", "build", "-f", str(root / "Bad.Dockerfile"), "-t", identifier + ":bad", str(root)], log=root / "bad-build.log")
        agent.command(compose_command + ["up", "-d", "--no-build"], log=root / "start.log")
        if not instance.ready(target, old_sha, identifier + ":old", timeout=30):
            raise RuntimeError("Fixture did not become ready")
        release = root / "state/releases/fixture"
        release.mkdir(parents=True)
        try:
            instance.deploy(target, new_sha, identifier + ":bad", release, old_sha)
            raise RuntimeError("Broken candidate was incorrectly accepted")
        except agent.Blocked as error:
            if str(error) != "Candidate failed; previous application restored":
                raise
        if not instance.ready(target, old_sha, identifier + ":old", timeout=30):
            raise RuntimeError("Previous image was not recovered")
        if env_path.read_text() != "TEST_TAG=old\nPRIVATE_SENTINEL=keep-this\n":
            raise RuntimeError("Rollback changed deployment secrets")
        print(json.dumps({"passed": True, "realDocker": True, "brokenCandidateRejected": True,
                          "oldImageHealthy": True, "previousRevisionRestored": True, "privateEnvironmentPreserved": True,
                          "productionContainersTouched": False, "checkedAt": agent.now()}))
    finally:
        subprocess.run(compose_command + ["down"], capture_output=True)
        subprocess.run(["docker", "image", "rm", identifier + ":old", identifier + ":bad"], capture_output=True)
