# E-011: A124 AIPursuit layout and OnTask reconstruction

Reconstructed SLES A124-specific `AIPursuit` layout and `OnTask` behavior from the target type data, instruction stream, relocations, and rodata:

- A124 has no `mAllowStatsToAccumulate` field or later-build statistics gate.
- `mLastKnownLocation` retains 16-byte alignment, preserving the target offsets of the pursuit timers and `GroundSupportRequest`.
- The heat update locks pursuit attributes on first use, reads the career bin directly, does not apply the later challenge-race override, and does not refresh `mRepPointsPerMinute` on a heat-level transition.
- Formation changes update `DebugMenu::mCurPursuitForm` and emit the A124 `COP_FORMATION` and `COP_ATTEMPT` Juice telemetry with zone, image, and player-position context.
- `ForcePursuitNeverEnd` caps the evade meter at `0.9f`.
- Entering cooldown does not clamp `mSpawnCopTimer` in A124.
- The A124 function does not make the later post-computation `GetCoolDownTimeRequired()` clamp.
- The evade-complete path is expressed directly, eliminating the later-build temporary latch.

## Verification

- SLES EE-GCC build: passed.
- SLES `AIPursuit::OnTask`: `80.65467% -> 84.191696% -> 87.18893%` direct objdiff.
- SLES target/current size: `5780 / 5780` bytes.
- The corrected tail from the cooldown-time calculation through the epilogue now matches instruction-for-instruction.
- Correcting the A124 `pursuitlevels` layout raised `GetRandomValidCopCar` from `66.72881%` to `99.78814%` and `IsValidPursuitCarName` from `67.74138%` to `99.74138%`.
- Current whole-object mapped non-section function-and-object weighted result: `90.44037087961117%` over `281388` target bytes. This is a binary-similarity metric, not functional completion.
- SLUS EE-GCC build: passed.
- SLUS `AIPursuit::OnTask` remains `93.97875%`; target/current size remains `5836 / 5852` bytes.

Artifacts: `/private/tmp/sles-aitask-layout-v6.json`, `/private/tmp/sles-zai-layout-v6.json`, `/private/tmp/slus-aipursuit-regression-v5.json`.
