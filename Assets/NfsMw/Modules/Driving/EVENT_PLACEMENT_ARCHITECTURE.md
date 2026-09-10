# Event Placement Studio architecture

## Audited baseline

Unity 6000.6.0f1, URP 17.6, Splines 2.9, Input System 1.20 and Test Framework 1.8. No engine, package, pipeline or assembly dependency changes were required. Runtime code is in `NfsMwRemaster.Driving`; authoring tools remain in the existing Editor assembly.

Observed owners:

| Responsibility | Existing owner | Placement integration |
|---|---|---|
| Directed road topology | `RoadNetworkAsset` / road authoring | Explicit lane ID, station and publication fingerprint |
| Published city entrances | `CityPublication` | Stable entrance ID and validated entrance lane |
| Race geometry, grid and entrant size | `RaceRouteDefinition` / `RaceRoutePublication` | Pin publication; reuse its checkpoints and grid |
| Race/service interaction | `FreeRoamSession` | Apply placement policy, then call existing owner |
| Mission execution and settlement | `MissionHost` / profile settlement | Start an existing catalog mission; no new objective engine |
| Availability | `CareerRequirement` / `ICareerFacts` | Compile existing expressions; synthetic or injected facts |
| Persistent player state | `CareerProfileSystem` | Never stored on definitions, publications or preview assets |
| Map display / navigation | `FreeRoamHud` / session navigation | Stable marker registry; GPS uses access rather than icon |
| Shared vehicle simulation | `VehicleController` / `RacingVehicleRig` | Isolated service-owner smoke test |

The audit found no production-wide `ICareerFacts` provider automatically available to every free-roam session, nor a generic collectible/encounter activation owner. Gated placements fail closed until facts are injected. Collectible, challenge-area and encounter-portal definitions remain inspectable but cannot publish. Do not substitute a counter or a new wallet to bypass these dependencies.

## Boundaries and schema

`WorldActivityDefinition` is reusable content: identity, adapter, presentation, mission/race/service references and career-expression JSON. JSON retains Career's bounded expression schema without exposing its recursive DTO to Unity's inline serialization depth limit.

`EventPlacementSource` owns one stable spatial identity, explicitly versioned anchors, separate interaction/staging/icon/cinematic offsets, oriented trigger and entrant envelope, direct access policy, ordered service exits, district/level/cell metadata and a pinned publication reference. The source fields are authoritative; use its Scene tool or anchor fields rather than moving the generated runtime transform with Unity's ordinary Move tool.

`EventPlacementPublication` schema 1 is initialized once and returns detached record copies. Publications pin race and mission assets; modifying a source does not rewrite old publications. Fingerprints replace transient Unity instance IDs with stable global references. Revisions are new assets, never in-place overwrites.

`WorldActivityInstance` owns registration while enabled. `ActivityRegistry` rejects duplicate placement identity and unique-definition collisions. Unregister checks ownership, so a rejected duplicate cannot unregister the winner. Registry changes reset interaction dwell. `ActivityMapCatalog` can live in a persistent bootstrap scene, retaining unloaded marker descriptors. Conflicting catalog revisions are rejected. Loaded does not imply discovered or completed.

## Validation decisions

- Resolve only the authored lane/entrance/socket. A changed fingerprint, removed lane, invalid station or missing entrance is an error; never silently project to the nearest lane.
- Access uses an explicit directed lane station and a conservative **straight** corridor to entrance/staging/exits. Check length, vertical grade, lane width, supporting collision surface, ground slope, sampled vehicle clearance with overlapping longitudinal envelopes, and the final authored orientation. Queries use the placement's physics scene.
- All required collision geometry must be loaded. A diagnostic build that skips geometry is labeled unevaluated and cannot be passed to publication (publication always rebuilds).
- Exact oriented-box overlap tests diagnose interaction conflicts in matching district/level scope. Different vertical volumes do not conflict merely because their map projections overlap.
- Runtime admission checks the full 3D volume, heading, speed, dwell, police/foreground state and career eligibility, including when callers invoke the existing owner directly.
- Race rewards are zero in the generated spatial adapter. Reward configuration remains with mission/career settlement, not this editor. Definition-scoped race completion uses definition ID; placement-scoped races use placement ID. MissionHost requires definition-scoped completion.
- Service exit occupancy uses the published entrant envelope and ordered authored candidates; if all are blocked, the session remains at the service.

## Editor, transactions and performance

Seven studio views cover catalog, placement, availability, access, map, batch and publication. Serialized edits support multi-selection and prefab overrides. Custom handles, a surface brush and a Scene overlay share the source. Lane scatter requires a reviewed interval and minimum spacing, previews before creation, validates each result and rolls back the entire batch on failure.

Publishing validates synchronously, creates a new asset, binds adapters in an Undo group and rolls back that newly created asset if the transaction fails. Ordinary Undo restores scene bindings and deliberately retains the generated asset as revision history. Import uses GUID/local-file references and stable socket IDs, rejects unresolved references and unknown schemas, and assigns a fresh placement ID. Unique definitions cannot be cloned through these commands; accidental standard Unity duplication is diagnosed before publication.

No Jobs/Burst, asynchronous bake, native buffers or background world mutations were justified. Catalog discovery runs on editor project changes. Runtime registry queries are linear in loaded activities; markers rebuild on membership changes. The UI reports actual per-validation milliseconds. No large-city frame-time or capacity guarantee has been measured.

## Delivered scope and remaining integrations

M01–M03: schemas, owner audit, anchor resolution, editor workflow and conservative direct access checks.
M04–M05: race/service/mission handoffs, career seam, isolated service-owner test and loaded/unloaded map markers.
M06–M07: atomic draft scatter, multi-edit, lifecycle registration, portable transfer and immutable publication.
M08: graybox garage sample and automated regression coverage.

These milestones are an implemented vertical slice, not certification of every aspirational item in the supplied brief. Remaining integrations are explicit: collectible/challenge/encounter runtime owners; production career/discovery/completion presentation provider; traffic reservation acquisition and release; arbitrary curved driveway connectivity/access masks; per-conflict override records; a world-streaming loader; cross-project GUID remapping; full race/mission/service storefront end-to-end playtests; large-city performance profiling. Existing road, race and mission tools retain their own validation responsibilities. The map view edits offsets through fields/handles; it is a local spatial preview, not a replacement city map renderer.

## Documentation basis

The installed editor version governs API choices. Unity's [editor extension guide](https://docs.unity3d.com/6000.6/Documentation/Manual/extending-the-editor.html), [custom tools](https://docs.unity3d.com/6000.6/Documentation/Manual/UsingCustomEditorTools.html), [custom overlays](https://docs.unity3d.com/6000.6/Documentation/Manual/overlays-custom.html), and [gizmos/handles](https://docs.unity3d.com/6000.6/Documentation/Manual/gizmos-handles-programming.html) support the chosen workspace integrations. [Graph Toolkit](https://docs.unity3d.com/6000.6/Documentation/Manual/gtk/gtk-index.html) was considered but not introduced: placements are spatial records, and existing owners already define mission and road graphs. [Workspace customization](https://docs.unity3d.com/6000.6/Documentation/Manual/CustomizingYourWorkspace.html) and [tool contexts](https://docs.unity3d.com/6000.6/Documentation/Manual/define-context-for-aop.html) informed the dockable window/global Scene-tool choice. No claim of original NFS source access or measured reference-game behavior is made.
