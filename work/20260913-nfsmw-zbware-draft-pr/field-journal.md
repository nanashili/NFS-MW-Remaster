# Field journal (anonymized)

## 2026-09-13 | focused upstream contribution

- Reusable pattern: when a dirty reverse-engineering worktree contains multiple efforts, derive a clean upstream-main branch and apply only the target module diff before publishing.
- Evidence: E-001/E-002 record the 17-file isolation and initial build; E-005/E-006 record the Ghidra CLI and headless static pass; E-007 records the rebuilt object and focused match improvement; E-003/E-004 record fork publication and draft-PR state.
- Finding: F-001 confirms the isolated source is buildable; F-002 confirms the focused change is reviewable upstream; the PC debug/ticker helpers are now source-reconstructed from target disassembly and Ghidra decompilation.
- Reusable static workflow: when the canonical ELF/DWARF inputs are not present, keep the repository's exact commands documented as blocked, import the available PC sample under an explicit fallback program name, and report the distinction instead of conflating the two analyses.
- Residual boundary: runtime/platform behavior, canonical ELF/DWARF import, and maintainer CI remain intentionally unverified.
- High-leverage lesson: when several allocator functions disagree at once, verify object layout before tuning individual control flow. Recovering the missing third intrusive list shifted every subsequent PC `MemoryPool` field and converted five functions to exact matches in one coherent change.
- Compiler-shape lesson: the retail hermite routine uses five uninitialized matrices, reuses `U` as the intermediate `K`, and requires a 16-byte-aligned local. Suppressing identity construction and expressing that reuse produced a byte-exact function without changing its math.
