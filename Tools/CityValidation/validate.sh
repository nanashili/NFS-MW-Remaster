#!/usr/bin/env bash
set -euo pipefail
city_script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
city_source="$(CDPATH= cd -- "$city_script_dir/../.." && pwd)"
city_version="$(sed -n 's/^m_EditorVersion: //p' "$city_source/ProjectSettings/ProjectVersion.txt")"
city_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$city_version/Unity.app/Contents/MacOS/Unity}"
city_project="$(mktemp -d "${TMPDIR:-/tmp}/nfs-city-validation.XXXXXX")"
printf 'Isolated project: %s\n' "$city_project"
rsync -a "$city_source/Assets" "$city_source/Packages" "$city_source/ProjectSettings" "$city_project/"
city_arguments=(-batchmode -nographics -projectPath "$city_project")
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then city_arguments+=(-licensingIpc "$UNITY_LICENSE_IPC"); fi
"$city_editor" "${city_arguments[@]}" -runTests -testPlatform EditMode -testFilter NfsMwRemaster.Driving.Tests.CityBuilderTests -testResults "$city_project/City.xml" -logFile "$city_project/tests.log"
"$city_editor" "${city_arguments[@]}" -quit -executeMethod NfsMwRemaster.Driving.Editor.CityValidationSmoke.Run -logFile "$city_project/map-smoke.log"
printf 'Passed. Results, map report and example scene are retained in %s\n' "$city_project"
