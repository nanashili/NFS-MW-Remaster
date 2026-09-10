#!/usr/bin/env python3
"""Compile frontend changes with Unity's installed Roslyn and generated references.

Writes fresh assemblies and evidence only to a new directory. It never launches
Unity or replaces the live project's Library assemblies. This is compilation
evidence, not a substitute for Unity's test runner or a player build.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def assembly_sources(root: Path, assembly_name: str) -> list[Path]:
    definitions = {}
    names_by_guid = {}
    for definition in (root / "Assets").rglob("*.asmdef"):
        name = json.loads(definition.read_text())["name"]
        definitions[definition.parent] = name
        metadata = definition.with_suffix(".asmdef.meta").read_text()
        guid = next(line.split(": ", 1)[1] for line in metadata.splitlines() if line.startswith("guid: "))
        names_by_guid[guid] = name
    for reference in (root / "Assets").rglob("*.asmref"):
        target = json.loads(reference.read_text())["reference"]
        definitions[reference.parent] = names_by_guid[target[5:]] if target.startswith("GUID:") else target
    sources = []
    for path in (root / "Assets").rglob("*.cs"):
        owner = next((definitions[parent] for parent in path.parents if parent in definitions), None)
        if owner == assembly_name:
            sources.append(path.relative_to(root))
    return sorted(sources)


def find_response(root: Path, name: str, player: bool) -> Path:
    candidates = []
    for path in (root / "Library/Bee/artifacts").glob(f"*/{name}.rsp"):
        text = path.read_text()
        editor_defines = any(line in ("-define:UNITY_EDITOR", "/define:UNITY_EDITOR") for line in text.splitlines())
        if editor_defines != player:
            candidates.append(path)
    if not candidates:
        raise RuntimeError(f"No {'player' if player else 'Editor'} response file for {name}. Import the project in Unity first.")
    return max(candidates, key=lambda path: path.stat().st_mtime_ns)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", action="store_true", help="Compile with cached player defines and current imported package references (not a player build).")
    parser.add_argument("--editor-tools", action="store_true", help="Also compile the downstream Editor tooling assembly.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    version = next(line.split(": ", 1)[1] for line in (root / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()
                   if line.startswith("m_EditorVersion: "))
    resources = Path(os.environ.get("UNITY_RESOURCES", f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/Resources"))
    dotnet = resources / "Scripting/NetCoreRuntime/dotnet"
    compilers = sorted((resources / "Scripting/DotNetSdk/sdk").glob("*/Roslyn/bincore/csc.dll"))
    if not dotnet.is_file() or not compilers:
        raise RuntimeError("The installed Unity .NET runtime/compiler was not found. Set UNITY_RESOURCES to its Resources directory.")

    evidence = root / "Tools/FrontendValidation/Evidence"
    evidence.mkdir(parents=True, exist_ok=True)
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    output = Path(tempfile.mkdtemp(prefix=f"{'player' if args.player else 'editor'}-compile-{stamp}-", dir=evidence))
    (root / "Tools/FrontendValidation/latest-compile.txt").write_text(str(output.relative_to(root)) + "\n")
    names = ["NfsMwRemaster.Driving.Contracts", "NfsMwRemaster.Driving.GameFlow",
             "NfsMwRemaster.Driving", "NfsMwRemaster.Driving.Rendering.Runtime"]
    if not args.player:
        if args.editor_tools:
            names.append("NfsMwRemaster.Driving.Editor")
        names += ["NfsMwRemaster.Driving.Frontend.Tests", "NfsMwRemaster.Driving.GameFlow.Tests"]

    built = {}
    report = {"unityVersion": version, "playerDefines": args.player, "assemblies": [], "succeeded": False}
    definitions = {data["name"]: data for path in (root / "Assets").rglob("*.asmdef")
                   for data in [json.loads(path.read_text())]}
    for name in names:
        fallback_defines = None
        # A player's cache can predate the URP-to-HDRP or assembly migration. Keep the
        # current imported package references and vary only platform compilation symbols.
        original = find_response(root, name, False)
        if args.player:
            player_response = find_response(root, "NfsMwRemaster.Driving", True)
            fallback_defines = [line for line in player_response.read_text().splitlines()
                                if line.startswith(("-define:", "/define:"))]
        lines = []
        referenced_built = set()
        for line in original.read_text().splitlines():
            if line.startswith(("-out:", "-refout:", "/out:", "/refout:")):
                continue
            unquoted = line.strip('"')
            if unquoted.endswith(".cs") and not line.startswith(("-", "/")):
                continue
            if fallback_defines is not None and line.startswith(("-define:", "/define:")):
                continue
            if args.player and line.startswith(("-r:", "/reference:")) and "UnityEditor" in line:
                continue
            if line.startswith(("-r:", "/reference:")):
                for dependency, replacement in built.items():
                    if line.endswith((f"/{dependency}.ref.dll\"", f"/{dependency}.dll\"")):
                        line = f'-r:"{replacement}"'
                        referenced_built.add(dependency)
                        break
            lines.append(line)
        if fallback_defines is not None:
            lines.extend(fallback_defines)
        # Refresh project dependencies from the actual asmdef, not a stale player's cache.
        for dependency in definitions[name].get("references", []):
            if dependency in built and dependency not in referenced_built:
                lines.append(f'-r:"{built[dependency]}"')
        sources = assembly_sources(root, name)
        if not sources:
            raise RuntimeError(f"No source files found for {name}.")
        source_hashes = {str(path): hashlib.sha256((root / path).read_bytes()).hexdigest() for path in sources}
        lines.extend(f'"{path.as_posix()}"' for path in sources)
        refout = output / f"{name}.ref.dll"
        lines += [f'-out:"{output / (name + ".dll")}"', f'-refout:"{refout}"']
        response = output / f"{name}.rsp"
        response.write_text("\n".join(lines) + "\n")
        log = output / f"{name}.log"
        with log.open("w") as stream:
            result = subprocess.run([str(dotnet), str(compilers[-1]), "@" + str(response)], cwd=root,
                                    stdout=stream, stderr=subprocess.STDOUT, check=False)
        sources_changed = assembly_sources(root, name) != sources or any(
            not (root / path).is_file() or hashlib.sha256((root / path).read_bytes()).hexdigest() != source_hashes[str(path)]
            for path in sources)
        entry = {"assembly": name, "exitCode": result.returncode, "responseSource": str(original.relative_to(root)),
                 "validationKind": "player-symbol compilation with imported references" if args.player else "Editor compilation",
                 "sourceSha256": source_hashes, "sourcesChangedDuringCompile": sources_changed,
                 "log": str(log.relative_to(root))}
        report["assemblies"].append(entry)
        print(f"{name}: exit {result.returncode}, {len(sources)} source files", flush=True)
        if result.returncode or sources_changed:
            print(log.read_text(), flush=True)
            if sources_changed:
                print("Source files changed during compilation. Rerun against a stable revision.", flush=True)
            (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
            print(f"Evidence: {output}")
            return 1
        built[name] = refout
    report["succeeded"] = True
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"Evidence: {output}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Frontend compilation failed: {error}", file=sys.stderr)
        sys.exit(2)
