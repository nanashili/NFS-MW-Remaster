#!/usr/bin/env bash
set -euo pipefail
audio_root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
audio_version="$(sed -n 's/^m_EditorVersion: //p' "$audio_root/ProjectSettings/ProjectVersion.txt")"
audio_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$audio_version/Unity.app/Contents/MacOS/Unity}"
audio_project="${BLACKBOX_TEST_PROJECT:-$(mktemp -d "${TMPDIR:-/tmp}/black-box-validation.XXXXXX")}"
printf 'Isolated validation project: %s\n' "$audio_project"
if [[ ! -d "$audio_project/Library" && -d "$audio_root/Library" && "$(uname -s)" == Darwin ]]; then
  # APFS copy-on-write preserves the existing import cache without a second live editor.
  cp -cR "$audio_root/Library" "$audio_project/Library"
fi
rsync -a "$audio_root/Assets" "$audio_root/Packages" "$audio_root/ProjectSettings" "$audio_project/"
audio_args=(-batchmode -projectPath "$audio_project")
if [[ "${BLACKBOX_HEADLESS:-0}" == 1 ]]; then audio_args+=(-nographics); fi
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then audio_args+=(-licensing-client-channel "$UNITY_LICENSE_IPC"); fi
"$audio_editor" "${audio_args[@]}" -runTests -testPlatform EditMode \
  -testFilter 'NfsMwRemaster.Driving.Tests.BlackBox;NfsMwRemaster.Driving.Tests.SensoryFeedbackTests;NfsMwRemaster.Driving.Tests.VehicleProfileStudioTests' \
  -testResults "$audio_project/BlackBox.xml" -logFile "$audio_project/black-box.log"
test -s "$audio_project/BlackBox.xml"
printf 'Test results: %s/BlackBox.xml\n' "$audio_project"
"$audio_editor" "${audio_args[@]}" -runTests -testPlatform PlayMode \
  -testFilter 'NfsMwRemaster.Driving.Tests.NativeAudioWorldPlayTests' \
  -testResults "$audio_project/BlackBoxPlay.xml" -logFile "$audio_project/black-box-play.log"
test -s "$audio_project/BlackBoxPlay.xml"
printf 'Play Mode results: %s/BlackBoxPlay.xml\n' "$audio_project"
