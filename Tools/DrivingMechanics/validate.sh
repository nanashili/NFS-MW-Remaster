#!/usr/bin/env bash
# Validate in a separate project. Never launch tests against the user's live editor project.
set -euo pipefail
source_project="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
workspace=""
prepare_only=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --workspace) [[ $# -ge 2 ]] || { echo '--workspace needs a path' >&2; exit 2; }; workspace="$2"; shift 2 ;;
    --prepare-only) prepare_only=true; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [[ -z "$workspace" ]]; then
  workspace="$(mktemp -d "${TMPDIR:-/tmp}/mw-driving-validation.XXXXXX")"
  printf '%s\n' "$source_project" > "$workspace/.mw-driving-validation-source"
  for directory in Assets Packages ProjectSettings Library; do
    [[ -d "$source_project/$directory" ]] || continue
    if [[ "$(uname -s)" == Darwin ]]; then
      cp -cR "$source_project/$directory" "$workspace/$directory"
    else
      mkdir -p "$workspace/$directory"
      rsync -a "$source_project/$directory/" "$workspace/$directory/"
    fi
  done
else
  workspace="$(CDPATH= cd -- "$workspace" && pwd)"
  [[ "$workspace" != "$source_project" && -f "$workspace/.mw-driving-validation-source" ]] || { echo 'Not a task-owned isolated validation workspace.' >&2; exit 2; }
  [[ "$(< "$workspace/.mw-driving-validation-source")" == "$source_project" ]] || { echo 'Workspace belongs to a different source project.' >&2; exit 2; }
  case "$workspace/" in "$source_project/"*) echo 'Validation workspace must be outside the source project.' >&2; exit 2 ;; esac
  rsync -a "$source_project/Assets" "$source_project/Packages" "$source_project/ProjectSettings" "$workspace/"
fi
mkdir -p "$workspace/Tools/DrivingMechanics/Evidence" "$source_project/Library/DrivingMechanicsWork"
cp "$source_project/Tools/DrivingMechanics/Evidence/handling-local-20260908.json" "$workspace/Tools/DrivingMechanics/Evidence/"
mkdir -p "$workspace/Tools/VehicleFramework/Evidence/Baseline"
cp "$source_project/Tools/VehicleFramework/Evidence/Baseline/reference-data.json" "$workspace/Tools/VehicleFramework/Evidence/Baseline/"
printf '%s\n' "$workspace" > "$source_project/Library/DrivingMechanicsWork/validation-workspace.txt"
printf 'Isolated workspace: %s\n' "$workspace"
[[ "$prepare_only" == false ]] || exit 0
version="$(sed -n 's/^m_EditorVersion: //p' "$source_project/ProjectSettings/ProjectVersion.txt")"
editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity}"
[[ -x "$editor" ]] || { echo "Unity executable missing: $editor" >&2; exit 2; }
result="$(mktemp -d "$source_project/Tools/DrivingMechanics/Evidence/validation-$(date -u +%Y%m%dT%H%M%SZ).XXXXXX")"
printf '%s\n' "$result" > "$source_project/Library/DrivingMechanicsWork/validation-results.txt"
filter="${MW_TEST_FILTER:-NfsMwRemaster.Driving.Tests.MostWanted;NfsMwRemaster.Driving.Tests.VehicleFrameworkPhysicsRegressionTests;NfsMwRemaster.Driving.Tests.VehiclePhysicsContractTests;NfsMwRemaster.Driving.Tests.VehicleConfigurationTests;NfsMwRemaster.Driving.Tests.VehicleConfigurationTransactionTests;NfsMwRemaster.Driving.Tests.VehicleFrameworkAuthoringTests;NfsMwRemaster.Driving.Tests.VehicleFrameworkPartsTests;NfsMwRemaster.Driving.Tests.BmwVehicleIntegrationTests}"
args=(-batchmode -nographics -projectPath "$workspace" -job-worker-count 4 -runTests -testPlatform EditMode -testFilter "$filter" -testResults "$result/tests.xml" -logFile "$result/unity.log")
[[ -z "${UNITY_LICENSE_CHANNEL:-}" ]] || args+=(-licensing-client-channel "$UNITY_LICENSE_CHANNEL")
set +e
MW_DRIVING_EVIDENCE_DIRECTORY="$result" "$editor" "${args[@]}"
status=$?
set -e
[[ -s "$result/tests.xml" ]] || { echo "No test XML. Inspect $result/unity.log" >&2; exit 1; }
python3 - "$result/tests.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
print('Unity test result:', root.attrib)
for case in root.iter('test-case'):
    if case.get('result') == 'Failed':
        print('FAILED:', case.get('fullname'), case.findtext('failure/message', '').strip())
if root.get('failed', '0') != '0' or int(root.get('passed', '0')) == 0:
    raise SystemExit(1)
PY
[[ "$status" -eq 0 ]] || { echo "Unity exited $status despite XML; inspect log." >&2; exit "$status"; }
printf 'Evidence: %s\n' "$result"
