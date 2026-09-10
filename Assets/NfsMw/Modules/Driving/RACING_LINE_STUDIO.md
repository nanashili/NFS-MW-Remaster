# Racing Line Studio

Practical guide for the current Racing Line Studio implementation in Unity
6000.6.0f1. This document describes the code that exists in this checkout, not
the complete scope advertised by the original implementation prompt. It is
deliberately explicit about the seams that are still adapters and about the
measurements that do not yet exist.

The authoritative validation record is
[RACING_LINE_STUDIO_VALIDATION.md](RACING_LINE_STUDIO_VALIDATION.md). This guide contains no test
pass counts and is not a substitute for that report.

## What the studio is

Racing Line Studio is an editor-side authoring, bounded candidate-generation,
capability-calibration, production-physics rollout, and publication workflow.
It produces a route-local trajectory candidate, ordinary vehicle-input cues,
diagnostics, telemetry, and—only after a matching successful rollout—an
immutable `RacingLineArtifact` that runtime code can read.

The useful mental model is:

```text
published RoadNetworkAsset
        + ordered RacingLineRoute spans
        + RacingVehicleSetup (tune, upgrades, footprint, controller, dt)
        + RacingCapabilityProfile
        + hints and exclusions
                    |
                    v
       detached RacingLineSnapshot + SHA-256 fingerprint
                    |
                    v
       bounded geometry/speed candidate (disposable)
                    |
                    v
       isolated shared-vehicle rollouts and telemetry
                    |
                    v
       immutable RacingLineArtifact -> optional RacingLineInput adapter
```

The editor UI is a coordinator. The detached snapshot, planner, trajectory
reader, tracker, vehicle rig, calibration, and rollout classes own the actual
work. A planner step does not touch a `ScriptableObject`, scene, `Rigidbody`,
save, wallet, mission, or player profile.

## Advertised scope versus current implementation

The supplied prompt asks for an integrated tool around Race Route Editor,
Vehicle Physics Lab, shared vehicle simulation, and an AI controller. The
current repository has the shared road publication and vehicle simulation, but
does not contain those three dedicated upstream/consumer systems as named
products. The explicit adapters are intentional and should not be mistaken for
replacement systems.

| Prompt scope | Current source-grounded status |
| --- | --- |
| Grip and drift candidates | Present as `Grip` and `Drift` families. Drift emits ordinary brake/steering cues, but engagement, sustained slip, momentum loss, and recovery are rollout observations, not guaranteed behavior. |
| Alternate lines | Present as `Inside`, `Outside`, `SideBySide`, `TrafficBypass`, and `RecoveryEntry`. They are candidate families, not seven permanently independent assets per corner. |
| Route/corridor integration | `RacingLineRoute` consumes a published `RoadNetworkAsset` through ordered lane-span occurrences. A dedicated Race Route Editor is not present in this checkout. |
| Vehicle Physics Lab integration | `RacingCapabilityProfile` is the input seam. The Studio has an isolated straight-line calibration action, but no separate Physics Lab publisher is present. |
| Shared production physics | `RacingVehicleRig` clones a physics-only prefab or builds a narrow synthetic fixture and calls the shared `VehicleController` in a local `PhysicsScene`. |
| Opponent trajectory consumer | Not present. `IRacingTrajectory` and `RacingLineInput` are narrow runtime seams; tactical selection, pacing authority, traffic prediction, and permissions remain outside the Studio. |
| Geometry and speed planning | Present as a bounded heuristic refinement plus forward/backward capability-based speed propagation. It is not a global minimum-time or nonlinear optimal-control solver. |
| Robust verification | Present as seeded entry perturbation trials and an optional inside/outside simultaneous-occupancy run. The final evidence and any runtime smoke result belong in the validation report. |
| Reference-game fidelity | Not established. The synthetic example and capability defaults are not NFS telemetry, original assets, or a perceptual fidelity claim. |
| Reference capture import/review | Editor-only still-image attachments, source/permission/build/setup metadata, synchronization stations, visible speeds with uncertainty, and observations appear in Reference review. This is manual annotation, not calibrated video reconstruction. |
| Export | Candidate JSON, report JSON, and telemetry CSV are available from the Compare & export panel. |
| Immutable publication | Present. Publication requires a successful non-companion report and creates a new uniquely named artifact; the previous artifact is retained. |

The current lateral capability is a conservative, unmeasured assumption. Even
after straight-line calibration, `lateralMeasured` remains false and the
calibration evidence explicitly says that the lateral limit is not measured.
Do not describe a generated candidate as NFS 2015-fidelity, shipping-ready, or
perceptually matched without separate evidence.

## Ownership boundaries

| Concern | Authoritative owner | Studio contract |
| --- | --- | --- |
| Road topology, lane frames, surface grip, collision chunks | Published `RoadNetworkAsset` from the Road authoring pipeline | Consume only published lanes/chunks and their directed successors. If loaded authoring is newer than the publication, capture fails with `LINE_ROAD_STALE`. |
| Legal route progress | The upstream route/event owner when it exists | Current `RacingLineRoute` is an explicit ordered-lane-span adapter. It does not own missions, event objectives, junction policy, or a second road graph. |
| Vehicle forces, wheels, powertrain, aero, handling assists, telemetry | `VehicleController`, `VehicleWheel`, `VehiclePowertrain`, `VehicleAssists` | Supply ordinary `VehicleInputState`; use the shared controller's manual-step entry point only in an isolated harness. |
| Brake-to-drift interpretation | `VehicleAssists` and the shared handling model | `BrakeToDrift` is a timed input cue. The Studio never sets yaw, slip angle, velocity, or assist torque to force a result. |
| Capability data | Future Physics Lab publisher, or the current `RacingCapabilityProfile` adapter | A capability profile is tune-specific. Straight calibration writes measured longitudinal points and keeps lateral capability labelled as an assumption. |
| Tactical opponent choice and pacing | Runtime racing AI/controller | The Studio supplies reusable trajectories and reports. It does not implement a second opponent brain or guarantee a pass is safe in arbitrary traffic. |
| Mission, career, wallet, save, player state | Existing game systems | Preview and calibration do not access them. The example rig is explicitly named as having no profile, wallet, or save modules. |

Relevant assembly boundaries are:

