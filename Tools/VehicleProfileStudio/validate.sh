#!/usr/bin/env bash
set -euo pipefail
vehicle_script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
vehicle_source="$(CDPATH= cd -- "$vehicle_script_dir/../.." && pwd)"
vehicle_version="$(sed -n 's/^m_EditorVersion: //p' "$vehicle_source/ProjectSettings/ProjectVersion.txt")"
vehicle_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$vehicle_version/Unity.app/Contents/MacOS/Unity}"
vehicle_project="$(mktemp -d "${TMPDIR:-/tmp}/vehicle-studio-validation.XXXXXX")"
printf 'Isolated validation project: %s\n' "$vehicle_project"
rsync -a "$vehicle_source/Assets" "$vehicle_source/Packages" "$vehicle_source/ProjectSettings" "$vehicle_project/"
vehicle_args=(-batchmode -nographics -projectPath "$vehicle_project")
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then vehicle_args+=(-licensingIpc "$UNITY_LICENSE_IPC"); fi
"$vehicle_editor" "${vehicle_args[@]}" -runTests -testPlatform EditMode \
  -testFilter 'NfsMwRemaster.Driving.Tests.VehicleProfileStudioTests;NfsMwRemaster.Driving.Tests.VehicleCustomizationTests;NfsMwRemaster.Driving.Tests.RacingWorkspaceTests' \
  -testResults "$vehicle_project/VehicleProfiles.xml" -logFile "$vehicle_project/vehicle-profiles.log"
test -s "$vehicle_project/VehicleProfiles.xml"
printf 'Results: %s/VehicleProfiles.xml\n' "$vehicle_project"
