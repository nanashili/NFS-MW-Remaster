# E-016: generated message implicit destructors

## Finding

The SLES and SLUS `AICopManager::UpdateSupportCops` targets destroy temporary
`MReqRoadBlock` and `MReqBackup` objects by calling the inherited
`Hermes::Message` destructor directly. Explicit empty derived destructors in the
reconstructed generated-message headers forced different out-of-line destructor
calls and disrupted instruction alignment.

Removing the redundant explicit destructors preserves the C++ destruction
semantics while reproducing the target compiler's implicit-destructor path.

## Measurements

- SLES `UpdateSupportCops`: 39.458332% -> 99.55357%; target/current size 672/672.
- SLUS `UpdateSupportCops`: 99.53571%; target/current size 672/672.
- SLES whole-unit weighted mapped score using the same 308,444-byte denominator:
  84.59428457369248% -> 85.48486770170265%.

Artifacts: `/private/tmp/sles-update-support-cops-v2.json`,
`/private/tmp/slus-update-support-cops-v2.json`, and
`/private/tmp/sles-zai-layout-v17.json`.
