#!/usr/bin/env bash
set -euo pipefail
workspace_script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
workspace_source="$(CDPATH= cd -- "$workspace_script_dir/../.." && pwd)"
cd "$workspace_source"
workspace_version="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)"
workspace_resources="/Applications/Unity/Hub/Editor/$workspace_version/Unity.app/Contents/Resources"
workspace_output="$(mktemp -d "${TMPDIR:-/tmp}/nfs-workspace-compile.XXXXXX")"
# Reuse the installed Editor's exact defines, analyzer set and package references.
# These generated response files are compile evidence, not an alternative Unity build.
workspace_editor_rsp="$(rg --files Library/Bee/artifacts -g NfsMwRemaster.Driving.Editor.rsp | head -1)"
workspace_tests_rsp="$(rg --files Library/Bee/artifacts -g NfsMwRemaster.Driving.Tests.rsp | head -1)"
test -f "$workspace_editor_rsp"
test -f "$workspace_tests_rsp"
workspace_csc="$(rg --files "$workspace_resources/Scripting/DotNetSdk/sdk" -g csc.dll | head -1)"
sed -E '/^-out:/d; /^-refout:/d; /"Assets\/Driving\/Editor\/Workspace\//d' "$workspace_editor_rsp" > "$workspace_output/Editor.rsp"
"$workspace_resources/Scripting/NetCoreRuntime/dotnet" "$workspace_csc" @"$workspace_output/Editor.rsp" -out:"$workspace_output/NfsMwRemaster.Driving.Editor.dll" -refout:"$workspace_output/NfsMwRemaster.Driving.Editor.ref.dll" Assets/NfsMw/Modules/Driving/Editor/Workspace/*.cs > "$workspace_output/editor.log" 2>&1 || { tail -35 "$workspace_output/editor.log"; exit 1; }
sed -E '/^-out:/d; /^-refout:/d; /^-r:.*NfsMwRemaster.Driving.Editor.ref.dll/d; /"Assets\/Driving\/Tests\/Editor\/RacingWorkspaceTests.cs"/d' "$workspace_tests_rsp" > "$workspace_output/Tests.rsp"
"$workspace_resources/Scripting/NetCoreRuntime/dotnet" "$workspace_csc" @"$workspace_output/Tests.rsp" -r:"$workspace_output/NfsMwRemaster.Driving.Editor.ref.dll" -out:"$workspace_output/NfsMwRemaster.Driving.Tests.dll" -refout:"$workspace_output/NfsMwRemaster.Driving.Tests.ref.dll" Assets/NfsMw/Modules/Driving/Tests/Editor/RacingWorkspaceTests.cs > "$workspace_output/tests.log" 2>&1 || { tail -35 "$workspace_output/tests.log"; exit 1; }
printf 'Editor and test assemblies compiled. Logs: %s\n' "$workspace_output"
