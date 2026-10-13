# E-055: PS2 traffic ABI and A124 traffic-action split

## Binary evidence

- The SLES retail object exports `AIVehicleTraffic::StartDriving()` without
  an argument at `0x22c00`, with a `0x3c`-byte body. The corresponding SLUS
  source/retail contract is `AIVehicleTraffic::StartDriving(float)`; the
  reconstructed SLUS body is `0x258` bytes. This is an ABI split, not just a
  compiler naming difference.
- Ghidra CLI was used on the extracted SLUS target and current source objects.
  The target was imported at base `0x10000` and the source at the same base;
  the target `IsCollapseActive` bytes at `0x52678` are `jr ra; lwc1 f0,0x160(a0)`,
  while the current source bytes at `0x55060` are `jr ra; lw v0,0x18c(a0)`. That
  unrelated SLUS return/layout mismatch remains an open gap and was not
  papered over by changing the traffic ABI.
- SLES `AIActionTraffic::BeginAction(float)` has an exact `0x138`-byte target
  and source body after the A124 split. Its
  target stream has no `UpdateNavPos(30.0f)` call and uses the single
  `0x3ee4e11e` (`0.44703`, `MPH2MPS`) multiply for each search speed. The
  non-A124 stream retains the existing navigation update and `0.75f` scaling.

## Reconstruction

- `ITrafficAI::StartDriving` is now declared with no parameter for
  `EA_BUILD_A124` and with the existing `float speed` parameter elsewhere.
- `AIVehicleTraffic` and `AITrafficManager::SpawnTraffic` select the matching
  virtual declaration/call at compile time. The A124 implementation performs
  only the retail `AIGoalTraffic` goal assignment; the non-A124 implementation
  retains its damage reset, speed setup, and activation path.
- `AIActionTraffic::BeginAction` now guards the A124 omissions and speed
  constants without changing the SLUS/other-platform path.

## Validation

- SLES A124 temp parity build: `SpawnTraffic` target/source `812/812` bytes,
  `99.85222%`; `BeginAction` `312/312` bytes, `98.46154%`; and
  `StartDriving` `60/60` bytes, `99.0%`.
- SLES zAI `.text` improved from `91.57527%` to `91.591064%` in the final
  artifact. SLUS remains stable at `88.29549%`; its `SpawnTraffic` is
  `94.4329%` and `BeginAction` is `93.414635%` while preserving the existing
  non-A124 ABI.
- Artifacts: `/private/tmp/sles-zai-traffic-begin-a124-final.json` and
  `/private/tmp/slus-zai-traffic-begin-final.json`.
- These measurements use the configured EE-GCC temp build because the
  authoritative PC worktree path contains spaces; no claim is made that the
  authoritative PC build itself completed.
