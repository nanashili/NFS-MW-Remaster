# E-043: A124 cop-lockout behavior

## Binary evidence

- SLES `AICopManager::LockoutCops(bool)` is at `0x10a660` and is exactly 36
  bytes.
- The A124 function sets a 45-second timer when locking out cops. Unlocking
  clears both the timer and `mNoNewPursuitsOrCops`.
- It does not contain the later `SoundAI`, race-status, race-database, or
  pursuit-attribute override path.
- The prior A124 reconstruction used the newer 60-second behavior and emitted
  a 240-byte function.

## Reconstruction

- Restored the A124 value of `kCopLockoutTime` as 45 seconds.
- Excluded the later speech/race-dependent timer override from A124 while
  retaining it for newer builds.

## Validation

- SLES function: 0.0% at 240/36 bytes -> 100.0% at the exact 36/36-byte size.
- SLES zAI `.text`: 90.46836% -> 90.48132%.
- SLUS retains its newer 240-byte implementation and zAI `.text` remains
  86.121475%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v82.json` and
`/private/tmp/slus-zai-layout-v27.json`.