- Runtime: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/` in
  `NfsMwRemaster.Driving`.
- Editor: `Assets/NfsMw/Modules/Driving/Editor/RacingLines/` in
  `NfsMwRemaster.Driving.Editor`, which references the runtime, road authoring,
  and Splines assemblies.
- EditMode tests: `Assets/NfsMw/Modules/Driving/Tests/Editor/RacingLines/` in
  `NfsMwRemaster.Driving.Tests`.

## Prerequisites

Use the project versions already selected by the checkout:

- Unity `6000.6.0f1` (`ProjectSettings/ProjectVersion.txt`).
- Universal Render Pipeline `17.6.0`.
- Splines `2.9.0`.
- Input System `1.20.0`.
- Unity Test Framework `1.8.0`.

No package or project-settings upgrade is required for this feature. The
Studio uses UI Toolkit for the window shell and IMGUI containers for the
inspector/workbench controls. Scene overlays use `Handles`; this is not a
replacement for the road editor or the runtime game UI.

Before authoring a real line, have these inputs available:

1. A current, published `RoadNetworkAsset` containing valid lane samples and
   collision chunks.
2. An ordered legal route expressed through `RacingLineRoute.spans`.
3. A `RacingVehicleSetup` for the exact tune, installed upgrades, dimensions,
   fixed step, and controller settings that will drive the line.
4. A `RacingCapabilityProfile` with at least two increasing speed samples,
   including standstill.
5. A saved scene or a disposable copy for the example/validation work. Do not
   run scene-changing batch methods against the user's live project while the
   editor is open.

## Create or open a Studio document

### Open the window

Use **NFS MW Remaster > Racing Lines > Racing Line Studio**. A
`RacingLineSource` asset can also be double-clicked: the window's
`OnOpenAsset` callback selects it automatically.

For the supplied drivable scene, use **NFS MW Remaster > Racing Lines > Open
Compound Corner Example**, then press **Play**. The supplied publication is
qualified on macOS; on Windows, recalibrate, generate, verify and publish for
that platform first. A different OS family intentionally invalidates its fingerprint.

The toolbar contains:

- **New** — creates a `RacingLineSource` asset at a selected path.
- **Duplicate with new IDs** — clones the source, gives the document, hints,
  and exclusions new identities, and clears `published`. It intentionally keeps
  the same route, vehicle, and capability references.
- **Create example** — invokes the synthetic compound-corner generator.
- **Guide** — opens this file from the project.

Selecting another document cancels/disposes active work, clears transient
variants, resets the station, and captures the new document. A source change,
Undo/Redo, assembly reload, Play Mode transition, or window close also cancels
work where appropriate.

### Assign dependencies in the inspector

The left pane is headed **AUTHORING & DEPENDENCIES**. It displays the unit
contract—metres, seconds, m/s, and the lane's directed left/up frame—and edits
the following source fields:

`route`, `vehicle`, `capability`, `planner`, `verification`, `hints`,
`exclusions`, `referenceNotes`, and the `published` reference.

`referenceCaptures` holds editor-only still images and explicit uncertainty/
provenance annotations. These images are not part of the runtime artifact.

The route, vehicle, and capability assets are shown again in nested inspectors.
That makes the input contract visible without opening another asset. While
work is busy or the editor is entering Play Mode, the authoring controls are
disabled.

Use **Save authoring assets** after deliberate inspector changes. It saves the
selected source and the selected route, vehicle, and capability dependencies if
they are assigned. It does not save transient candidates or reports as source
data.

## Authoring inputs

### `RacingLineRoute` and route spans

`RacingLineRoute` is schema `1` and contains:

- `network`: the published `RoadNetworkAsset`.
- `closed`: whether the final span must join back to the first span.
- `spans`: ordered `RacingRouteSpan` occurrences.

Each span has a stable `id`, a non-empty `branch` label, an exact `laneId`,
`startMetres`, and `endMetres`. An `endMetres` of `-1` means the lane's end.
Distances follow the lane's legal travel direction. The adapter validates:

- the lane exists in the selected network and has a unique identity;
- each occurrence ID is present and unique;
- start/end are an increasing interval within the lane;
- adjacent spans are contiguous on the same lane or use a declared successor;
- joined positions are within the small positional tolerance and do not reverse
  their forward frame;
- a closed route's seam joins and its first/last positions meet; and
- sampling stays within the bounded `maximumSamples` budget.

`station` in the resulting snapshot is concatenated route progress. It is not
the same thing as the source lane's `roadStation`. Occurrence ID, branch ID,
lane ID, route station, and road station must remain distinct when debugging a
branch or bridge.

If an upstream route editor becomes available, it should publish this ordered
occurrence contract rather than making the Studio infer a route by nearest
lane. Missing, renamed, split, or merged lanes require deliberate remapping.

### `RacingVehicleSetup`

The setup is the exact runtime identity for the line:

- `tuning`: the base `VehicleTuning` asset;
- `upgrades`: the ordered installed performance upgrades;
- `physicsPrefab`: optional physics-only `VehicleController` prefab;
- `dimensions`: full body width/height/length in metres;
- `wheelbase` and `trackWidth` in metres;
- `fixedStep` in seconds; and
- `controller`: line-tracker gains and lookahead settings.

If no prefab is assigned, the isolated harness builds a narrow four-wheel
synthetic fixture around the shared vehicle controller. If a prefab is
assigned, it must pass `RacingVehicleRig.ValidatePrefab`:

- one root `Rigidbody` and four `VehicleWheel` components;
- one enabled, non-trigger root `BoxCollider` matching `dimensions`, centred in
  X/Z, with no custom physics material;
- an enabled controller and a dynamic, gravity-driven, unconstrained body with
  automatic inertia;
- unit root scale and no parent;
- only approved physics components; and
- a `VehicleModuleHost` with no gameplay module components, including external
  module references.

Serialized powertrain, assist, nitrous and module-host references must also belong
to that same chassis; external references are rejected before the prefab is cloned.

Do not use a player, career, profile, mission, wallet, or save root as the
physics prefab. It would cross the ownership boundary and make preview state
unsafe to discard.

The effective tuning is a runtime copy of `tuning` with every upgrade applied.
The copy is disposed after capture/calibration/rig teardown. Source tuning is
never modified by planning or rollout.

### `RacingCapabilityProfile`

The profile is schema `1`. Its points are speed-indexed and contain positive
acceleration, braking, and lateral-acceleration values. Validation requires:

- at least two and no more than 256 points;
- strictly increasing speeds from `0` m/s;
- finite positive acceleration, braking, and lateral limits; and
- a matching `vehicleFingerprint` whenever the profile is marked measured or
  carries a fingerprint.

The default profile is explicitly labelled:

> Uncalibrated conservative design assumptions; not measured vehicle capability.

The current **Measure & apply capability** action measures a dry, straight,
grip-1 shared-physics fixture. It records full-throttle acceleration and
service-brake deceleration, filters shifting samples, and stores a conservative
lower-quartile result scaled by `0.7`. It writes four longitudinal points
covering the observed speed range. It sets `longitudinalMeasured = true`,
`lateralMeasured = false`, and records Unity version, fixed step, sample count,
and the calibration method in `evidence`.

The action does not measure cornering, drift initiation, drift recovery,
banking, airborne behaviour, wet surfaces, or traffic. Its lateral value stays
at the conservative `3 m/s²` placeholder. Recalibrate after tuning, upgrades,
controller, rig, fixed-step, gravity, or relevant physics changes.

### Planner and verification settings

The serialized defaults in the current source are:

| Group | Setting | Default | Unit/meaning |
| --- | --- | ---: | --- |
| Planner | `spacing` | 2 | corridor sample spacing, m |
| Planner | `iterations` | 60 | bounded refinement iterations |
| Planner | `refinementStrength` | 0.08 | geometric step strength |
| Planner | `clearance` | 0.3 | required body clearance, m |
| Planner | `maximumSpeed` | 30 | planner ceiling, m/s |
| Planner | `entrySpeed` | 0 | initial target cap, m/s |
| Planner | `exitSpeed` | 8 | open-route terminal target cap, m/s |
| Planner | `gripSafety` | 0.7 | dimensionless safety multiplier |
| Planner | `brakingDelay` | 0.25 | response delay, s |
| Planner | `maximumVerticalAcceleration` | 3 | vertical envelope, m/s² |
| Planner | `maximumSamples` | 2048 | hard sample budget |
| Planner | `seed` | 2005 | deterministic perturbation seed |
| Controller | `minimumLookahead` | 5 | minimum pure-pursuit lookahead, m |
| Controller | `lookaheadSeconds` | 0.5 | speed-scaled lookahead term, s |
| Controller | `speedGain` | 0.6 | speed controller gain |
| Controller | `integralGain` | 0.06 | bounded speed integral gain |
| Controller | `yawDamping` | 0.1 | dimensionless damping |
| Verification | `trials` | 5 | seeded rollout attempts |
| Verification | `maximumSeconds` | 180 | per-trial budget, s |
| Verification | `entrySpeedPerturbation` | 1 | ± entry-speed perturbation, m/s |
| Verification | `entryOffsetPerturbation` | 0.25 | ± lateral entry offset, m |
| Verification | `gripPerturbation` | 0.05 | ± dimensionless grip scale |
| Verification | `reactionDelayPerturbation` | 0.05 | maximum added delay, s |
| Verification | `maximumLateralError` | 1.5 | tracking tolerance, m |
| Verification | `maximumSpeedError` | 5 | RMS speed tolerance, m/s |
| Verification | `maximumAirborneSeconds` | 0.2 | contact-loss tolerance, s |
| Verification | `maximumSaturationFraction` | 0.15 | steering saturation fraction |
| Verification | `maximumSlipDegrees` | 65 | body-slip tolerance, degrees |

All settings have wider validation bounds than their UI defaults. Raising a
bound does not make an infeasible candidate valid; it changes the explicit
budget/tolerance and should be recorded in the validation report.

### Hints and exclusions

`RacingLineHint` values are authored in route station space:

| Hint | Current effect |
| --- | --- |
| `Entry`, `Apex`, `Exit` | Pulls the candidate toward the signed lateral target inside the radius. |
| `Pin` | Pulls with full weight and pins nearby refinement samples. |
| `SpeedLimit` | Adds a local target-speed ceiling; it does not pull geometry. |
| `BrakeToDrift` | Stores brake, cue duration, and steering for a drift-family ordinary-input cue. It is ignored as a geometry pull. |

The fields are `station` (m), `radius` (m), `lateral` (m), `speed` (m/s),
`brake` (`0..1`), `cueSeconds` (s), and `cueSteering` (`-1..1`). Positive
`lateral` means metres left of the directed lane centre, using the lane's
published `left` vector—not world X. A hint outside footprint clearance is
reported as `HINT_OUTSIDE`; the road is never widened to accommodate it.

`RacingLineExclusion` contains a route-station interval and a forbidden lateral
interval. It is a route-local occupancy constraint. It is not a scene obstacle
database and does not account for arbitrary meshes, moving traffic, or
overhead geometry.

## Editor workflow

### 1. Validate the inputs first

Select the document and inspect the status bar. A ready message includes the
corridor sample count and route length. A capture failure is actionable: fix
the named route, identity, capability, setup, or settings issue before
generating.

A source capture takes a detached copy of all authoring inputs and builds a
SHA-256 fingerprint. The digest includes the source/route IDs, published road
identity and fingerprint, ordered spans and sampled corridor frames, effective
tuning/upgrades, body dimensions, controller, fixed step, gravity and selected
physics settings, planning parameters, capability data, verification settings,
hints, and exclusions. It deliberately does not use transient instance IDs.

### 2. Choose a family and inspect the corridor

In the right pane choose one of the seven `Line family` values:

1. `Grip`
2. `Drift`
3. `Inside`
4. `Outside`
5. `SideBySide`
6. `TrafficBypass`
7. `RecoveryEntry`

The **Corridor**, **Variants**, and **Measured path** toggles control Scene view
overlays. **Frame in Scene** moves the last active Scene view to the current
station. The station slider is route progress in metres and drives the white
station marker/body footprint preview.

The corridor edges are drawn from the published 3D position/left frame. The
candidate is drawn above the road using its normal. Variant colour encodes the
selected family, stale state, errors, and target-speed range. A green measured
path is only recorded rollout telemetry; it is not the planned trajectory.

### 3. Author hints with controls

The row of `+ Entry`, `+ Apex`, `+ Exit`, `+ Pin`, `+ SpeedLimit`, and
`+ BrakeToDrift` buttons inserts a hint at the current station. The source
mutation is recorded by Unity Undo. Adjust the complete hint in the inspector.

In Scene view, each hint has a lateral `Handles.Slider` along the local lane
left vector. Dragging it changes only the hint's signed lateral offset and is
recorded as **Move racing line hint**. Red indicates that the body plus the
hint offset exceeds available width. The handle does not alter road geometry,
AI permission masks, or route legality.

Author exclusions in the `exclusions` array. The red translucent polygons show
their route-local lateral intervals. A blocked footprint produces an error;
the planner does not silently jump across the excluded area.

### 4. Generate a candidate

Use **Generate / refine**. The window captures the source, validates duplicate
document identity, creates a `RacingLinePlanner`, and advances it in small
editor-thread work slices. The planner is cooperative and bounded; it can be
cancelled without touching source assets.

The geometric phase:

- starts from the corridor centreline;
- derives a family target from local bend direction and available footprint
  room;
- applies authored hint pulls and pins;
- bounds each sample by vehicle half-width plus configured clearance;
- honours route-local exclusions;
- refines smoothness using a bounded local curvature-like stencil; and
- rejects swept-width, heading-break, coincident-edge, and other hard errors.

This is a candidate generator, not a proof of minimum lap time. `baselineCost`
and `geometricCost` are geometric surrogate values, not measured sector time.
The candidate remains `Generated` unless a later rollout marks it `Verified`.
An error changes it to `Failed` and blocks both rollout and publication.

Use **Regenerate sector** only after a full candidate exists. Enter finite
`Sector start (m)` and `End (m)` values. The planner preserves samples outside
the selected interval and pins a two-spacing continuity margin at its edges.
It rejects a changed route sample mapping and reports
`SECTOR_SPEED_BOUNDARY` if the new sector needs braking outside the selected
range. Expand the sector or regenerate the full line; do not assume a local
apex edit is independent of downstream speed.

Use **Generate all 7 families** to queue each enum value. The queue is
serialized through the same bounded work loop; it does not run physics scenes
in parallel. Each family is shown as a separate transient variant.

### 5. Inspect trajectory and speed

The **Trajectory & speed** panel shows:

- estimated candidate time in seconds;
- numerical planner compute time in milliseconds;
- geometric surrogate cost before and after refinement;
- target speed and local speed ceiling in m/s;
- lateral and vertical curvature in `1/m`; and
- planned body clearance in metres.

Speed planning applies, in order, local road/speed/hint caps, curvature and
vertical-curvature caps, then bounded forward acceleration and backward braking
propagation. Combined lateral/longitudinal demand, grade, surface grip,
grip-safety, shift duration, and the explicit braking response delay are
represented in the surrogate. Intended `targetSpeed` is stored separately
from rollout `actualSpeed`.

The planner emits `BANK_GRIP`, `GRADE_STALL`, `BRAKING_GRADE`,
`SPEED_CONVERGENCE`, and `ZERO_SPEED_SECTOR` diagnostics when this candidate
cannot be made consistent. It does not turn an invalid value into a valid one
by claiming the controller will under-drive it.

### 6. Calibrate the longitudinal capability

Select **Measure & apply capability** only when the correct vehicle setup is
selected and the editor is not in Play Mode. The editor creates a disposable
preview scene, a synthetic dry straight, and an approved physics rig. It
explicitly drives the shared vehicle's control/physics step and local physics
simulation, then disposes the rig and scene.

On completion, the capability asset is modified with Undo, marked dirty, and
the status message reminds the author that lateral capability is still an
assumption. Regenerate after applying the new points because capability data
participates in the document fingerprint.

Calibration is not a corner rollout and is not a substitute for verification.
If the vehicle leaves the straight, produces non-finite state, or lacks speed
coverage, the calibration is discarded and the error is retained for diagnosis.

### 7. Verify with the actual shared physics

Select **Verify robust entry batch** for the selected candidate. The editor
creates a fresh isolated local physics scene for each trial, copies only
published collision chunks, creates fresh vehicle/controller/assist state, and
uses the same candidate snapshot. Trial zero is the unperturbed baseline; the
remaining trials draw deterministic entry speed, lateral offset, grip, and
reaction-delay perturbations from the document seed and configured bounds.

Only collision chunks intersecting the route vicinity are cloned. A rollout is
rejected above 512 such chunks or two million collision vertices; use a shorter
sector instead of silently omitting geometry. Contact loss, tracking, slip and
stall checks cover both cars in paired runs. Companion clearance is a conservative
oriented-box separation lower bound, not an exact Euclidean distance.

Raw telemetry is adaptively sampled to a maximum of 50,000 entries per run;
constraint statistics still examine every physics step. The report records the
telemetry interval. Report import checks the revision/trajectory, finite values,
non-null trials/traces, and the same sample limit before opening charts.

The **Physics telemetry** panel can show live telemetry during a rollout. The
**Pause rollout** and **Single physics step** controls are intended for slow
diagnosis. The test loop performs the control update and shared vehicle step
explicitly, calls the local `PhysicsScene.Simulate` exactly once, then reads
the resulting body and wheel state. Unity's manual simulation does not invoke
`FixedUpdate`, so the harness must not rely on a callback magically running.

The rollout records target/actual speed, route station, lateral and heading
error, ordinary steering/throttle/brake, body slip, yaw rate, applied shared
assist yaw torque, wheel contacts, clearance, handling phase, and world
position. It also records trial entry conditions, collision and boundary
counts, airborne duration, saturation fraction, maximum slip, and a diagnosis.

The default trial pass requires completion with no configured violations:

- collision or measured footprint-clearance violation;
- lateral tracking error above tolerance;
- RMS speed error above tolerance;
- excessive airborne duration;
- excessive steering saturation;
- body slip above tolerance;
- unexpected drift on a non-drift candidate; or
- missing drift engagement or recovery on a drift candidate.

The report distinguishes a planning error from a tracking/control error. Read
the diagnosis and graphs before changing a line. A slower car may have a lower
speed error because its target was already reduced; that does not establish
that the original speed plan was physically feasible.

For a two-car check, generate both `Inside` and `Outside`, select either, and
use **Run inside + outside pair**. The pair uses simultaneous local physics and
reports actual occupancy/collision evidence. A companion run is intentionally
not publishable, and a successful pair does not prove universal traffic safety
or replace runtime tactical prediction.

### 8. Read diagnostics and measured path

The **Diagnostics** panel lists each candidate diagnostic with its station. A
station button selects and frames the corresponding Scene view location. If a
report exists, each trial diagnosis is shown below the candidate diagnostics.

The **Compare & export** panel lists all transient family variants, state,
estimated time, and measured trial summary when available. It also shows the
input and vehicle SHA-256 fingerprints. Export before closing if the raw
candidate or report is needed for investigation.

## Line families in the current generator

The family enum is a stable contract. The current geometric preferences are
intentionally simple and bounded:

- **Grip** — smooth corridor-constrained baseline with no special lateral
  family bias.
- **Drift** — a conservative drift-oriented offset plus ordinary
  `BrakeToDrift` cues; it warns that initiation/recovery require measurement.
- **Inside** — biases toward the inside of the first detected bend and retains
  that side through the linked sector.
- **Outside** — retains the opposite side. These two families do not exchange
  sides automatically at S-bend inflections; verify their complete entry/exit.
- **SideBySide** — reserves a compromise offset for simultaneous occupancy;
  it is still only a candidate until pair evidence exists.
- **TrafficBypass** — uses the corridor and exclusion constraints to find a
  route-local bypass candidate; it does not query arbitrary scene traffic.
- **RecoveryEntry** — biases early progress toward a recovery-entry posture;
  it is not a crash-recovery controller.

Every family shares the same route occurrence mapping, footprint bounds,
exclusion checks, speed planner, and artifact schema. A family name does not
change the authoritative vehicle physics.

## Safety, Undo, cancellation, and publication

### Editor mutation rules

The following source edits use Unity Undo:

- serialized source/route/vehicle/capability inspector changes;
- adding a hint;
- moving a hint with a Scene handle;
- schema 1 to 2 migration; and
- applying completed straight-line capability measurements.

Generated candidates and reports are disposable window variants. Planning does
not mutate the source. A cancelled planner has no result. A stale planner or
rollout result is rejected when its captured fingerprint no longer matches the
current source.

File exports are separate from Unity Undo. They write to a temporary sibling
file and replace/move it into place, then clean up the temporary file. Exported
JSON/CSV is an external diagnostic file, not an asset transaction.

### Lifecycle cleanup

The window registers and unregisters its editor update, Undo, assembly reload,
Play Mode, and Scene view callbacks. `Cancel()` disposes the planner, rollout,
calibration, and family queue. The rollout closes its preview scene and
destroys every rig; calibration does the same. Partial construction also calls
the same disposal path.

The harness never uses the default physics scene for manual stepping. It does
not change global simulation mode, global fixed step, gravity, time scale, or
production scene objects. It uses initial-condition velocity assignment only
at the start of a measured trial; it does not correct pose, velocity, or yaw
after physics stepping to make a line pass.

### Publish an immutable artifact

Publication is intentionally stricter than generation:

1. Generate the selected family with no candidate errors.
2. Run **Verify robust entry batch** for that exact candidate. Do not use the
   inside/outside companion report as a publication report.
3. Confirm the source has not changed since verification.
4. Choose **Publish verified revision** and select a unique `.asset` path inside
   `Assets/`.
5. The editor captures again, validates document identity, initializes a new
   `RacingLineArtifact`, writes it, reopens it against the current fingerprint,
   then records the source's `published` reference with Undo.

`RacingLineArtifact.Initialize` requires all of the following to match:

- the input fingerprint and candidate fingerprint;
- candidate ID and geometry fingerprint;
- current Unity version;
- `RacingLineTracker.Revision`;
- verification trial count;
- a passed report with every trial completed and passed;
- a non-companion run; and
- a candidate without errors.

The artifact stores a detached candidate, explicit start envelope, and compact
verification summaries. Raw traces remain in exported report JSON/CSV, not the
player asset. Reopening a document loads its compatible published candidate;
use **Load report JSON** in Compare & export to inspect the matching raw trace.
Once initialized, it is immutable: publishing a new revision creates another
asset. The previous publication is not deleted by Undo and remains recoverable
on disk. Undo changes the source reference; it is not filesystem rollback.

If artifact creation fails, the new asset is deleted or the transient object is
destroyed and the source reference is not switched. Never hand-edit the
private serialized artifact fields to bypass the gate.

## Runtime use

The current runtime path is a deliberately narrow adapter, not a complete AI
integration.

### Attach the input adapter

On the same GameObject as a `VehicleController`, add `RacingLineInput` and
assign its `source` to the document whose `published` field points to the
verified artifact. Set `pacing` only as a runtime pacing multiplier (`0..1`);
it does not rewrite the artifact.

The component has default execution order `-110` so its ordinary input is
available before the vehicle's normal `FixedUpdate`. On enable/start it:

1. captures the current source fingerprint;
2. requires the runtime `Time.fixedDeltaTime` to equal `source.vehicle.fixedStep`;
3. requires a published artifact whose fingerprint matches;
4. creates an effective tuning copy and compares it with the actual vehicle
   tuning; and
5. creates a `RacingLineTracker` over the artifact reader.

Every half second while active it rechecks the publication/source fingerprint,
runtime tuning, and fixed step. If any dependency changes, it stops tracking
and reports `LINE_STALE`. On any bind failure, the adapter applies the safe
brake/handbrake state rather than driving an unknown trajectory.

The source fingerprint capture is leased through the internal
`RacingLineRuntimeCache`. Multiple cars bound to the same source share the
validated fingerprint refresh (at most once per half second per source), the
cache allows at most 64 concurrently bound source documents, and the lease is
released when the component is disabled, rebound or invalidated. The cache retains no line history and
does not make stale artifacts valid; it only avoids repeating the same source
validation allocation once per car.

The bind also validates the runtime shape and the artifact's entry envelope.
The chassis must have the verified root box dimensions and four wheels with the
verified geometry/steering/drive/handbrake flags. The reset pose must be within
the published start speed, position, heading, up-axis, and angular-velocity
envelope. `RacingLineInput` is therefore an entry-from-reset adapter; a
mid-route join needs a separately verified transition contract. The published
reader independently rejects non-finite, unordered samples and invalid cues
before tracking starts.

The narrow adapter uses layer 2 (Ignore Raycast) for the chassis, default raycast
layers for wheel ground queries, automatic inertia, and no custom chassis physics
material. Aerodynamic enablement, native solver/contact settings and body dynamic
settings must match the verified rig. Unsupported physics modules are rejected;
the adapter does not certify arbitrary gameplay prefabs or mid-race modifications.
Slower runtime pacing remains an AI decision, not a new drift-feasibility certificate.

### What the tracker supplies

`RacingLineTracker` is pure low-level target tracking:

- bounded local projection onto the trajectory;
- target station, target speed, lateral error, and heading error;
- pure-pursuit steering using the configured lookahead and wheelbase;
- speed-error throttle/brake with bounded integral state;
- ordinary brake/steering cue timing for drift hints; and
- finished-line braking/handbrake behavior.

It does not select a family, choose a legal branch, predict opponents, decide
whether to pass, grant permission to occupy a lane, own event pacing, or apply
direct chassis corrections. A higher-level racing AI should own those decisions
and call the trajectory reader/tracker only after it has selected a compatible
line.

`IRacingTrajectory` currently exposes only `Id`, `Length`, and `Sample(station)`.
The implementation is therefore enough for a narrow follower adapter, not a
full runtime candidate registry or opponent trajectory service.

`RacingLineDemoCamera` is demonstration-only. Its OnGUI overlay displays a
failure/active message and the vehicle's km/h telemetry; it is not a gameplay
camera replacement.

## Units and coordinate conventions

| Value | Unit/convention |
| --- | --- |
| World position, dimensions, route station, road station, arc length, lateral error, clearance, hint radius/offset, lookahead | metres |
| Time, sample time, fixed step, shift duration, cue duration, rollout delay | seconds |
| Speed, target speed, actual speed, speed limits, entry/exit speed | m/s; the demo overlay alone displays km/h for presentation |
| Acceleration, braking, lateral/vertical limits, gravity-derived demand | m/s² |
| Curvature and vertical curvature | signed `1/m`; the sample frame's `up` separates lateral from vertical curvature |
| Heading error and body slip | degrees in telemetry/UI |
| Yaw rate | rad/s |
| Assist torque | N·m |
| Steering, throttle, brake | normalized input; steering is signed `-1..1`, throttle/brake are `0..1` |
| Grip and grip scale | dimensionless multiplier; the road surface profile supplies the base grip |
| Trial collisions, contacts, boundary samples | integer counts |

The road's directed frame is authoritative:

- `forward` is legal travel direction;
- `left` is the signed lateral axis;
- `up` is the published surface/up axis;
- `position` is the 3D corridor sample; and
- `width` is the corridor width in metres.

The planner and body sweep use all three axes. Do not project a banked road,
bridge, tunnel, or elevated lane into X/Z and assume the result is valid.
The vehicle footprint uses setup X width, Y height, and Z length; clearance is
computed for rotated body corners and intermediate poses, not only a zero-width
centre point.

## Diagnostics reference

The diagnostic code is intended to identify the owner and next action. The
station printed beside a code is route station in metres unless the message
says otherwise.

### Capture and authoring diagnostics

| Code | Meaning and action |
| --- | --- |
| `LINE_SCHEMA` | Source is not schema 2 or has no valid document ID. Migrate explicitly or select a current document. |
| `LINE_ROUTE` | No current published road network or ordered spans. Assign the route adapter and published network. |
| `LINE_ROAD_STALE` | Loaded road authoring is newer/different from the network publication. Publish the road revision first. |
| `LINE_LANE_ID`, `LINE_SPAN`, `LINE_SPAN_RANGE` | Lane/occurrence identity or interval is missing, duplicated, unknown, or invalid. Remap deliberately. |
| `LINE_TOPOLOGY`, `LINE_JOIN`, `LINE_CLOSED` | Adjacent spans or the closed seam are not a declared legal/contiguous 3D transition. Fix the route/network publication. |
| `LINE_SAMPLE_BUDGET`, `LINE_ROUTE_SHORT` | Spacing creates too many samples or fewer than three useful samples. Adjust the bounded setting or author a longer route. |
| `LINE_SETTINGS` | Planner setting is outside its finite bounded range. |
| `LINE_SETUP` | Vehicle identity, dimensions, gravity, upgrades, fixed step, or controller settings are invalid. |
| `LINE_PREFAB_MODULES`, `LINE_PREFAB`, `LINE_PREFAB_SCRIPT`, `LINE_PREFAB_FOOTPRINT` | Optional prefab is not a physics-only four-wheel fixture. Use a dedicated fixture. |
| `LINE_CAPABILITY` | Capability points are missing, non-finite, unordered, or not positive. |
| `LINE_CAPABILITY_STALE`, `LINE_CAPABILITY_PROVENANCE` | Measured capability no longer identifies the selected tune/rig/controller/fixed-step setup. Recalibrate. |
| `LINE_VERIFICATION` | Verification budget/tolerance is invalid. |
| `LINE_HINTS`, `LINE_HINT`, `LINE_EXCLUSION` | Authoring collection, stable ID, interval, or cue value is invalid. |
| `LINE_DUPLICATE_ID` | Raw asset duplication produced a shared source identity. Use the Studio duplicate action. |

### Candidate diagnostics

| Code | Meaning and action |
| --- | --- |
| `SURROGATE` | Capability is not measured for the planner's longitudinal path assumptions. Rollout is mandatory. |
| `DRIFT_SURROGATE` | Drift uses conservative grip speeds plus authored ordinary-input cues. Inspect engagement/recovery telemetry. |
| `DRIFT_NO_CUE` | A drift candidate has no `BrakeToDrift` hint. Add one if a real initiation sequence is intended. |
| `FOOTPRINT`, `HINT_OUTSIDE`, `SWEPT_WIDTH`, `BODY_SWEEP` | Vehicle footprint plus clearance does not fit the corridor at one or more poses. Widen/re-route the road, change the footprint, or move the hint; never widen the road in the planner. |
| `EXCLUSION_BLOCKS`, `EXCLUSION_FOOTPRINT` | The exclusion leaves no footprint-sized passage or the generated body intersects it. Edit the exclusion/corridor deliberately. |
| `COINCIDENT`, `HEADING_BREAK` | Geometry contains a zero-length edge or a sample-to-sample heading break over the project tolerance. Increase sampling quality or fix the route frame. |
| `BANK_GRIP` | Banking/gravity consumes the available lateral envelope. Check the published frame and capability assumptions. |
| `GRADE_STALL`, `BRAKING_GRADE` | Grade and combined demand make forward travel or braking infeasible in the surrogate. Inspect the 3D route and capability profile. |
| `SPEED_CONVERGENCE` | Bounded speed propagation did not reach its fixed-point tolerance. Treat the candidate as failed. |
| `ZERO_SPEED_SECTOR` | Consecutive samples cannot be traversed at the computed target speed. Fix the cap or route rather than accepting a stalled line. |
| `SECTOR_SPEED_BOUNDARY` | A sector-only edit requires braking outside its selected interval. Expand the sector or regenerate the full line. |

### Rollout and runtime diagnostics

| Code/message | Meaning and action |
| --- | --- |
| `LINE_ROLLOUT`, `LINE_ROLLOUT_STALE` | Rollout was started in Play Mode or its captured dependencies changed. Dispose the attempt and regenerate/verify again. |
| `LINE_PREVIEW_ISOLATION` | The editor did not provide a non-default local physics scene. Do not certify the run. |
| `LINE_COLLISION_MISSING` | The published road has no usable collision chunk in the preview. Fix the road publication. |
| `LINE_CONTACT_BUDGET` | The overlap query saturated its fixed collider buffer. The run cannot be certified until the fixture is reduced/owned explicitly. |
| `NON_FINITE` | Shared physics produced invalid position or speed. Inspect tune, fixture, gravity, and the preceding telemetry. |
| `AIRBORNE`, `CONTACT` | Contact loss exceeded the configured envelope. Inspect vertical corridor geometry and road collision. |
| `TRACKING`, `LATERAL` | The controller left the local tracking corridor or exceeded lateral error. Separate controller tuning from path geometry. |
| `STALL` | Ordinary inputs made no route progress for the bounded stall period. Inspect target speed, braking, grip, and drivetrain state. |
| `TIMEOUT` | The trial exceeded its explicit maximum. Review under-speed/control telemetry before increasing the budget. |
| `COLLISION`, `FOOTPRINT` | Chassis overlap or measured clearance violation was observed. Do not treat a visual line as safe. |
| `SPEED`, `CONTROL`, `SLIP` | Actual speed, steering saturation, or body slip exceeded the configured limit. |
| `DRIFT` | A drift candidate did not both engage and recover. Inspect the cue, assist phase, speed loss, and tune. |
| `UNEXPECTED_DRIFT` | A non-drift candidate activated drifting. Check the tune/assist state and controller inputs. |
| `LINE_FIXED_STEP` | Runtime fixed delta differs from the verified setup. Match it deliberately or recalibrate/reverify. |
| `LINE_MISSING` | The source has no published artifact. Generate, verify, and publish. |
| `LINE_INCOMPATIBLE` | Artifact is missing, stale, unsupported, or not verified against the expected fingerprint. Use the deliberate safe fallback. |
| `LINE_TUNE` | Actual runtime tuning differs from the verified effective tuning. Use the exact setup or publish a new line. |
| `LINE_STALE` | Source publication, tune, controller, or fixed step changed during runtime. Stop following the artifact. |

## Migration and invalidation

### Source schema migration

The current `RacingLineSource.CurrentSchema` is `2`. A schema-1 source is not
silently accepted by capture. Select it in the Studio and click
**Migrate document schema 1 → 2**. `MigrateV1`:

- records one Undo operation;
- creates missing verification, hints, and exclusions collections;
- changes only the source schema to `2`; and
- marks the source dirty.

Undo restores the previous schema/content state. The route adapter remains
schema `1`; the artifact is schema `2`; and the capability profile remains
schema `1`. There is no implicit migration that rewrites a route, artifact, or
published road. Add a deliberate migration before changing those contracts.

### Stable identity and rebuilds

Use the Studio duplicate action for a new authored document. It assigns a new
document ID plus new hint/exclusion IDs, clears the publication, and keeps
shared dependency references. A raw Unity asset duplicate that retains the
same ID is rejected by `ValidateUniqueDocument`.

Rebuilding the same source produces a deterministic candidate ordering and ID
(`sourceId.family`) within the same validated input. A rebuild does not
overwrite the hand-authored source or the prior immutable artifact.

The source fingerprint changes when any relevant dependency changes, including:

- road network publication, route spans, frame samples, surface values, or
  branch/occurrence order;
- vehicle tuning, installed upgrade order/content, dimensions, wheelbase,
  track width, controller settings, or fixed step;
- Unity/vehicle/tracker revision and selected physics settings;
- verifier revision and OS family (macOS and Windows require separate qualification);
- planning or verification settings;
- capability data and its provenance; or
- hints and exclusions.

An old candidate becomes `Stale` in the window. An old artifact remains on disk
as an old revision but fails `TryOpen` against the new fingerprint. Publish a
new verified artifact; do not edit the old artifact in place.

## Synthetic example assets

Use **Create example** in a clean/disposable project copy. The generator is
`Assets/NfsMw/Modules/Driving/Editor/RacingLines/RacingLineStudioDemo.cs` and its constants
target:

```text
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/CompoundCorner.unity
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/GripLine.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/DriftLine.asset
```

The generator also creates the supporting synthetic assets:

```text
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/Road.mat
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/Car.mat
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/DryAsphalt.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/CompoundRoadProfile.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/CompoundRoad.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/CompoundRoute.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/GripTuning.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/DriftTuning.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/GripSetup.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/DriftSetup.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/GripCapability.asset
Assets/NfsMw/Modules/Driving/Examples/RacingLineStudio/Validated/DriftCapability.asset
```

The scene is a synthetic S-bend with a placeholder chassis, a follow camera,
and a light. The drift tune deliberately differs from the grip tune and enables
the project's brake-to-drift assist; its constants are fixture values, not
NFS values. The drift source includes a `BrakeToDrift` hint around station
`90 m`. The builder initially creates an unpublished rig; the supplied `Validated`
sample includes measured grip and drift publications for the recorded macOS setup.
Any earlier partial assets in its parent folder are preserved and are not the
qualified example.

When the synchronous example validation method is used, it additionally writes
per-family diagnostics to the example folder:

```text
GripCalibration.json       DriftCalibration.json
GripCandidate.json         DriftCandidate.json
GripReport.json            DriftReport.json
GripTelemetry.csv          DriftTelemetry.csv
GripVerified.asset         DriftVerified.asset
PairReport.json            PairTelemetry.csv
Benchmark.json
```

If the folder already contains partial assets, the generator stops rather than
overwriting them. Inspect or use a fresh disposable copy; do not delete
unrelated user content to make the example fit.

## Testing and validation procedure

To rerun qualification in a fresh isolated copy, use:

```sh
bash Tools/RacingLineValidation/validate.sh
bash Tools/RacingLineValidation/validate.sh --build-mac
```

The script preserves the live editor, copies only the project inputs, validates
examples, runs the Driving tests, and optionally builds the explicit Mac demo
scene. See [the runner guide](../../Tools/RacingLineValidation/README.md) for
license/executable overrides. The script's separate syntax check is not itself
test-execution evidence; consult the validation record.

The current EditMode suite is
`NfsMwRemaster.Driving.Tests.RacingLineStudioTests` under
`Assets/NfsMw/Modules/Driving/Tests/Editor/RacingLines/`. Its source covers snapshot
validation, 3D frames, deterministic detached planning, footprint/exclusion
rejection, speed propagation, tune/controller/fixed-step invalidation,
cancellation, sector continuity, immutable artifacts, drift cue scoping,
Undo/migration/duplication, default-world guards, isolated rollout cleanup,
repeatability, and Studio open/close cleanup. The suite source is not a claim
that every check has passed on the current checkout.

`RacingLineRuntimeTests` additionally enters actual Play Mode with domain reload
enabled and disabled. It checks normal `FixedUpdate` progress and brakes on stale
tuning. Its teardown restores prior scene/editor settings; edited interactive
scenes are skipped rather than discarded. Run it in the disposable copy.

### Exact disposable-copy EditMode command

The parent validation run owns Unity execution. Do not run this from the live
editor task. Use the existing disposable copy
`/private/tmp/nfs-line-studio-Io4dQq`, one Unity process at a time, with Unity
`6000.6.0f1`:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity
COPY=/private/tmp/nfs-line-studio-Io4dQq

"$UNITY" -batchmode -nographics \
  -projectPath "$COPY" \
  -runTests -testPlatform EditMode \
  -testResults "$COPY/RacingLineStudio-EditMode.xml" \
  -logFile "$COPY/RacingLineStudio-EditMode.log"
```

