# E-009: A124 ResetCopsForRestart control flow

- Reconstructed the SLES A124 pursuit teardown directly from the target instruction stream.
- A124 removes and unspawns each matching cop inside the pursuit's vehicle loop, then releases that pursuit. It does not use the later build's separate global unspawn pass.
- A124 does not test `IVehicle::IsActive()` in this path and does not explicitly clear `mIVehicleList` after `KillVehicle`; destruction and detach processing own list removal.
- Removed target-absent A124 lockout/challenge-race and pursuit-spec initialization while retaining it for later builds.

## Verification

- SLES EE-GCC build: passed.
- SLES target size/current size: `1508 / 1508` bytes.
- SLES direct objdiff match: `99.933685%` (remaining differences are relocation labels for the `SoundAI` literal, equivalent message destructor symbol, and local `KillVehicle` helper).
- SLUS EE-GCC build: passed.
- SLUS shortened-target size/current size: `1928 / 1928` bytes.
- SLUS direct objdiff match: `99.80498%`, preserving the later-build control flow.

Artifacts: `/private/tmp/sles-reset-v5.json`, `/private/tmp/slus-reset-current.json`.
