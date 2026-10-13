# E-033: A124 road-navigation spline layout and helicopter navigation

## Binary evidence

- SLES `WRoadNav::WRoadNav` is at `0x4664e8` in `NFS.ELF`.
- GNU MIPS objdump shows embedded `USpline` constructor calls at object offsets
  `0x1d0`, `0x240`, and `0x2b0`, establishing a `0x70`-byte stride.
- The same constructor stores the vtable at `0x34c` and the path pointer at
  `0x348`; `AIActionHeliPursuit::UpdateNavPos` reads `fDeadEnd` at `0x320`.
- Before reconstruction, the current constructor placed the splines at
  `0x1cc`, `0x234`, and `0x29c`, with `fDeadEnd` at `0x304`.

## Reconstruction

- Restored the A124-only 16-byte `USpline` alignment. This produces the exact
  historical `0x1d0` first offset, `0x70` stride, `0x320` dead-end offset, and
  `0x348` trailing path-pointer offset without speculative padding.
- Restored the target statement order in helicopter navigation: acquire the
  road forward vector before subtracting positions.
- Restored the target loop form, whose continuation repeats the
  `distance_along_nav > next_distance_along_nav` test before checking road-nav
  validity and dead-end state.

## Validation

- SLES `AIActionHeliPursuit::UpdateNavPos`: 76.281044% -> 96.07843%, retaining
  the exact 612-byte target size.
- SLES zAI `.text`: 89.96457% -> 90.02599%.
- SLUS zAI `.text` remains 86.05584%; the A124 alignment is compile-time
  guarded and the later target has no separate `UpdateNavPos` symbol.
- Both SLES and SLUS zAI objects build successfully.

Artifacts: `/private/tmp/sles-zai-layout-v48.json`,
`/private/tmp/slus-zai-layout-v12.json`, `/private/tmp/nav-left-v48.tsv`, and
`/private/tmp/nav-right-v48.tsv`.

