#!/usr/bin/env bash
# Generate and exercise the frontend test scenes in an owned validation copy.
set -euo pipefail
source_project="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
[[ $# -eq 2 && "$1" == --workspace ]] || { echo 'Usage: scenes.sh --workspace /absolute/owned/workspace' >&2; exit 2; }
workspace="$(CDPATH= cd -- "$2" && pwd)"
[[ "$workspace" != "$source_project" && -f "$workspace/.mw-frontend-validation-source" ]] || { echo 'Not an isolated frontend validation workspace.' >&2; exit 2; }
[[ "$(< "$workspace/.mw-frontend-validation-source")" == "$source_project" ]] || { echo 'Workspace ownership mismatch.' >&2; exit 2; }
case "$workspace/" in "$source_project/"*) echo 'Workspace must be outside the source project.' >&2; exit 2 ;; esac
rsync -a "$source_project/Assets" "$source_project/Packages" "$source_project/ProjectSettings" "$workspace/"
version="$(sed -n 's/^m_EditorVersion: //p' "$source_project/ProjectSettings/ProjectVersion.txt")"
editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity}"
[[ -x "$editor" ]] || { echo "Unity executable missing: $editor" >&2; exit 2; }
result="$(mktemp -d "$source_project/Tools/FrontendValidation/Evidence/scenes-$(date -u +%Y%m%dT%H%M%SZ).XXXXXX")"
printf '%s\n' "$result" > "$source_project/Tools/FrontendValidation/latest-scenes.txt"
args=(-batchmode -projectPath "$workspace" -job-worker-count 4
  -executeMethod NfsMwRemaster.Driving.Editor.MostWantedFrontendTestSceneSmoke.Run
  -logFile "$result/unity.log")
[[ "${MW_FRONTEND_HEADLESS:-0}" != 1 ]] || args+=(-nographics)
[[ -z "${UNITY_LICENSE_CHANNEL:-}" ]] || args+=(-licensing-client-channel "$UNITY_LICENSE_CHANNEL")
set +e
MW_FRONTEND_SCENE_SMOKE_EVIDENCE="$result" "$editor" "${args[@]}"
status=$?
set -e
printf '%s\n' "$status" > "$result/exit-code.txt"
[[ -s "$result/scene-smoke.json" ]] || { echo "No smoke report. Inspect $result/unity.log" >&2; exit 1; }
python3 - "$result/scene-smoke.json" <<'PY'
import json,sys
report = json.load(open(sys.argv[1]))
print(json.dumps(report, indent=2))
if not report.get('succeeded') or len(report.get('pages', [])) != 26:
    raise SystemExit(1)
PY
[[ "$status" -eq 0 ]] || exit "$status"
printf 'Generated scenes in %s/Assets/NfsMw/Content/Frontend/UI/Tests/Scenes\nEvidence: %s\n' "$workspace" "$result"
