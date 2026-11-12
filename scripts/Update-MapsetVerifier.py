#!/usr/bin/env python3
"""Select the latest stable upstream release and pin its immutable source archive."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
from datetime import datetime, timezone
from functools import cache
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parent.parent


@cache
def github_cli_authenticated():
    return bool(shutil.which("gh")) and subprocess.run(
        ["gh", "auth", "status", "--hostname", "github.com"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30).returncode == 0


def github(path):
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if not token and github_cli_authenticated():
        result = subprocess.run(
            ["gh", "api", "--hostname", "github.com", path],
            capture_output=True, encoding="utf-8", timeout=60)
        if result.returncode:
            raise RuntimeError("Authenticated GitHub CLI request failed: " + result.stderr.strip())
        return json.loads(result.stdout)
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "FruitsAtelier-upstream"}
    if token:
        headers["Authorization"] = "Bearer " + token
    try:
        with urllib.request.urlopen(urllib.request.Request("https://api.github.com/" + path, headers=headers), timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        mode = "authenticated" if token else "unauthenticated"
        detail = f"GitHub {mode} request failed: HTTP {error.code}."
        if error.headers.get("X-RateLimit-Remaining") == "0":
            detail += f" API quota exhausted (limit {error.headers.get('X-RateLimit-Limit', 'unknown')})."
            reset = error.headers.get("X-RateLimit-Reset", "")
            if reset.isdigit():
                detail += " Resets at " + datetime.fromtimestamp(int(reset), timezone.utc).isoformat() + "."
        if not token:
            detail += " Set GH_TOKEN/GITHUB_TOKEN or sign in with gh auth login --hostname github.com."
        error.close()
        raise RuntimeError(detail) from None


def main():
    path = ROOT / "eng/mapset-verifier.json"
    pin = json.loads(path.read_text(encoding="utf-8"))
    repository = pin["repository"]
    release = github(f"repos/{repository}/releases/latest")
    if release["draft"] or release["prerelease"]:
        raise RuntimeError("Expected a stable release")
    tag = release["tag_name"]
    ref = github(f"repos/{repository}/git/ref/tags/{tag}")["object"]
    for _ in range(10):
        if ref["type"] == "commit":
            break
        if ref["type"] != "tag":
            raise RuntimeError("Release tag does not resolve to a commit")
        ref = github(f"repos/{repository}/git/tags/{ref['sha']}")["object"]
    else:
        raise RuntimeError("Too many nested release tags")
    commit = ref["sha"]
    changed = tag != pin["tag"] or commit != pin["commit"]
    if changed:
        with urllib.request.urlopen(f"https://codeload.github.com/{repository}/zip/{commit}", timeout=120) as archive:
            digest = hashlib.sha256(archive.read()).hexdigest()
        path.write_text(json.dumps(dict(repository=repository, tag=tag, commit=commit, sourceSha256=digest), indent=2) + "\n", encoding="utf-8")
    print(f"Mapset Verifier {tag} ({commit}): {'update prepared' if changed else 'already pinned'}")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"changed={str(changed).lower()}\ntag={tag}\n")


if __name__ == "__main__":
    main()
