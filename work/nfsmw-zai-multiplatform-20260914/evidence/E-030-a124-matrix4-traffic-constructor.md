# E-030: A124 Matrix4 ABI and AIActionTraffic construction

Ghidra CLI and the SLES constructor instruction stream show that A124 treats
`UMath::Matrix4` as a naturally aligned aggregate, while individual members
that need vector alignment are explicitly declared `ALIGNVEC`. In
`AIActionTraffic`, this places `mNavMatrix` at 0xd0 while retaining the
unaligned `ldl`/`ldr` identity-copy sequence.

The same trace references the historical two-argument
`Sim::Collision::AddListener(IListener *, const UTL::COM::IUnknown *)`
overload. The later debug-name argument and its string load are absent.

Results:

- A124 `Matrix4` natural type alignment raised SLES zAI `.text` from
  89.667854% to 89.90519%.
- Explicit `ALIGNVEC` on `AIActionTraffic::mNavMatrix` plus the historical
  listener overload raised its constructor from 66.771736% at 736/680 bytes
  to 98.690216% at the exact 736/736-byte size.
- Final SLES zAI `.text` for this step is 89.90999%.
- SLUS retains its later aligned `Matrix4` type and three-argument listener
  call; its object rebuilds and remains 86.05584% `.text`.

Artifacts: `/private/tmp/sles-zai-layout-v41.json` and
`/private/tmp/slus-zai-layout-v9.json`.
