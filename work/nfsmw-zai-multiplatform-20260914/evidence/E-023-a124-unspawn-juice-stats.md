# E-023: A124 UnSpawn Juice statistics

The SLES target mirrors the A124 spawned-vehicle telemetry when a vehicle is
removed. After shared unspawn behavior, it classifies traffic and police
drivers, distinguishes police helicopters through `GetModelType()`, and
increments `JuiceStatsDB` per-race counters 5, 1, and 3 respectively.

Adding the three unspawn counter values and the A124-only switch produces an
exact-sized 424-byte `AIVehicle::UnSpawn` with 99.90566% code match, up from
50.54717% and 224 current bytes. Its only reported differences are the two
known local-call target mappings for `IsSimplePhysicsActive` and `ClearGoal`.
The whole SLES zAI `.text` score rises from 88.309975% to 88.38524%. The
guarded SLUS function remains 224 bytes and 99.28571%.

Artifacts: `/private/tmp/sles-unspawn-v1.json`,
`/private/tmp/sles-zai-layout-v30.json`, and
`/private/tmp/slus-unspawn-v1.json`.
