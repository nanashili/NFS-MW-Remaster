# E-004: PS2 GPS VU0 vector path

## Claim

The PS2 `Gps::Render` target performs arrow-direction normalization and scaled basis cross-normalization as inline VU0 operations rather than the generic `bVector3` constructor, `bNormalize`, and `bCross` calls.

## Evidence

- Both PS2 targets show inline `lqc2`, `vsub`, `vopmula`, `vopmsub`, `vrsqrt`, and `vmulq` sequences at the corresponding render stages.
- Reconstructing those operations increased SLES `Render__3GpsP5eView` from 40.013622% to 44.607628%.
- The same source increased SLUS `Render__3GpsP5eView` from 41.85753% to 49.069893%.
- Fresh SLES and SLUS zAI object builds succeeded.

## Reconstruction

- The PS2 render branch uses `UMath::Vector3`, `VU0_v3sub`, and `VU0_v3unit` for arrow direction.
- Added an inline scaled unit-cross-product VU0 primitive and used it for the right/up arrow basis.
- Non-PS2 source remains on the existing generic vector path.