The command intentionally has no `-quit`: the Test Framework owns completion
and result reporting. To focus only the Studio suite while diagnosing it, add
the installed Test Framework's filter argument before `-testResults`:

```sh
  -testFilter NfsMwRemaster.Driving.Tests.RacingLineStudioTests
```

The exact command for the synchronous synthetic example helper is:

```sh
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$COPY" \
  -executeMethod NfsMwRemaster.Driving.Editor.RacingLineStudioDemo.ValidateExamples \
  -logFile "$COPY/RacingLineStudio-examples.log"
```

That helper creates/validates the synthetic grip and drift documents, exports
their calibration/candidate/report/telemetry files, and attempts immutable
publication in the disposable copy. It is not a substitute for the dedicated
runtime smoke or the final evidence review.

### Evidence boundary

Do not record a pass count, runtime smoke result, shipping performance result,
or cleanup claim in this guide. Record the actual command, Unity version,
project-copy path, machine, cold/warm condition, sample counts, timings,
reports, logs, and remaining gates in the forthcoming
`Assets/NfsMw/Modules/Driving/RACING_LINE_STUDIO_VALIDATION.md`.

The validation report should keep these axes separate:

- source/schema and migration correctness;
- generated geometry and hard constraint residuals;
- surrogate speed-planning behaviour;
- actual shared-physics tracking and contacts;
- drift engagement/recovery evidence;
- inside/outside occupancy evidence;
- runtime artifact compatibility/safe fallback;
- editor lifecycle and Undo cleanup; and
- any interactive or platform-specific review.

