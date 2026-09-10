#!/usr/bin/env bash
set -euo pipefail

# The live project is read-only. All imports, scene changes, reports and builds belong to a fresh copy.
racing_script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
racing_source="$(CDPATH= cd -- "$racing_script_dir/../.." && pwd)"
racing_version="$(sed -n 's/^m_EditorVersion: //p' "$racing_source/ProjectSettings/ProjectVersion.txt")"
racing_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$racing_version/Unity.app/Contents/MacOS/Unity}"
racing_mode="${1:---tests}"
if [[ "$racing_mode" != --tests && "$racing_mode" != --build-mac ]]; then
  printf 'Usage: %s [--tests|--build-mac]\n' "$0" >&2
  exit 2
fi
racing_project="$(mktemp -d "${TMPDIR:-/tmp}/nfs-racing-line-validation.XXXXXX")"
printf 'Disposable project and evidence: %s\n' "$racing_project"
rsync -a "$racing_source/Assets" "$racing_source/Packages" "$racing_source/ProjectSettings" "$racing_project/"
racing_arguments=(-batchmode -nographics -projectPath "$racing_project")
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then
  racing_arguments+=(-licensingIpc "$UNITY_LICENSE_IPC")
fi
"$racing_editor" "${racing_arguments[@]}" -executeMethod NfsMwRemaster.Driving.Editor.RacingLineStudioDemo.ValidateExamples \
  -quit -logFile "$racing_project/examples.log"
"$racing_editor" "${racing_arguments[@]}" -runTests -testPlatform EditMode \
  -testFilter NfsMwRemaster.Driving.Tests \
  -testResults "$racing_project/EditMode.xml" -logFile "$racing_project/editmode.log"
if [[ "$racing_mode" == --build-mac ]]; then
  "$racing_editor" "${racing_arguments[@]}" -executeMethod NfsMwRemaster.Driving.Editor.RacingLineStudioBuild.BuildMacExample \
    -quit -logFile "$racing_project/build-mac.log"
fi
printf 'Qualification succeeded. Keep the logs, XML, and Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated reports in %s\n' "$racing_project"
