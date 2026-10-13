# E-026: A124 SetAiControl behavior overrides

Ghidra 12.1.3 headless decompilation of the SLES zAI function at object
address `0x00027c00`, corroborated by its target relocations and the `IVehicle`
interface, shows that A124 changes AI control through vehicle behavior
overrides rather than the later retail `EEnableAIPhysics` event.

When AI control is enabled, A124 installs `SuspensionSimple` for
`BEHAVIOR_MECHANIC_SUSPENSION`. When it is disabled, A124 removes both the
input and suspension overrides. Both paths use the A124 goal string
`AIGoalRa` and then store the new `bAiControl` state.

Restoring that guarded behavior improves
`AIVehicleHuman::SetAiControl(bool)` from 48.96154% at 312 target / 188
current bytes to 99.61539% at the exact 312-byte target size. The SLES zAI
`.text` score rises from 88.93593% to 88.99277%. The non-A124 source path is
unchanged and the SLUS zAI object still compiles successfully; its current
target symbol does not produce a paired similarity measure.

Artifacts: `/private/tmp/sles-set-ai-control-v2.json`,
`/private/tmp/sles-zai-layout-v33.json`, and
`/private/tmp/slus-set-ai-control-v1.json`.