Passing finite trials does not prove universal feasibility, arbitrary traffic
safety, cross-machine determinism, shipping CPU/GC budgets, or visual,
controller, or perceptual NFS fidelity.

## Troubleshooting recipes

### Capture fails before generation

Read the first `LINE_*` message in the status bar and Diagnostics panel. Fix
the owning asset instead of adjusting planner iterations. In particular,
`LINE_ROAD_STALE` means the road authoring publication must be updated;
`LINE_TOPOLOGY`/`LINE_JOIN` means the route adapter is not legal/continuous;
`LINE_CAPABILITY_STALE` means the profile belongs to another setup.

### A hint is red or the generated line is failed

The footprint is wider than the available corridor at the hint/sample, or a
rotated body corner fails the swept-width check. Move the hint in the lane
frame, widen/re-author the upstream corridor, or use the correct vehicle
dimensions. Do not remove the error by increasing a lateral target beyond the
road; the source hint should stay authored and diagnosable.

### A sector edit changes neighbouring speed

That is expected when the new sector requires earlier braking. The planner
preserves geometry outside the requested range but rejects a speed boundary
that cannot be reached without extending the sector. Expand the sector and
rerun verification.

### The line looks good but verification fails

Open **Physics telemetry** and classify the failure:

- high lateral error/steering saturation suggests tracker/controller mismatch;
- high speed error suggests capability, delay, drivetrain, or target-speed
  mismatch;
