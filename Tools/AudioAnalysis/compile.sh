#!/usr/bin/env bash
set -euo pipefail
audio_root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$audio_root"
audio_version="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)"
audio_resources="/Applications/Unity/Hub/Editor/$audio_version/Unity.app/Contents/Resources"
audio_output="$(mktemp -d "${TMPDIR:-/tmp}/black-box-compile.XXXXXX")"
audio_editor_rsp="$(rg --files Library/Bee/artifacts -g NfsMwRemaster.Driving.Editor.rsp | head -1)"
audio_rsp_dir="$(dirname "$audio_editor_rsp")"
audio_csc="$(rg --files "$audio_resources/Scripting/DotNetSdk/sdk" -g csc.dll | head -1)"
for audio_assembly in NfsMwRemaster.Driving NfsMwRemaster.Driving.Editor NfsMwRemaster.Driving.Tests; do
  audio_rsp="$audio_output/$audio_assembly.rsp"
  sed -E '/^-out:/d; /^-refout:/d; /^"Assets\//d' "$audio_rsp_dir/$audio_assembly.rsp" > "$audio_rsp"
  printf '\n' >> "$audio_rsp"
  if [[ "$audio_assembly" != NfsMwRemaster.Driving ]]; then
    sed -i '' '/^-r:.*\/NfsMwRemaster.Driving.ref.dll/d' "$audio_rsp"
    printf '%s\n' "-r:\"$audio_output/NfsMwRemaster.Driving.ref.dll\"" >> "$audio_rsp"
  fi
  if [[ "$audio_assembly" == NfsMwRemaster.Driving ]]; then
    audio_source_folder=Assets/NfsMw/Modules/Driving/Runtime
  elif [[ "$audio_assembly" == NfsMwRemaster.Driving.Editor ]]; then
    audio_source_folder=Assets/NfsMw/Modules/Driving/Editor
  else
    sed -i '' '/^-r:.*\/NfsMwRemaster.Driving.Editor.ref.dll/d' "$audio_rsp"
    printf '%s\n' "-r:\"$audio_output/NfsMwRemaster.Driving.Editor.ref.dll\"" >> "$audio_rsp"
    audio_source_folder=Assets/NfsMw/Modules/Driving/Tests/Editor
  fi
  # Cached response-file source lists may be stale. Retain the actual package/engine
  # references and defines, but derive ownership from current nested asmdefs.
  while IFS= read -r audio_file; do
    audio_dir="$(dirname "$audio_file")"
    audio_owned=true
    while [[ "$audio_dir" != "$audio_source_folder" ]]; do
      for audio_nested in "$audio_dir"/*.asmdef; do
        if [[ -f "$audio_nested" ]]; then audio_owned=false; break; fi
      done
      [[ "$audio_owned" == true ]] || break
      audio_dir="$(dirname "$audio_dir")"
    done
    if [[ "$audio_owned" == true ]]; then printf '"%s"\n' "$audio_file" >> "$audio_rsp"; fi
  done < <(rg --files "$audio_source_folder" -g '*.cs')
  "$audio_resources/Scripting/NetCoreRuntime/dotnet" "$audio_csc" @"$audio_rsp" -out:"$audio_output/$audio_assembly.dll" -refout:"$audio_output/$audio_assembly.ref.dll" > "$audio_output/$audio_assembly.log" 2>&1 || { tail -45 "$audio_output/$audio_assembly.log"; exit 1; }
done
printf 'Runtime, Editor and tests compiled. Logs: %s\n' "$audio_output"
