# E-024: A124 PrepareForRace tail

The SLES A124 `AIVehicleRacecar::PrepareForRace` target has a platform-specific
tail after the shared player reset. It unblocks `IInputPlayer`, enables the
current road navigation's race filter, resets navigation with
`SELECT_VALID_LANE`, and selects the A124 goal string `AIGoalRa`. Unlike retail,
the A124 path passes `RacePreparationInfo::HeatLevel` directly to `SetHeat`
without clamping it to at least 1.0.

Restoring this guarded tail and heat behavior improves the SLES function from
67.189545% at 464 current bytes to 99.803925% at the exact 612-byte target
size. The whole SLES zAI `.text` score rises from 88.38524% to 88.45702%.
The guarded SLUS function remains 464 bytes and 99.69827%.

Artifacts: `/private/tmp/sles-prepare-race-v2.json`,
`/private/tmp/sles-zai-layout-v31.json`, and
`/private/tmp/slus-prepare-race-v1.json`.
