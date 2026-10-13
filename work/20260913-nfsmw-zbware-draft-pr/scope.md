# Case Scope

## meta
- case_id: 20260913-nfsmw-zbware-draft-pr
- created: 2026-09-13T20:14:50+02:00
- operator: local
- project_root: /Users/tihan-nico/NFS MW Remaster
- primary_skill: reverse-engineering/SKILL.md
- primary_id: R0
- lead_role: lead
- specialist_roles: []
- hint: Create draft upstream pull request for zBWare reconstruction in dbalatoni13/nfsmw
- preset: none

## auth
- status: granted
- basis: own_system
- evidence_of_auth: user explicitly authorized contribution to https://github.com/dbalatoni13/nfsmw using the nanashili account
- MUST NOT proceed if status != granted

## in_scope
- assets:
  - https://github.com/dbalatoni13/nfsmw
  - /Users/tihan-nico/.codex/worktrees/1a8f/NFS MW Remaster/Library/PCDecompilation/nfsmw-pc-match
  - /Users/tihan-nico/.codex/worktrees/1a8f/NFS MW Remaster/Library/PCDecompilation/nfsmw-pc-match/orig/SPEED_EXE_1_3/speed.exe
  - /opt/homebrew/opt/ghidra/libexec
  - /Users/tihan-nico/.local/bin/ghidra
  - /private/tmp/ghidra-cli-install/bin/ghidra
  - /private/tmp/nfsmw-zbware-ghidra-cli
  - /Users/tihan-nico/NFS MW Remaster/work/20260913-nfsmw-zbware-draft-pr/artifacts
- surfaces: []
- activities:
  - offline PE inspection and headless decompilation
  - Ghidra CLI project import and function decompilation
  - source reconstruction, matching build, and focused objdiff verification
  - authorized fork branch and draft pull request publication

## out_of_scope
- assets: []
- activities: [dos, phishing_real_users, unrestricted_exfil]

## network_profile
- mode: authorized_target_only
- notes: |
    offline | lab_only | authorized_target_only | unrestricted_lab
    Change mode only after auth.status = granted.
    Presets: offline-sample | ctf-public | own-system

## deliverables
- report: true
- field_journal: true
- diagrams: true
- timeline: true

## constraints
- timebox: {}
- stealth: low
- data_handling: anonymize

## Input boundary

- The repository's canonical `orig/GOWE69/NFSMWRELEASE.ELF` and `orig/SLES-53558-A124/NFS.ELF` inputs are absent in the local checkout (the directories contain only `.gitkeep` placeholders).
- The available PC `SPEED_EXE_1_3/speed.exe` was therefore imported as the explicit local Ghidra CLI fallback; no ELF/DWARF dump or runtime launch is claimed in this case.

## signoff
- ready_for_act: true
- checklist:
  - [x] auth.status = granted
  - [x] in_scope.assets non-empty OR offline sample path set
  - [x] network_profile.mode chosen
  - [x] out_of_scope reviewed
  - [x] roles assigned (see skills/ops/role-map.md)

## ops_refs
- skills/ops/scope-contract.md
- skills/ops/evidence-finding-path.md
- skills/ops/role-map.md
- skills/ops/timeline-workitem.md
- skills/ops/IDENTITY.md
