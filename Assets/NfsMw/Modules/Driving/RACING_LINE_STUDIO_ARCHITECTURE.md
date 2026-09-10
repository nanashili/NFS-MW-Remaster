# Racing Line Studio — architecture and delivery map

Unity 6000.6.0f1; URP 17.6; Splines 2.9; Input System 1.20;
Test Framework 1.8. No package or project-settings migration is required.

## Ownership and audit

`RoadNetworkAsset` owns published 3D lane samples, directed successors and collision
chunks. The studio consumes those publications. `MissionCourse` owns mission
progress; it is not a lane-route editor. A dedicated Race Route Editor, Physics
Lab capability publisher and opponent trajectory controller are not present in
this checkout. The narrow `RacingLineRoute` ordered-lane-span adapter, capability
input and tracking input are explicit integration seams, not replacement topology,
mission, traffic or opponent-tactics systems.

`VehicleController` owns input smoothing, powertrain, wheels, aero and handling.
`VehicleAssists` owns brake-to-drift interpretation. The studio never adds yaw
torque or changes velocity while a trial runs. Initial-condition placement is
allowed only before stepping. Local physics queries and an explicit guarded step
are required because manual `PhysicsScene.Simulate` does not call `FixedUpdate`.

```text
Road publication → ordered route occurrences → immutable corridor snapshot
Vehicle tuning + installed upgrades + controller settings → dependency digest
Capability measurements + author hints → bounded geometric/speed planner
    → transient candidates → production-physics rollouts → immutable publication
                            ↘ telemetry/rejections       → runtime target reader
UI Toolkit + Scene handles call these modules; they do not own simulation logic.
```

## Schemas and invariants

- Source assets hold stable IDs, route occurrences, hints, exclusions and settings.
  Generated trajectories and run reports are separate. Duplication is explicit;
  duplicate IDs are errors, not an excuse to silently change identity on import.
- Samples use metres, seconds, m/s, radians and signed 1/metre curvature. Route
  distance, lane station, occurrence and branch identity are distinct. Banking and
  elevation use the published normal/left frame; no XZ-only projection.
- Fingerprints cover road/route samples, surfaces, tuning, ordered effective
  upgrades, chassis dimensions, control/physics revisions, fixed step, planning
  parameters, capability measurements and authored constraints. No instance IDs.
- Authored, Generated, Verified, Failed and Stale are different states. A geometric
  candidate never implies measured feasibility. Intended/achieved speeds remain
  separate. A failed or cancelled attempt does not replace the last publication.
- Feasibility errors cannot be traded for lap-time score. Airborne optimization
  and unknown overhead geometry are not claimed by the analytic surrogate.
- Passive preview does not access profiles, wallets, missions, PlayerPrefs or saves.
- Local physics scenes contain only explicit road collision chunks and approved
  vehicle physics rigs. Each attempt creates fresh controller/assist/drivetrain
  state. No global simulation mode, fixed step, gravity or time scale is changed.
- Source schema 2 and artifact schema 2 are distinct. The compact artifact retains
  the verified reset-entry envelope and summary, not raw run histories. OS family,
  verifier revision, native solver/contact configuration and aero settings take
  part in compatibility. Runtime input brakes on stale or unsupported rigs.
- Editor changes use SerializedObject/Undo. Publishing creates a new uniquely
  named immutable asset, then switches the source reference with Undo. Undo leaves
  the old publication file recoverable; it is not filesystem rollback.
- Optimization is cooperatively budgeted on the editor thread, with cancellation
  and a captured dependency digest checked before accepting a result. No worker
  touches Unity objects and no native buffers/jobs need speculative ownership.

## Dependency-ordered milestones

1. M01: source/route/setup/capability/artifact contracts and validation.
2. M02: corridor sampling, author handles, pinned hints, bounded refinement.
3. M03: capability/combined-demand/grade-aware forward and backward speed planning.
4. M04: disposable local-physics rollout using the production fixed-step sequence.
5. M05: ordinary-input B2D cues and separate engagement/recovery diagnostics.
6. M06: line families, perturbed entry trials and companion occupancy reports.
7. M07: revision-aware publication/runtime reader, charts and telemetry export.
8. M08: cancellation, stale-result rejection, lifecycle/regression tests and demo.

This is the implementation sequence, not a claim of universal feasibility.
Executed gates and remaining qualifications are recorded in
[the validation report](RACING_LINE_STUDIO_VALIDATION.md); the
[usage guide](RACING_LINE_STUDIO.md) maps the adapters and authoring workflow.

## Test strategy

Test the planner through snapshot → operation → result: finite inputs, missing IDs,
disconnected occurrences, 3D footprint, deterministic generation, pins, sector
continuity, speed/braking envelopes, invalidation and cancellation. Test editor
Undo, immutable publication, duplicate IDs and failed writes. Test real vehicle
movement, isolated contacts, reset repeatability and B2D through the production
step. Run scene-changing checks in a disposable project, not the user's workspace.
Record sample counts, cold/warm timings and machine details; editor timing is not
a shipping Mac/Windows budget. Research choices and uncertainty are documented in
[the source note](Research/RACING_LINE_STUDIO_RESEARCH.md).
