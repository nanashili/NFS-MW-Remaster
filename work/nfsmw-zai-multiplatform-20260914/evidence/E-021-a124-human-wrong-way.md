# E-021: A124 human wrong-way behavior and layout

The SLES A124 target omits the retail staging and actively-racing gates in
`AIVehicleHuman::UpdateWrongWay`. It copies path, lane, race, traffic, and
decision filters from the vehicle's drive-to navigation object, then determines
wrong-way state by comparing the local navigation node index with the road
segment's race-route direction. The retail dot-product test remains guarded to
non-A124 builds.

Direct target/current instruction comparison also established the A124 member
layout: `bAiControl` remains at `0x980`, while `vMomentPosition`,
`fMomentRadius`, and `mWrongWay` begin at `0x990`, `0x99c`, and `0x9a0`.
An A124-only 12-byte gap after `bAiControl` restores those offsets without
changing retail layouts.

After reconstruction, the SLES function improved from 57.190475% at 544 bytes
to 99.007935% at the exact 504-byte target size. The whole SLES zAI `.text`
score rose from 88.15285% to 88.22883%. The guarded SLUS function remains at
86.283585% with its unchanged 536-byte target and 548-byte current sizes.

Artifacts: `/private/tmp/sles-update-wrong-way-v3.json`,
`/private/tmp/sles-zai-layout-v28.json`, and
`/private/tmp/slus-update-wrong-way-v2.json`.
