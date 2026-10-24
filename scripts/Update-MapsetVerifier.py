#!/usr/bin/env python3
"""Select the latest stable upstream release and pin its immutable source archive."""
import hashlib
import json
import os
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parent.parent


def github(path):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "FruitsAtelier-upstream"}
    if os.environ.get("GITHUB_TOKEN"):
        headers["Authorization"] = "Bearer " + os.environ["GITHUB_TOKEN"]
    with urllib.request.urlopen(urllib.request.Request("https://api.github.com/" + path, headers=headers), timeout=60) as response:
        return json.load(response)


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