- clearance/collision suggests the box footprint or published collision is not
  represented by the visual centreline;
- airborne/contact loss suggests missing 3D collision or unsupported geometry;
- `UNEXPECTED_DRIFT` suggests the non-drift setup is not actually grip-only; and
- a drift failure means the ordinary cue did not both engage and recover under
  the actual shared assist state.

Do not fix a rollout by setting `Rigidbody.linearVelocity`, rotating the
chassis, or adding Studio yaw torque. Those would invalidate the evidence.

### Publish is rejected

Publication needs the exact candidate's successful non-companion report. Check
that the source fingerprint has not changed, all configured trials completed
and passed, the trajectory fingerprint still matches, and the current Unity
and tracker revisions match the report. Regenerate/reverify after any input
change.

### Runtime immediately brakes or handbrakes

Inspect `RacingLineInput.Failure`. The common causes are no publication,
fingerprint mismatch, fixed-step mismatch, or an actual vehicle tune that
differs from the setup's effective tuning. Runtime fallback is intentionally
fail-closed; do not bypass it with a development-wide exception.

### Example generation stops with partial assets

The example generator refuses to overwrite an existing non-empty example
folder. Preserve the folder for inspection or use a fresh disposable project
copy. It also refuses an unsaved untitled scene in the multi-scene workspace
case so that it cannot silently replace user scene state.

