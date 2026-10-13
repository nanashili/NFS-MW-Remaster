# E-029: A124 GPS render ordering and rear-view behavior

Direct SLES instruction comparison identifies three A124-specific details in
`Gps::Render`:

1. `EVIEWMODE_ONE_RVM` selects the 1.5 aspect ratio without consulting the
   later `FEDatabase` `RearviewOn` setting.
2. The inverse-camera matrix is output-only storage at this call site; A124
   does not run the later identity default constructor before inversion.
3. A124 allocates the render matrix after constructing the elementary
   forward/up vectors but before calculating the normalized right/up basis.

All three are guarded by `EA_BUILD_A124`. Across this pass, SLES `Gps::Render`
improved from 62.850136% at 1468/1512 bytes to 70.14714% at 1468/1452 bytes.
SLES zAI `.text` increased from 89.62933% to 89.667854%.

Verification:

- SLES historical EE build passes; report:
  `/private/tmp/sles-zai-layout-v39.json`.
- SLUS historical EE build passes and the later path remains 65.13441% for
  `Gps::Render`, with whole zAI `.text` at 86.05584%; report:
  `/private/tmp/slus-zai-layout-v8.json`.
