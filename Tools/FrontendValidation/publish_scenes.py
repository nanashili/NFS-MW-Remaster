#!/usr/bin/env python3
"""Copy only new, validated frontend test assets from an owned Unity workspace."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    workspace = args.workspace.resolve(strict=True)
    marker = workspace / ".mw-frontend-validation-source"
    if workspace == root or root in workspace.parents or not marker.is_file() or marker.read_text().strip() != str(root):
        raise RuntimeError("The supplied workspace is not an owned, isolated frontend validation copy.")

    evidence = Path((root / "Tools/FrontendValidation/latest-scenes.txt").read_text().strip())
    report = json.loads((evidence / "scene-smoke.json").read_text())
    if not report.get("succeeded") or len(report.get("pages", [])) != 26:
        raise RuntimeError("A successful 26-page runtime smoke is required before publication.")

    relative = Path("Assets/NfsMw/Content/Frontend/UI/Tests/Scenes")
    source = workspace / relative
    destination = root / relative
    if len(list(source.glob("*.unity"))) != 27:
        raise RuntimeError("Expected the gallery and 26 page-specific Unity scenes.")
    files = [path for path in source.rglob("*") if path.is_file() and path.name != ".DS_Store"]
    if any(path.is_symlink() or source.resolve() not in path.resolve().parents for path in files):
        raise RuntimeError("A generated asset resolves outside its owned test folder.")
    allowed = {".unity", ".asset", ".prefab", ".json", ".wav", ".md", ".meta"}
    if any(path.suffix not in allowed for path in files):
        raise RuntimeError("The generated folder contains an unexpected file type.")

    # Preflight every existing asset before publishing anything. Never replace user edits.
    for path in files:
        target = destination / path.relative_to(source)
        if target.exists() and sha256(path) != sha256(target):
            if path.suffix == ".meta" and path.with_suffix("").is_dir():
                continue  # Existing folder GUIDs are unrelated to the generated object references.
            raise RuntimeError(f"Existing asset differs and will not be replaced: {target.relative_to(root)}")

    created: dict[str, str] = {}
    preserved: list[str] = []
    for path in sorted(files, key=lambda item: (item.suffix != ".meta", len(item.parts), str(item))):
        target = destination / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        try:
            with target.open("xb") as output:
                output.write(path.read_bytes())
        except FileExistsError:
            if path.suffix != ".meta" or not path.with_suffix("").is_dir():
                if sha256(target) != sha256(path):
                    raise RuntimeError(f"An asset changed during publication: {target.relative_to(root)}")
            preserved.append(str(target.relative_to(root)))
            continue
        if sha256(target) != sha256(path):
            raise RuntimeError(f"Publication verification failed: {target.relative_to(root)}")
        created[str(target.relative_to(root))] = sha256(target)

    proof = {"sourceWorkspace": str(workspace), "smokeEvidence": str(evidence.relative_to(root)),
             "sceneCount": 27, "created": created, "preserved": preserved}
    output = evidence / "scene-publication.json"
    with output.open("x", encoding="utf-8") as stream:
        json.dump(proof, stream, indent=2)
        stream.write("\n")
    print(f"Published {len(created)} new files; preserved {len(preserved)} existing files; 27 scenes.")
    print(f"Evidence: {output.relative_to(root)}")


if __name__ == "__main__":
    main()