## Known gaps and integration work

These are concrete current-code boundaries, not hidden promises:

1. There is no dedicated Race Route Editor, Physics Lab capability publisher,
   or opponent trajectory/tactical consumer in this checkout. The narrow
   `RacingLineRoute`, `RacingCapabilityProfile`, `IRacingTrajectory`, and
   `RacingLineInput` contracts are the integration points.
2. The built-in calibration is longitudinal-only. `RacingLineSnapshot.Measured`
   currently reflects `longitudinalMeasured`; it does not independently gate
   the unmeasured `lateralMeasured` flag. The UI/evidence must therefore keep
   calling the lateral envelope an assumption even when straight calibration
   has completed.
3. The planner's lateral model is a conservative surrogate. It does not
   identify the shared tire envelope through brake-to-drift, transient load
   transfer, traction recovery, contact loss, wet weather, or airborne motion.
4. The rollout's local physics scene isolates the explicit road chunks and
   approved rigs. It is not proof that arbitrary scene statics, traffic, event
   buses, clocks, or other world services are isolated or thread-safe.
5. The inside/outside pair measures two explicit candidates from perturbed
   starts. It is not a universal passing guarantee and does not predict live
   opponents or arbitrary obstacles.
6. The runtime follower is a low-level adapter. It does not own tactical line
   selection, route permissions, opponent pacing, mission state, or a runtime
   candidate registry.
