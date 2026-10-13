# 2026-09-13 zBWare PC static reconstruction

## 场景分类

Binary analysis / offline reverse engineering

## 目标概述

Recover the deterministic portion of the PC zBWare runtime from an authorized local PE sample and matching source tree, then separate source-reconstructable code from platform/runtime dependencies.

## Scope 摘要（脱敏）

- auth_basis: owner-operated offline sample
- network_profile: offline
- asset_types: [32-bit PE executable, C/C++ source tree]

## 角色

- lead_role: lead
- specialists: [cre, doc]

## 完整执行链路

1. Confirm the offline scope and identify the exact PC sample by architecture, image base, size, and SHA-256.
2. Use the SourceList, symbol map, and retail disassembly to reconstruct pure list, math, geometry, parsing, memory, slot-pool, printf, and packet-header routines.
3. Build the matching translation unit and compare sections and named functions with the local object-diff tool.
4. Record stripped/platform routines and imported D3DX behavior as a separate runtime boundary; do not execute the target in the offline case.

## Evidence 链摘要（脱敏）

| E-id | severity | status | source_type | 可复用命令模式 | 关联 Finding |
|------|----------|--------|-------------|----------------|--------------|
| E-001 | info | observed | command | `file`, `sha256sum`, and anchor validator against an authorized local PE | F-001 |
| E-002 | info | observed | command | SourceList/symbol search plus focused `objdump` ranges | F-001 |
| E-003 | info | observed | command | matching `ninja` build followed by focused `objdiff-cli` JSON comparison | F-001 |

## Finding / Path 摘要

- top_finding: Deterministic zBWare routines are buildable and statically recoverable; transport/debug/VM/D3DX behavior remains runtime-bound.
- path_type: callflow
- path_one_liner: PE identity → symbol/disassembly map → source reconstruction → matching build → objdiff → runtime boundary.

## 踩坑记录

| 问题 | 原因 | 解决方案 | 耗时 |
|------|------|---------|------|
| RuntimeTrace validator was first invoked from the matching worktree | The validator is maintained in the project tools tree | Re-run it from the project root with the target path supplied explicitly | brief |
| Some target routines did not have public symbols | Retail stripping and platform imports remove stable source anchors | Keep semantic reconstruction and classify the remainder as runtime/platform work | ongoing |

## 工具链发现

The reliable loop was: exact `rg`/symbol lookup, bounded `objdump`, historical MSVC matching build, then focused `objdiff-cli`. Section-level fuzzy scores are useful progress signals; they do not prove runtime equivalence.

## 关键代码/命令

```text
ninja build/SPEED_EXE_1_3/src/Speed/Indep/SourceLists/zBWare.obj
build/tools/objdiff-cli diff -p . -u 'main/Speed/Indep/SourceLists/zBWare' -o /tmp/zbware-final.json --format json
python3 Tools/AI/RuntimeTrace/validate_target.py --target {pc_match_worktree}/orig/SPEED_EXE_1_3/speed.exe --catalogue Tools/AI/RuntimeTrace/hook_catalogue.json
```

## 可复用的模式/脚本片段

Preserve the target hash and comparison command with every reconstruction batch; use a case-local Evidence record for each measurement and keep runtime-dependent functions out of a static parity claim.

## 进化动作

- [ ] 更新了路由矩阵
- [ ] 更新了 tool-index
- [ ] 更新了 bootstrap-manifest
- [ ] 更新了子 skill 文档
- [ ] 新增了 pitfalls 记录
- [x] 无需更新

## 环境信息

- OS: macOS on an owner-operated workstation
- 工具版本: repository-pinned Python 3 and MSVC matching build tools
- 目标平台/版本: Need for Speed: Most Wanted PC 1.3, Intel 80386 PE

## 脱敏要求

All machine-specific paths are represented by `{pc_match_worktree}` or `{install_dir}` placeholders. No credentials, tokens, user names, domains, or network targets are recorded.
