#!/usr/bin/env bash
set -euo pipefail
route_script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
route_source="$(CDPATH= cd -- "$route_script_dir/../.." && pwd)"
route_version="$(sed -n 's/^m_EditorVersion: //p' "$route_source/ProjectSettings/ProjectVersion.txt")"
route_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$route_version/Unity.app/Contents/MacOS/Unity}"
route_project="$(mktemp -d "${TMPDIR:-/tmp}/nfs-route-validation.XXXXXX")"
printf 'Isolated project: %s\n' "$route_project"
rsync -a "$route_source/Assets" "$route_source/Packages" "$route_source/ProjectSettings" "$route_project/"
route_arguments=(-batchmode -nographics -projectPath "$route_project")
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then route_arguments+=(-licensingIpc "$UNITY_LICENSE_IPC"); fi
"$route_editor" "${route_arguments[@]}" -runTests -testPlatform EditMode -testFilter NfsMwRemaster.Driving.Tests.RaceRouteTests -testResults "$route_project/Routes.xml" -logFile "$route_project/tests.log"
printf 'Results retained in %s\n' "$route_project"