7. Reference review accepts still images and manual annotations, but does not
   reconstruct calibrated telemetry from video. No claim about original NFS timing,
   steering, drift rhythm, assets, or visual fidelity follows from the
   synthetic example.
8. The current implementation is cooperatively budgeted on the editor thread;
   it does not promise Jobs/Burst parallel rollout determinism or a shipping
   performance budget.

The next implementation/validation work should address these gaps through
explicit upstream contracts and measured evidence, not by expanding the
Studio into a second road system, physics engine, mission runtime, wallet, or
opponent brain.

## Source map

Use these files when extending or reviewing the feature:

- Runtime schemas and enums: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingLineModels.cs`.
- Source document: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingLineSource.cs`.
- Route adapter: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingLineRoute.cs`.
- Vehicle setup and effective tuning: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingVehicleSetup.cs`.
- Capability profile: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingCapabilityProfile.cs`.
- Snapshot, validation, dependency fingerprints, and tuning lease:
  `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingLineSnapshot.cs`.
- Geometry and speed planner: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingLinePlanner.cs` and
  `RacingCorridorGeometry.cs`.
- Artifact and runtime reader: `Assets/NfsMw/Modules/Driving/Runtime/RacingLines/RacingLineArtifact.cs`.
- Runtime cache, entry envelope, tracker, and input: `RacingLineRuntimeCache.cs`,
  `RacingLineArtifact.cs`, `RacingLineTracker.cs`, and `RacingLineInput.cs`.
- Isolated vehicle fixture: `RacingVehicleRig.cs`.
- Editor workflow: `Assets/NfsMw/Modules/Driving/Editor/RacingLines/RacingLineStudioWindow.cs`.
- Undo, migration, publication, and export: `RacingLineEditorOperations.cs`.
- Capability calibration: `RacingCapabilityCalibration.cs`.
- Production-physics rollout: `RacingLineRollout.cs`.
- Synthetic assets and CLI helper: `RacingLineStudioDemo.cs`.
- Current architecture/ownership note: `Assets/NfsMw/Modules/Driving/RACING_LINE_STUDIO_ARCHITECTURE.md`.
- Bounded research and uncertainty: `Assets/NfsMw/Modules/Driving/Research/RACING_LINE_STUDIO_RESEARCH.md`.
- EditMode coverage: `Assets/NfsMw/Modules/Driving/Tests/Editor/RacingLines/RacingLineStudioTests.cs`.

The completion standard remains practical: a designer must be able to author
and inspect a candidate, understand its constraints and telemetry, undo source
changes safely, rebuild it against explicit dependencies, publish only a
matching measured artifact, and consume it through the actual shared vehicle
contract. A visually attractive spline alone is not a racing line.
