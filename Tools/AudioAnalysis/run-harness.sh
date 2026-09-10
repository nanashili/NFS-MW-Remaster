#!/usr/bin/env bash
set -euo pipefail
audio_root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
audio_out="${BLACKBOX_AUDIO_REPORT:-$audio_root/Library/BlackBoxAudio/audio-analysis-report.json}"
unity_version="$(sed -n 's/^m_EditorVersion: //p' "$audio_root/ProjectSettings/ProjectVersion.txt")"
unity_root="${UNITY_ROOT:-/Applications/Unity/Hub/Editor/$unity_version/Unity.app/Contents/Resources}"
unity_dotnet="$unity_root/Scripting/NetCoreRuntime/dotnet"
csc="$unity_root/Scripting/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll"
refdir="$(find "$unity_root/Scripting/DotNetSdk/packs/Microsoft.NETCore.App.Ref" -path '*/ref/net8.0' -type d -print -quit)"
build_dir="$(mktemp -d "${TMPDIR:-/tmp}/black-box-audio-harness.XXXXXX")"
refs=(); for ref in "$refdir"/*.dll; do refs+=("-r:$ref"); done
"$unity_dotnet" "$csc" -nologo -target:exe -langversion:latest -out:"$build_dir/AudioAnalysisHarness.dll" -nostdlib+ "${refs[@]}" \
  "$audio_root/Tools/AudioAnalysis/Harness/Program.cs" \
  "$audio_root/Assets/NfsMw/Modules/Driving/Editor/AudioAnalysis/Binary/BinaryAnalysis.cs"
cat > "$build_dir/AudioAnalysisHarness.runtimeconfig.json" <<'JSON'
{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.21"}}}
JSON
shift_args=()
if [[ -n "${BLACKBOX_AUDIO_VGMSTREAM:-}" ]]; then shift_args+=(--vgmstream "$BLACKBOX_AUDIO_VGMSTREAM"); fi
if [[ -n "${BLACKBOX_AUDIO_REFERENCE_DIR:-}" ]]; then shift_args+=(--reference-dir "$BLACKBOX_AUDIO_REFERENCE_DIR"); fi
if [[ "${BLACKBOX_AUDIO_STRICT_REFERENCE:-0}" == 1 ]]; then shift_args+=(--strict-reference); fi
run_args=(--out "$audio_out"); if ((${#shift_args[@]})); then run_args+=("${shift_args[@]}"); fi; run_args+=("$@")
export BLACKBOX_AUDIO_PARSER_SOURCE="$audio_root/Assets/NfsMw/Modules/Driving/Editor/AudioAnalysis/Binary/BinaryAnalysis.cs"
"$unity_dotnet" "$build_dir/AudioAnalysisHarness.dll" "${run_args[@]}"
