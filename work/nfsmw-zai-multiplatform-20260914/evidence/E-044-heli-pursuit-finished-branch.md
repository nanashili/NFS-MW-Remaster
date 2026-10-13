# E-044: Helicopter-pursuit finished branch

## Binary evidence

- SLES `AIActionHeliPursuit::IsFinished()` is at `0x10fd28` and is exactly 40
  bytes.
- The retail function branches on the false comparison, initializes false on
  that path, and assigns true only on the completed path.
- Returning the comparison expression directly inverted the branch layout.

## Reconstruction

- Restored the explicit `if` returning true when pursuit time exceeds the
  maximum, followed by a false return.

## Validation

- SLES function: 74.0% -> 100.0%, exact 40/40-byte match.
- SLES zAI `.text`: 90.48132% -> 90.48506%.
- SLUS function: 74.0% -> 100.0%, exact 40/40-byte match.
- SLUS zAI `.text`: 86.121475% -> 86.12519%.
- Both PS2 zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v85.json` and
`/private/tmp/slus-zai-layout-v28.json`.
