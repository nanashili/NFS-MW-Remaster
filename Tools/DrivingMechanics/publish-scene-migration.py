#!/usr/bin/env python3
"""Publish a tested migration copy using hashes, exact backups and atomic writes.

Only existing .asset/.unity files named by the migration manifest are changed.
No .meta, source code, model, material, prefab or build-settings rewrite is allowed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
import xml.etree.ElementTree as ET


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def asset_path(root: Path, relative: str) -> Path:
    candidate = Path(relative)
    if candidate.is_absolute() or ".." in candidate.parts or candidate.parts[:1] != ("Assets",):
        raise ValueError(f"Not an ordinary project asset path: {relative}")
    path = root / candidate
    for parent in (path, *path.parents):
        if parent == root:
            break
        if parent.is_symlink():
            raise ValueError(f"Symbolic-link target is not permitted: {relative}")
    if path.suffix not in {".asset", ".unity"}:
        raise ValueError(f"Unapproved migration file type: {relative}")
    path.resolve().relative_to(root.resolve())
    return path


def scene_non_tuning(text: bytes) -> bytes:
    """Remove only the body of VehicleTuning objects, retaining IDs and all other bytes."""
    pattern = rb"(?ms)(^--- !u!114 &[^\r\n]+\r?\n)(.*?)(?=^--- !u!|\Z)"

    def replace(match: re.Match[bytes]) -> bytes:
        if b"guid: ac296239dbe2842f5a57ca32dba789f7, type: 3" in match.group(2):
            return match.group(1) + b"<VehicleTuning payload>\n"
        return match.group(0)

    return re.sub(pattern, replace, text)


def atomic_write(path: Path, data: bytes) -> None:
    mode = path.stat().st_mode
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile(prefix=".mw-migration-", dir=path.parent, delete=False) as stream:
            temporary = stream.name
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, mode)
        os.replace(temporary, path)
        temporary = None
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--tests", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--apply", action="store_true", help="Without this flag, validate the proposed changes only.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    workspace = args.workspace.resolve()
    marker = workspace / ".mw-driving-validation-source"
    if workspace == root or not marker.is_file() or Path(marker.read_text().strip()).resolve() != root:
        raise ValueError("Workspace is not a validation copy of this project.")
    report_path = args.report.resolve()
    report_path.relative_to(workspace)
    report = json.loads(report_path.read_text())
    if report.get("schema") != 1 or report.get("completed") is not True:
        raise ValueError("Migration did not complete.")
    tests = ET.parse(args.tests).getroot()
    if int(tests.get("failed", "1")) != 0 or int(tests.get("passed", "0")) == 0:
        raise ValueError("A successful test result is required before publication.")
    migration_tests = [case for case in tests.iter("test-case")
                       if "MostWantedSceneMigrationTests" in case.get("fullname", "")
                       or "MostWantedSceneCoverageTests" in case.get("fullname", "")]
    if not migration_tests or any(case.get("result") != "Passed" for case in migration_tests):
        raise ValueError("All migration-specific tests must have passed on the staged project.")
    capture = root / "Tools/DrivingMechanics/Evidence/handling-local-20260908.json"
    if digest(capture.read_bytes()) != report["captureSha256"]:
        raise ValueError("Captured source evidence changed during migration.")
    proposed: list[tuple[Path, bytes, bytes]] = []
    seen: set[str] = set()
    for change in report["changedFiles"]:
        relative = change["path"]
        if relative in seen or change.get("created"):
            raise ValueError("The manifest must contain unique existing assets only.")
        seen.add(relative)
        live = asset_path(root, relative)
        staged = asset_path(workspace, relative)
        before, after = live.read_bytes(), staged.read_bytes()
        if digest(before) != change["beforeSha256"]:
            raise ValueError(f"Live file changed since staging; refusing to overwrite: {relative}")
        if digest(after) != change["afterSha256"]:
            raise ValueError(f"Staged file changed since migration: {relative}")
        backup = (report_path.parent / change["backup"]).resolve()
        backup.relative_to(report_path.parent)
        if backup.read_bytes() != before:
            raise ValueError(f"Backup does not match the original: {relative}")
        if live.suffix == ".unity" and scene_non_tuning(before) != scene_non_tuning(after):
            raise ValueError(f"Scene changes extend beyond tuning payloads: {relative}")
        proposed.append((live, before, after))
    # Source and test code in the staged project must still match what is being delivered.
    for file in (root / "Assets/NfsMw/Modules/Driving").rglob("*"):
        if file.is_file() and file.suffix in {".cs", ".asmdef"}:
            tested = workspace / file.relative_to(root)
            if not tested.is_file() or file.read_bytes() != tested.read_bytes():
                raise ValueError(f"Live source differs from staged source: {file.relative_to(root)}")
    print(f"Preflight passed: {len(proposed)} asset/scene files; migration tests={len(migration_tests)}")
    if not args.apply:
        return
    output = args.output.resolve()
    output.relative_to(root / "Tools/DrivingMechanics")
    if output.exists():
        raise FileExistsError(f"Evidence destination already exists: {output}")
    shutil.copytree(report_path.parent, output)
    shutil.copy2(args.tests, output / "tests.xml")
    log = args.tests.with_name("unity.log")
    if log.is_file():
        shutil.copy2(log, output / "unity.log")
    written: list[tuple[Path, bytes, bytes]] = []
    try:
        for live, before, after in proposed:
            if live.read_bytes() != before:
                raise RuntimeError(f"Concurrent edit detected before publication: {live}")
            atomic_write(live, after)
            written.append((live, before, after))
        for live, _, after in written:
            if live.read_bytes() != after:
                raise RuntimeError(f"Concurrent modification detected after publication: {live}")
    except Exception:
        conflicts = []
        for live, before, after in reversed(written):
            if live.read_bytes() == after:
                atomic_write(live, before)
            else:
                conflicts.append(str(live))
        (output / "publication-failed.json").write_text(json.dumps({"concurrentConflicts": conflicts}, indent=2))
        raise
    publication = {"completed": True, "testCounts": tests.attrib,
                   "changedFiles": report["changedFiles"],
                   "preservedSceneNonTuningBytes": True,
                   "preservedAssetGuidsAndMetadata": True}
    (output / "publication.json").write_text(json.dumps(publication, indent=2) + "\n")
    print(f"Published {len(written)} files. Original backups and test evidence: {output}")


if __name__ == "__main__":
    main()
