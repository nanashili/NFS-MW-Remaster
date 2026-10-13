# E-042: A124 end-formation debug-state cleanup

## Binary evidence

- SLES `AIPursuit::EndCurrentFormation()` is at `0x12be38` and is exactly 48
  bytes.
- In addition to resetting the breaker timer and active-formation time, the
  retail A124 function copies the current pursuit level into
  `DebugMenu::mCurPursuitLevel` and clears `mCurPursuitState` and
  `mCurPursuitForm`.
- The prior reconstruction omitted all three debug-state updates and emitted
  only 20 bytes.

## Reconstruction

- Declared the three compiler-mangled debug-menu globals for A124 and restored
  their updates under `EA_BUILD_A124`.

## Validation

- SLES function: 35.833332% at 20/48 bytes -> 47.083332% at the exact
  48/48-byte size. Remaining differences are instruction scheduling and
  relocation/register ordering, not missing stores.
- SLES zAI `.text`: 90.466415% -> 90.46836%.
- SLUS retains its 20-byte implementation and zAI `.text` remains 86.121475%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v81.json` and
`/private/tmp/slus-zai-layout-v26.json`.
