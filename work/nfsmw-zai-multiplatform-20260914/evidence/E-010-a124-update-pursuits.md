# E-010: A124 UpdatePursuits reconstruction

Reconstructed four SLES A124-specific behaviors from the target instruction and relocation stream:

- A destroyed, removed cop increments `cops_destroyed` without the later-build race-mode policy gate.
- A busted pursuit emits `PURSUIT / END / Busted` with zone, image, and player-position context.
- The successful cop formation is logged and emitted as `COP_ATTEMPT_SUCCEEDED`.
- `ForcePursuitStart` invokes `PursueAtHeatLevel(1)` after processing a queued pursuit vehicle and clears the latch.

## Verification

- SLES EE-GCC build: passed.
- SLES `UpdatePursuits`: `73.077965% -> 98.4033%` direct objdiff.
- SLES target/current size: `2668 / 2672` bytes.
- Current whole-object mapped non-section function-weighted result: `90.18755315140663%` over `281388` target bytes. This metric deliberately excludes data/rodata sections and is not a functional-completion percentage.
- SLUS EE-GCC build: passed.
- SLUS `UpdatePursuits` regression result remains `93.8422%`, size `2248` bytes.
- SLUS `ResetCopsForRestart` remains `99.80498%`, size `1928` bytes.

Artifacts: `/private/tmp/sles-update-pursuits-v5.json`, `/private/tmp/sles-zai-after-update-pursuits.json`, `/private/tmp/slus-update-pursuits-regression.json`.
