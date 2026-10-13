# E-041: A124 pursuit-end overrides

## Binary evidence

- SLES `AIPursuit::ShouldEnd() const` is at `0x1301c8` and is exactly 172
  bytes.
- After validating the target, the A124 function checks `ForcePursuitEnd`.
  When set, it clears the flag and returns true.
- It next checks `ForcePursuitNeverEnd` and returns false while that flag is
  set, before evaluating evade level and pursuit state.
- The SLUS retail function is 140 bytes and contains neither override, proving
  that this logic is A124-specific.

## Reconstruction

- Restored both override paths under `EA_BUILD_A124`.

## Validation

- SLES function: 65.674416% at 140/172 bytes -> 100.0% at the exact
  172/172-byte size.
- SLES zAI `.text`: 90.44518% -> 90.466415%.
- SLUS function remains 84.85714% at 140/140 bytes.
- SLUS zAI `.text` remains 86.121475%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v77.json` and
`/private/tmp/slus-zai-layout-v25.json`.
