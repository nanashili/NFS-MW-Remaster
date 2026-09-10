#!/usr/bin/env bash
set -euo pipefail
workspace_script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
workspace_source="$(CDPATH= cd -- "$workspace_script_dir/../.." && pwd)"
workspace_version="$(sed -n 's/^m_EditorVersion: //p' "$workspace_source/ProjectSettings/ProjectVersion.txt")"
workspace_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$workspace_version/Unity.app/Contents/MacOS/Unity}"
workspace_project="$(mktemp -d "${TMPDIR:-/tmp}/nfs-workspace-validation.XXXXXX")"
printf 'Isolated project: %s\n' "$workspace_project"
rsync -a "$workspace_source/Assets" "$workspace_source/Packages" "$workspace_source/ProjectSettings" "$workspace_project/"
workspace_arguments=(-batchmode -nographics -projectPath "$workspace_project")
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then workspace_arguments+=(-licensingIpc "$UNITY_LICENSE_IPC"); fi
"$workspace_editor" "${workspace_arguments[@]}" -runTests -testPlatform EditMode -testFilter 'NfsMwRemaster.Driving.Tests.RacingWorkspaceTests;NfsMwRemaster.Driving.Tests.RaceRouteTests;NfsMwRemaster.Driving.Tests.EventPlacementTests;NfsMwRemaster.Driving.Tests.WorldValidationCoreTests' -testResults "$workspace_project/Workspace.xml" -logFile "$workspace_project/tests.log"
test -s "$workspace_project/Workspace.xml"
printf 'Test report: %s/Workspace.xml\n' "$workspace_project"
