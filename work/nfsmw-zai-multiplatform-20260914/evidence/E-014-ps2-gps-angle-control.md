# E-014: PS2 GPS angle-control ordering

The SLES and SLUS instruction streams show that `Gps::Render` handles the
first-draw case before its angle-smoothing branch. They also use strict
comparisons for rotation direction and the positive/negative pi wrap bounds.
The source now matches that control-flow ordering and boundary behavior.

## Verification

- SLES EE-GCC build: passed.
- SLES `Gps::Render`: `50.991825% -> 55.08992%` direct objdiff.
- SLES whole mapped non-section function-and-object weighted result:
  `90.46979789209217% -> 90.49335611372207%` over `281388` target bytes.
- SLUS EE-GCC build: passed.
- SLUS `Gps::Render`: `55.36828% -> 59.41129%` direct objdiff.

Artifacts: `/private/tmp/sles-gps-render-v14.json`,
`/private/tmp/sles-zai-layout-v14.json`, and
`/private/tmp/slus-gps-render-v14.json`.
