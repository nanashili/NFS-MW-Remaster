# E-013: PS2 GPS VU length and matrix initialization

Direct SLES instruction comparison recovered two additional PS2 implementation
details in `Gps::Render`:

- Camera speed uses the older VU dot/square-root sequence local to this call
  site. Applying that sequence to the shared `VU0_v3length` helper damaged
  unrelated functions, so the recovered variant is intentionally isolated to
  GPS.
- The PS2 `bMatrix4` identity constructor uses packed 128-bit integer
  operations and stores. PC retains its scalar constructor path.

## Verification

- SLES EE-GCC build: passed.
- SLES `Gps::Render`: `47.185287% -> 50.991825%` across the two changes.
- SLES target/current size: `1468 / 1564` bytes, down from `1468 / 1592`.
- SLES whole mapped non-section function-and-object weighted result:
  `90.45418860709063% -> 90.46979789209217%` over `281388` target bytes.
- `AIActionRace::Update` remains `85.59366%`, confirming that the rejected
  shared-helper experiment did not remain in the source.
- SLUS EE-GCC build: passed.
- SLUS `Gps::Render`: `51.612904% -> 55.36828%`; target/current size is
  `1488 / 1564` bytes.

Artifacts: `/private/tmp/sles-gps-render-v13.json`,
`/private/tmp/sles-zai-layout-v13.json`, and
`/private/tmp/slus-gps-render-v13.json`.
