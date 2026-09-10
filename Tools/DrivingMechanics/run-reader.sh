#!/usr/bin/env bash
set -euo pipefail
project="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$project"
unity_version="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)"
resources="${UNITY_RESOURCES:-/Applications/Unity/Hub/Editor/$unity_version/Unity.app/Contents/Resources}"
dotnet="${DOTNET_HOST:-$resources/Scripting/NetCoreRuntime/dotnet}"
csc="$(find "$resources/Scripting/DotNetSdk/sdk" -path '*/Roslyn/bincore/csc.dll' -print -quit)"
refdir="$(find "$resources/Scripting/DotNetSdk/packs/Microsoft.NETCore.App.Ref" -path '*/ref/net8.0' -type d -print -quit)"
nunit="$(find Library/PackageCache -name nunit.framework.dll -print -quit)"
[[ -x "$dotnet" && -f "$csc" && -d "$refdir" && -f "$nunit" ]] || { echo 'Installed compiler, .NET 8 references, or NUnit missing.' >&2; exit 1; }
mkdir -p Library/DrivingMechanics
build="$(mktemp -d "$project/Library/DrivingMechanics/reader-build.XXXXXX")"
refs=(); for reference in "$refdir"/*.dll; do refs+=("-r:$reference"); done
"$dotnet" "$csc" -nologo -target:exe -langversion:latest -nostdlib+ "${refs[@]}" "-r:$nunit" \
  -out:"$build/HandlingReader.dll" \
  Assets/NfsMw/Modules/Driving/Editor/AudioAnalysis/Binary/MostWantedAudioDatabase.cs \
  Assets/NfsMw/Modules/Driving/Editor/DrivingMechanics/MostWantedHandlingReport.cs \
  Assets/NfsMw/Modules/Driving/Editor/DrivingMechanics/MostWantedHandlingReader.cs \
  Assets/NfsMw/Modules/Driving/Tests/Editor/MostWantedHandlingReaderTests.cs \
  Tools/DrivingMechanics/Harness/Program.cs
cp "$nunit" "$build/nunit.framework.dll"
printf '%s\n' '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}' > "$build/HandlingReader.runtimeconfig.json"
if [[ $# -eq 0 ]]; then set -- --self-test; fi
"$dotnet" "$build/HandlingReader.dll" "$@" --project "$project"
