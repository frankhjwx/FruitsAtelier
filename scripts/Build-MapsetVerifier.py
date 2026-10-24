#!/usr/bin/env python3
"""Prepare the pinned, standalone Mapset Verifier engine. Never selects a newer release."""
import argparse
import hashlib
import json
import os
import platform
from pathlib import Path
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def runtime():
    system = {"Windows": "win", "Darwin": "osx", "Linux": "linux"}[platform.system()]
    arch = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
    return f"{system}-{arch}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", default=runtime())
    parser.add_argument("--update-locks", action="store_true")
    args = parser.parse_args()
    pin = json.loads((ROOT / "eng/mapset-verifier.json").read_text(encoding="utf-8"))
    cache = ROOT / "artifacts/upstream"
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / f"mapset-verifier-{pin['commit']}.zip"
    if not archive.exists():
        print(f"Downloading Mapset Verifier {pin['tag']} ({pin['commit']})", flush=True)
        with urllib.request.urlopen(f"https://codeload.github.com/{pin['repository']}/zip/{pin['commit']}", timeout=120) as source, archive.open("wb") as target:
            shutil.copyfileobj(source, target)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != pin["sourceSha256"]:
        raise RuntimeError(f"Source archive hash mismatch: {archive}")
    source = cache / f"MapsetVerifier-{pin['commit']}"
    if not source.exists():
        with zipfile.ZipFile(archive) as package:
            for name in package.namelist():
                if not (cache / name).resolve().is_relative_to(cache.resolve()):
                    raise RuntimeError("Invalid source archive path")
            package.extractall(cache)
    output = ROOT / "artifacts/mapset-verifier" / args.runtime
    host = ROOT / "tools/MapsetVerifier.Host"
    locks = host / "upstream-locks" / args.runtime
    for project in ("MapsetVerifier.Logging", "MapsetVerifier.Parser", "MapsetVerifier.Framework", "MapsetVerifier.Checks"):
        lock = locks / (project + ".lock.json")
        if lock.exists():
            shutil.copy2(lock, source / project / f"packages.{args.runtime}.lock.json")
    command = ["dotnet", "publish", str(host / "MapsetVerifier.Host.csproj"), "-c", "Release", "-r", args.runtime,
               "--self-contained", "true", "-o", str(output), f"-p:MapsetVerifierSourceRoot={source}",
               f"-p:RestorePackagesPath={ROOT / 'artifacts/packages'}", "-p:LangVersion=14",
               f"-p:NuGetLockFilePath=packages.{args.runtime}.lock.json"]
    command.append("-p:RestoreForceEvaluate=true" if args.update_locks else "-p:RestoreLockedMode=true")
    subprocess.run(command, cwd=ROOT, check=True)
    if args.update_locks:
        locks.mkdir(parents=True, exist_ok=True)
        for project in ("MapsetVerifier.Logging", "MapsetVerifier.Parser", "MapsetVerifier.Framework", "MapsetVerifier.Checks"):
            shutil.copy2(source / project / f"packages.{args.runtime}.lock.json", locks / (project + ".lock.json"))
    shutil.copy2(ROOT / "eng/mapset-verifier.json", output / "upstream.json")
    shutil.copy2(source / "LICENSE", output / "LICENSE.MapsetVerifier.txt")
    corresponding = output / "source"
    corresponding.mkdir(exist_ok=True)
    shutil.copy2(archive, corresponding / archive.name)
    for directory in ("tools/MapsetVerifier.Host", "scripts", "eng"):
        destination = corresponding / directory
        destination.mkdir(parents=True, exist_ok=True)
        paths = [host / name for name in ("Program.cs", "MapsetVerifier.Host.csproj", "README.md", "LICENSE")] + list(host.glob("packages.*.lock.json"))
        if directory == "scripts": paths = [ROOT / "scripts/Build-MapsetVerifier.py"]
        if directory == "eng": paths = [ROOT / "eng/mapset-verifier.json"]
        for path in paths:
            shutil.copy2(path, destination / path.name)
    shutil.copytree(host / "upstream-locks", corresponding / "tools/MapsetVerifier.Host/upstream-locks", dirs_exist_ok=True)
    # Retain metadata and available license texts for the actual dependency closure.
    assets = json.loads((host / "obj/project.assets.json").read_text(encoding="utf-8-sig"))
    for library in assets["libraries"].values():
        if library["type"] != "package": continue
        package = ROOT / "artifacts/packages" / library["path"]
        destination = output / "licenses" / library["path"].replace("/", "-")
        for path in package.glob("*"):
            if path.is_file() and (path.suffix == ".nuspec" or any(word in path.name.lower() for word in ("license", "licence", "notice", "copying"))):
                destination.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, destination / path.name)
    print(f"Mapset Verifier {pin['tag']} ready: {output}", flush=True)


if __name__ == "__main__":
    main()
