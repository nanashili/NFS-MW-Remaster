# E-017: A124 pursuit-evaded telemetry reconstruction

## Finding

The A124 SLES target contains a Juice telemetry path after resolving
`IPerpetrator`; the reconstruction previously stopped after `QueryInterface`.
The target sends `GameEvent("PURSUIT", "END", "Escaped", ...)`, using the
pending rep points from cop destruction, current zone, image name, and the
no-argument player-position helper. The A124 target also omits the retail
SoundAI/MControlPathfinder branch in this function.

The target destroys `MPerpEscaped` through the implicit inherited destructor,
matching the generated-message pattern established in E-016.

## Measurements

- SLES `PursuitIsEvaded`: 47.60625% at target/current 640/616 bytes ->
  99.675% at exact 640/640 bytes.
- SLUS `PursuitIsEvaded`: 99.28571% at exact 616/616 bytes.
- SLES whole-unit weighted mapped score on the 308,444-byte denominator:
  85.48486770170265% -> 85.60011122179718%.

Artifacts: `/private/tmp/sles-pursuit-evaded-v3.json`,
`/private/tmp/slus-pursuit-evaded-v3.json`, and
`/private/tmp/sles-zai-layout-v18.json`.
