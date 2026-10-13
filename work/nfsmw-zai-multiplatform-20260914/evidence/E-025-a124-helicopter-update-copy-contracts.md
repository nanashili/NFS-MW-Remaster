# E-025: A124 helicopter update and copy contracts

Ghidra 12.1.3 headless decompilation of the SLES zAI function at object
address `0x00033f40` confirms that `AIVehicleHelicopter::Update(float)` calls
the out-of-line `IPlayer::First(PLAYER_LOCAL)` specialization, copies a
`WWorldPos` member-by-member, and does not execute the retail source's unused
`bClamp` expression before calculating the helicopter shadow scale.

The reconstructed A124 contracts are:

- `ListableSet::First` is an out-of-line template member on A124;
- `WCollisionTri` has a memberwise A124 copy constructor;
- the A124 `WWorldPos` copy constructor preserves its three packed counters,
  surface hash, offset, and surface pointer individually;
- the packed A124 counter storage uses 64-bit bitfield operations; and
- the discarded retail clamp is excluded from the A124 path.

These changes improve the SLES function from 66.09091% at 616 target / 616
current bytes to 93.82468% at 616 target / 608 current bytes. They also make
several independent A124 callers exact, including
`AIActionHeliPursuit::StartPathToPlayerCar`,
`AIActionHeliExit::IsFinished`, `AICopManager::SpawnVehicleBehindTarget`,
`Gps::Engage`, and `AIActionHeliExit::Update`. The SLES zAI `.text` score
rises from 88.45702% to 88.93593%.

The SLUS configuration still compiles because every new ABI/copy rule is
guarded by `EA_BUILD_A124`. Its current target-symbol mapping for helicopter
`Update` resolves to a 468-byte target body and therefore is not used as
cross-version equivalence evidence for this A124 reconstruction.

Artifacts: `/private/tmp/sles-heli-update-v3.json`,
`/private/tmp/sles-zai-layout-v32.json`, and
`/private/tmp/slus-zai-layout-v5.json`.
