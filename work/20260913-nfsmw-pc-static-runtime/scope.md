# Case Scope

## meta
- case_id: 20260913-nfsmw-pc-static-runtime
- created: 2026-09-13T16:36:07+02:00
- operator: local
- project_root: /Users/tihan-nico/NFS MW Remaster
- primary_skill: reverse-engineering/SKILL.md
- primary_id: R0
- lead_role: lead
- specialist_roles: [cre, doc]
- hint: game reverse source recovery static decompile runtime classification
- preset: own-system

## auth
- status: granted
- basis: own_system
- evidence_of_auth: preset:own-system/lab
- MUST NOT proceed if status != granted

## in_scope
- assets:
  - /Users/tihan-nico/NFS MW Remaster
  - /Users/tihan-nico/.codex/worktrees/1a8f/NFS MW Remaster/Library/PCDecompilation/nfsmw-pc-match
- surfaces: [retail_pe32, matching_cpp_corpus, objdiff_report, schema3_trace_analysis]
- activities: [offline_inventory, static_source_recovery_assessment, runtime_visibility_gap_analysis, reporting]

## out_of_scope
- assets: []
- activities: [target_execution, live_instrumentation, binary_patching, network_access, dos, phishing_real_users, unrestricted_exfil]

## network_profile
- mode: offline
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
