# E-027: A124 traffic-density behavior

The SLES `AITrafficManager::ComputeDensity` target does not apply the later
retail pursuit-density multiplier. Its control flow returns the SkipFE or race
density directly, returns `1.0f` for roaming, and otherwise converges on a
shared zero return.

Reconstructing that A124-specific flow in `AITrafficManager.cpp` changed the
function from 74.046875% (target 256 bytes, current 296 bytes) to an exact
100% match at 256 bytes. The SLES zAI `.text` score increased from 88.99277%
to 89.01699%.

Verification:

- SLES zAI object rebuilt successfully with the historical EE compiler.
- `objdiff-cli` reports `ComputeDensity__C16AITrafficManager` as 100% at
  256/256 bytes in `/private/tmp/sles-zai-layout-v35.json`.
- SLUS zAI object also rebuilds successfully; its current whole `.text` score
  is 86.05584% in `/private/tmp/slus-zai-layout-v6.json`.
- The non-A124 implementation retains the pursuit multiplier.
