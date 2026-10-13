# E-022: A124 SetSpawned Juice statistics

The SLES target continues after the shared spawn reset logic by classifying the
vehicle driver. It increments A124 `JuiceStatsDB` per-race counters for ordinary
police vehicles, police helicopters, and traffic vehicles. Direct instruction
comparison identifies counter values 0, 2, and 4 respectively and confirms the
police-helicopter distinction uses `IVehicle::GetModelType()` against
`CARTYPE_COPHELI`.

Restoring the A124-only `JuicePerRaceStatType` and `JuiceStatsDB` declarations
plus the switch in `AIVehicle::SetSpawned` produces an exact 580-byte function
with 100% SLES code match, up from 61.09655% and 368 current bytes. The whole
SLES zAI `.text` score rises from 88.22883% to 88.309975%. The guarded SLUS
function remains 368 bytes and 99.51087%.

Artifacts: `/private/tmp/sles-set-spawned-v2.json`,
`/private/tmp/sles-zai-layout-v29.json`, and
`/private/tmp/slus-set-spawned-v1.json`.
