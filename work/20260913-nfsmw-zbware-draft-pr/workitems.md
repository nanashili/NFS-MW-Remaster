# Work Items

| ID | title | role | targets | surface | status | evidence | notes |
|----|-------|------|---------|---------|--------|----------|-------|
| WI-001 | Establish scope and auth | lead | case | process | done | | User-authorized upstream contribution scope |
| WI-002 | Isolate and verify zBWare patch | lead | clean PR worktree | source | done | E-001, E-002 | 17 zBWare files only; local sample remains untracked |
| WI-003 | Publish draft upstream PR | lead | dbalatoni13/nfsmw | GitHub | done | E-003, E-004, E-008, E-010 | nanashili fork and draft PR #138 with all three reconstruction commits |
| WI-004 | Run Ghidra CLI and headless static analysis | lead | speed.exe, Ghidra project | static | done | E-005, E-006 | Ghidra 12.1.3 and ghidra-cli 0.2.2; ELF/DWARF inputs unavailable locally |
| WI-005 | Integrate high-confidence PC helpers and remeasure | lead | bDebug.cpp, SPEED_EXE_1_3 symbols | build/match | done | E-007 | Rebuilt zBWare object; focused report reaches 63.802383% fuzzy and 147/467 matched functions |
| WI-006 | Reconstruct PC zBWare layouts, matrix paths, strings, and bounds | lead | bMemory.cpp, bMatrix.cpp, bMath.cpp, Strings.cpp, bVector.cpp | static/build/match | done | E-006, E-009 | Ghidra-guided pass reaches 67.43484% fuzzy, 11,360 exact bytes, and 159/467 exact functions |

## Coverage
- [x] Recon/analysis complete for in_scope assets
- [x] Critical/High candidates triaged (or N/A for pure RE)
- [x] Validated findings have Evidence (E-*)
- [x] Path documented (attack/call/solve)
- [x] Timeline continuous across major phases
- [x] Report via docs-generator
- [x] field-journal anonymized
- [x] Static/runtime boundary and missing canonical ELF inputs documented

## Refs
- skills/ops/timeline-workitem.md
- skills/ops/evidence-finding-path.md
