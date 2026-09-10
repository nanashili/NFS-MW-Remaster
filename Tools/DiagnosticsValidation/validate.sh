#!/usr/bin/env bash
set -euo pipefail
validation_root="$(cd "$(dirname "$0")/../.." && pwd)"
validation_copy="$(mktemp -d "${TMPDIR:-/tmp}/nfs-diagnostics-validation.XXXXXX")"
rsync -a "$validation_root/Assets" "$validation_root/Packages" "$validation_root/ProjectSettings" "$validation_copy/"
validation_unity="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity}"
validation_args=(-batchmode -projectPath "$validation_copy")
if [[ -n "${UNITY_LICENSE_IPC:-}" ]]; then validation_args+=(-licensingIpc "$UNITY_LICENSE_IPC"); fi
printf 'Validation copy: %s\n' "$validation_copy"
"$validation_unity" "${validation_args[@]}" -runTests -testPlatform EditMode -testFilter NfsMwRemaster.Diagnostics.Tests -testResults "$validation_copy/EditMode.xml" -logFile "$validation_copy/EditMode.log"
"$validation_unity" "${validation_args[@]}" -runTests -testPlatform PlayMode -testFilter NfsMwRemaster.Diagnostics.PlayTests -testResults "$validation_copy/PlayMode.xml" -logFile "$validation_copy/PlayMode.log"
