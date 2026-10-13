# E-031: A124 CopRequest wave and cooldown limits

The SLES instruction stream for `AIPursuit::CopRequest` contains a decision
block absent from the later reconstruction. Before weighted cop-type
selection, A124 compares the outstanding composition count with the remaining
wave (`mNumCopsRequiredToEvade - mNumFullyEngagedCopsEvaded`). It returns no
request when the outstanding count already covers that wave. During cooldown,
it obtains a fresh pursuit-level attribute and recalculates the count from
`NumPatrolCars`.

The behavior is guarded by `EA_BUILD_A124`; later PS2 and PC behavior retains
the original zero-only test and assignment order.

Results:

- SLES `CopRequest__9AIPursuit` improved from 74.59649% to 87.61053%.
- SLES zAI `.text` improved from 89.90999% to 89.96335%.
- SLUS rebuilds and remains unchanged at 86.05584% `.text`.

Artifacts: `/private/tmp/sles-zai-layout-v45.json` and
`/private/tmp/slus-zai-layout-v11.json`.
