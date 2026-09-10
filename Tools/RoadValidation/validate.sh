#!/usr/bin/env bash
set -euo pipefail

# All imports, tests and generated examples run in a fresh copy. Never open the user's live project in batch mode.
validation_script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
validation_source="$(CDPATH= cd -- "$validation_script_dir/../.." && pwd)"
validation_version="$(sed -n 's/^m_EditorVersion: //p' "$validation_source/ProjectSettings/ProjectVersion.txt")"
validation_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$validation_version/Unity.app/Contents/MacOS/Unity}"
validation_mode="${1:---tests}"
if [[ "$validation_mode" != --tests && "$validation_mode" != --player ]]; then
  printf 'Usage: %s [--tests|--player]\n' "$0" >&2
  exit 2
fi
validation_project="$(mktemp -d "${TMPDIR:-/tmp}/nfs-road-validation.XXXXXX")"
printf 'Validation project and logs: %s\n' "$validation_project"
rsync -a "$validation_source/Assets" "$validation_source/Packages" "$validation_source/ProjectSettings" "$validation_project/"
validation_arguments=(-batchmode -nographics -projectPath "$validation_project")
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then
  validation_arguments+=(-licensingIpc "$UNITY_LICENSE_IPC")
fi
"$validation_editor" "${validation_arguments[@]}" -runTests -testPlatform EditMode \
  -testResults "$validation_project/EditMode.xml" -logFile "$validation_project/editmode.log"
if [[ "$validation_mode" == --player ]]; then
  "$validation_editor" "${validation_arguments[@]}" -executeMethod NfsMwRemaster.Driving.Editor.DrivingDemoBuilder.PrepareRoadAuthoringValidation \
    -quit -logFile "$validation_project/prepare.log"
  ROAD_VALIDATION_PLAYER="$validation_project/RoadAuthoring.app" \
    "$validation_editor" "${validation_arguments[@]}" -executeMethod NfsMwRemaster.Driving.Editor.DrivingDemoBuilder.BuildRoadAuthoringPlayer \
    -quit -logFile "$validation_project/build.log"
  validation_executable="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$validation_project/RoadAuthoring.app/Contents/Info.plist")"
  "$validation_project/RoadAuthoring.app/Contents/MacOS/$validation_executable" -batchmode -nographics \
    --road-validation "$validation_project/RoadDriving.json" -logFile "$validation_project/player.log" \
    > "$validation_project/player-console.log" 2>&1
fi
printf 'Validation passed. Evidence retained in %s\n' "$validation_project"
