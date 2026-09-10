"""Repeat the full local publication and prove that outputs remain unchanged.

Only this tool's owned source/output directories are inspected. Existing files
are not cleaned or reset. The resulting evidence records a repeat publication,
not a Unity runtime test or proof of source-archive redistribution rights.
"""

from __future__ import annotations

import contextlib
import io
import json

import audit_capture
import font_metrics
from archive_decode import sha256
from decode_frontend import ASSETS, PROJECT, TOOLS, capture, guarded, immutable_write, json_bytes


def snapshot() -> dict[str, dict]:
    result: dict[str, dict] = {}
    for root in (ASSETS, TOOLS / "Source", TOOLS / "Evidence/local-20260908"):
        for path in sorted(root.rglob("*")):
            path = guarded(path, root)
            if not path.is_file():
                continue
            if path.name.startswith("repeat-"):
                continue
            data = path.read_bytes()
            result[path.relative_to(PROJECT).as_posix()] = {
                "sha256": sha256(data), "size": len(data), "mtimeNs": path.stat().st_mtime_ns,
            }
    return result


def main() -> None:
    before = snapshot()
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        report = capture(True, "local-20260908")
        font_metrics.main()
        audit_capture.main()
    after = snapshot()
    changed = [path for path in sorted(set(before) | set(after)) if before.get(path) != after.get(path)]
    if changed:
        raise RuntimeError("Repeat publication changed files or modification times: " + repr(changed))
    evidence = {
        "schemaVersion": 1, "generator": "nfsmw-frontend-decoding/v1",
        "verification": "Full decoding, bitmap-font publication and image/source validation repeated",
        "unchangedFiles": len(before), "changedFiles": [], "summary": report["summary"],
        "snapshotSha256": sha256(json_bytes(before)), "files": before,
        "verificationCodeSha256": sha256(guarded(TOOLS / "verify_repeat.py", TOOLS).read_bytes()),
        "log": output.getvalue(),
    }
    payload = json_bytes(evidence)
    path = TOOLS / "Evidence/local-20260908" / ("repeat-" + sha256(payload)[:16] + ".json")
    immutable_write(path, payload, TOOLS)
    print("REPEAT_VERIFIED", len(before), "files retain identical bytes, sizes and modification times")
    print("EVIDENCE", path.relative_to(PROJECT))
    print("SUMMARY", json.dumps(report["summary"], sort_keys=True))


if __name__ == "__main__":
    main()
