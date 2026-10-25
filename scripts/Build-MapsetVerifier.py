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
import re

ROOT = Path(__file__).resolve().parent.parent


def runtime():
    system = {"Windows": "win", "Darwin": "osx", "Linux": "linux"}[platform.system()]
    arch = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
    return f"{system}-{arch}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", default=runtime())
    parser.add_argument("--update-locks", action="store_true")
    parser.add_argument("--shared-runtime", type=Path, help="Editor publish directory whose bundled runtime the launcher must use")
    args = parser.parse_args()
    if not re.fullmatch(r"(win|osx|linux)-(x64|arm64)", args.runtime):
        raise ValueError("Unsupported runtime identifier")
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
    output = ROOT / "artifacts/mapset-verifier" / (args.runtime + ("-shared" if args.shared_runtime else ""))
    if output.exists():
        if not output.resolve().is_relative_to((ROOT / "artifacts/mapset-verifier").resolve()):
            raise RuntimeError("Engine output must stay inside artifacts/mapset-verifier")
        shutil.rmtree(output)
    host = ROOT / "tools/MapsetVerifier.Host"
    locks = host / "upstream-locks" / args.runtime
    for project in ("MapsetVerifier.Logging", "MapsetVerifier.Parser", "MapsetVerifier.Framework", "MapsetVerifier.Checks"):
        lock = locks / (project + ".lock.json")
        if lock.exists():
            shutil.copy2(lock, source / project / f"packages.{args.runtime}.lock.json")
    command = ["dotnet", "publish", str(host / "MapsetVerifier.Host.csproj"), "-c", "Release", "-r", args.runtime,
               "--self-contained", "false" if args.shared_runtime else "true", "-o", str(output), f"-p:MapsetVerifierSourceRoot={source}",
               f"-p:RestorePackagesPath={ROOT / 'artifacts/packages'}", "-p:LangVersion=14",
               f"-p:DirectoryBuildTargetsPath={host / 'Upstream.Build.targets'}",
               "-p:DebugType=none", "-p:DebugSymbols=false",
               f"-p:NuGetLockFilePath=packages.{args.runtime}.lock.json"]
    if args.shared_runtime:
        editor = args.shared_runtime.resolve()
        configs = list(editor.glob("FruitsAtelier.*.runtimeconfig.json"))
        configs = [p for p in configs if p.stem in ("FruitsAtelier.App.runtimeconfig", "FruitsAtelier.Mac.runtimeconfig")]
        if len(configs) != 1:
            raise RuntimeError("Expected one published editor runtime configuration")
        frameworks = json.loads(configs[0].read_text(encoding="utf-8-sig"))["runtimeOptions"]["includedFrameworks"]
        runtime_version = next(f["version"] for f in frameworks if f["name"] == "Microsoft.NETCore.App")
        command.extend(["-p:UseAppHost=false", f"-p:RuntimeFrameworkVersion={runtime_version}"])
    command.append("-p:RestoreForceEvaluate=true" if args.update_locks else "-p:RestoreLockedMode=true")
    subprocess.run(command, cwd=ROOT, check=True)
    if args.update_locks:
        locks.mkdir(parents=True, exist_ok=True)
        for project in ("MapsetVerifier.Logging", "MapsetVerifier.Parser", "MapsetVerifier.Framework", "MapsetVerifier.Checks"):
            shutil.copy2(source / project / f"packages.{args.runtime}.lock.json", locks / (project + ".lock.json"))
    shutil.copy2(ROOT / "eng/mapset-verifier.json", output / "upstream.json")
    shutil.copy2(source / "LICENSE", output / "LICENSE.MapsetVerifier.txt")
    if args.shared_runtime:
        launcher = ROOT / "tools/MapsetVerifier.Launcher/MapsetVerifier.Launcher.csproj"
        launcher_output = ROOT / "artifacts/mapset-verifier" / (args.runtime + "-launcher")
        subprocess.run(["dotnet", "publish", str(launcher), "-c", "Release", "-r", args.runtime,
                        "--self-contained", "true", "-o", str(launcher_output), f"-p:RuntimeFrameworkVersion={runtime_version}",
                        "-p:RestoreForceEvaluate=true" if args.update_locks else "-p:RestoreLockedMode=true"], cwd=ROOT, check=True)
        for suffix in (".exe" if args.runtime.startswith("win-") else "", ".dll", ".deps.json", ".runtimeconfig.json"):
            name = "FruitsAtelier.MapsetVerifier.Launcher" + suffix
            shutil.copy2(launcher_output / name, editor / name)
        print(f"Mapset Verifier uses editor .NET {runtime_version} through its separate launcher process", flush=True)

    source_name = f"FruitsAtelier-MapsetVerifier-{pin['tag']}-source.zip"
    source_output = ROOT / "artifacts/mapset-verifier" / source_name
    source_paths = [ROOT / name for name in ("Directory.Build.props", "global.json", "LICENSE", "scripts/Build-MapsetVerifier.py", "eng/mapset-verifier.json")]
    for directory in (host, ROOT / "tools/MapsetVerifier.Launcher"):
        source_paths.extend(p for p in directory.rglob("*") if p.is_file() and not set(p.relative_to(directory).parts).intersection({"bin", "obj"}))
    with zipfile.ZipFile(source_output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as corresponding:
        corresponding.write(archive, archive.relative_to(ROOT).as_posix())
        for path in sorted(set(source_paths)):
            corresponding.write(path, path.relative_to(ROOT).as_posix())
    (output / "SOURCE.txt").write_text(
        f"Mapset Verifier {pin['tag']} ({pin['commit']})\n"
        f"Corresponding source, adapter and build materials: {source_name}\n"
        "Download the source attachment alongside the binary at https://github.com/frankhjwx/FruitsAtelier/releases\n"
        "Extract it and run: python scripts/Build-MapsetVerifier.py\n", encoding="utf-8")
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
    print(f"Corresponding source attachment: {source_output}", flush=True)


if __name__ == "__main__":
    main()
