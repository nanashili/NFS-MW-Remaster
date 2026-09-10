#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
resources='/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources'
dotnet="$resources/Scripting/NetCoreRuntime/dotnet"
csc=("$resources"/Scripting/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll)
refs=(); for file in "$resources"/Scripting/DotNetSdk/packs/Microsoft.NETCore.App.Ref/*/ref/net8.0/*.dll; do refs+=("-r:$file"); done
build="$(mktemp -d /private/tmp/car-visual.XXXXXX)"
"$dotnet" "${csc[0]}" -nologo -target:exe -langversion:latest -nostdlib+ "${refs[@]}" \
  -out:"$build/VisualOffsets.dll" \
  Assets/NfsMw/Modules/Driving/Editor/AudioAnalysis/Binary/MostWantedAudioDatabase.cs \
  Assets/NfsMw/Modules/Driving/Editor/DrivingMechanics/MostWantedHandlingReport.cs \
  Assets/NfsMw/Modules/Driving/Editor/DrivingMechanics/MostWantedHandlingReader.cs \
  Tools/CarRemaster/VisualOffsets.cs
printf '%s\n' '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}' > "$build/VisualOffsets.runtimeconfig.json"
"$dotnet" "$build/VisualOffsets.dll"
