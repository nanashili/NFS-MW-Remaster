#!/usr/bin/env bash
set -euo pipefail
validation_project="${1:?Pass a disposable Unity validation project containing the current sources}"
unity_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity}"
script_directory="$(cd "$(dirname "$0")" && pwd)"
workspace_directory="$(cd "$script_directory/../.." && pwd)"
validation_project="$(cd "$validation_project" && pwd)"
if [[ "$validation_project" == "$workspace_directory" ]]; then
  echo 'Use a disposable validation copy, not the working project.' >&2
  exit 1
fi
licensing_arguments=()
if [[ -n "${UNITY_LICENSING_IPC:-}" ]]; then licensing_arguments=(-licensingIpc "$UNITY_LICENSING_IPC"); fi
"$unity_editor" -batchmode -nographics -projectPath "$validation_project" "${licensing_arguments[@]}" \
  -runTests -testPlatform EditMode -testFilter GrimePainterTests \
  -testResults "$script_directory/Evidence/focused.xml" -logFile "$script_directory/Evidence/focused.log"
