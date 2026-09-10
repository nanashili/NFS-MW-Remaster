# Race Route Editor

Open **NFS MW Remaster → Racing → Race Route Editor**. The window can dock beside Scene view; enable **Race route authoring** in Scene view's Overlays menu for picking, validation and test cancellation.

## Author a route

1. In **Routes**, create an asset and assign a published `RoadNetworkAsset`. Refresh the searchable route library to find existing sources. Duplicating a route assigns fresh owned IDs and removes publication/line bindings.
2. In **Network map**, choose the start and finish lanes and stations. Use the Scene picking tool or the map; the height layer helps separate flyovers. Lane IDs and station fields are the precise fallback for overlapping screen projections. Picking selects existing samples; it never invents road connectivity.
3. **Suggest connected sector** follows directed successor edges, respecting width, grade, surface, class and turn restrictions. Choose distance or posted-speed travel time as cost. Add intermediate sectors to enforce via locations. A failed/cancelled search leaves authored traversal unchanged.
4. In **Traversal**, inspect lane occurrences and sector order. Repeating a lane is legal when its distinct occurrences connect. Reordering never reverses a one-way lane automatically.
5. In **Gates & branches**, set spacing/height per sector. Duplicate a main path to author an alternative, then edit its span list. Alternatives must share first and last road-relative anchors. Select sector/path indices to move Scene endpoint arrows; release commits one Undo action. Moving one alternative's shared anchor requires updating its peers before validation passes.
6. In **Start grid**, enter the largest eligible entrant dimensions, row count and spacing. Choose single-file or side-by-side. Leave sufficient lane before the start and runoff after the finish. The loaded-scene check inspects current colliders; unloaded scenery is not verified.
7. In **Event policy**, configure the existing Sprint, Circuit, Drag or Speedtrap policy. Only Circuit allows multiple laps. Drag currently uses ordered traversal; dedicated staging/shift rules are not implemented here. Drift scoring, ghosts and spontaneous challenge approval require their respective runtime policy owners.
8. Validate, inspect issues in **Reports**, then **Publish** a new immutable asset. Previous revisions remain for Undo and existing consumers. Source changes do not silently mutate placed event instances: explicitly place/rebind the desired publication.
9. **Place event entry** creates a zero-reward `FreeRoamEventDefinition` with the publication binding. Add that definition to the existing `FreeRoamSession` event catalog through the normal event-placement workflow. Career eligibility/rewards remain owned by the event/mission/settlement systems.

## Progress and recovery

Each sector contains one or more ordered gate paths. The first distinct crossed gate selects a branch; common-prefix gates do not choose it prematurely. Every gate on that branch must be crossed in sequence, in the forward direction and inside its full 3D rectangle. A sweep can cross several gates/sectors between physics samples. A flyover outside the gate's height cannot count. Wrong-way crossings and skipped required gates do not advance progress.

`MissionCourse.LegalProgress` exposes completed sectors plus normalized distance through the selected branch. This is a legal-progress coordinate, not a claim of equal travel time between alternative routes or a replacement opponent-ranking system. Gate identity includes lane occurrence; mission checkpoint identity includes lap. Mission snapshots preserve branch/gate state and reject a different publication revision. No asynchronous gate-trigger subscription is installed; existing mission attempts retain their own identity and settlement transaction.

A missed gate remains required. The tool adds no automatic reset teleport, wrong-way penalty, recovery grant or scoring equation. Restart follows existing race lifecycle and checks the published grid using entrant dimensions. If all slots are occupied it refuses the restart. Finish transition remains with the existing event flow.

## Test and inspect

**Geometry replay** exercises gate passages through `MissionRuntime` without creating a vehicle or loading a profile. It does not establish driveability.

For a physical test, export the main traversal in **Dependencies**, open the resulting route in Racing Line Studio, assign/calibrate its vehicle and capability data, generate and verify a line, then link the source back here. The export unrolls the requested laps and includes a finish continuation so the follower crosses the final gate before stopping. The current test executes the main branch. Export alternatives separately for Racing Line Studio testing; they are not all automatically qualified by one main-path result.

**Run isolated vehicle / mission test** uses production `RacingVehicleRig`, its controller/follower, fixed-step physics and `MissionRuntime`. It owns a disposable local physics scene and no career/profile/wallet. Stop, closing the window, scene changes, script reload and Play entry dispose it. Changed route or line dependencies cancel a stale run. A missing/incompatible line is reported as geometry-only readiness.

The Scene trace is a measured speed overlay (blue to red, 0–150 km/h). Export JSON for per-step positions, speed, completed sectors, trial outcomes, revisions and hardware. This trace does not infer crash, passing, police or drift-failure heatmaps. The linked line's verification settings own trials, seed and duration. Loaded traffic/police components can be selected for read-only inspection; those systems are not populated or simulated by the route preview.

## Samples and troubleshooting

`Examples/RaceRoutes/RouteAuthoring.unity` is a synthetic straight-road demonstration with an event entry and a qualified production vehicle line. Select `GrayboxSprint.asset` in the window to replay or test it. The scene is an authoring fixture; it has no career session, so pressing Play alone does not start a career race. It is not decoded Rockport geometry or a measured NFS handling reference.

- **Unresolved lane:** restore/remap the exact lane reference through the Road Editor. There is no nearest-lane fallback or unproven road-split migration.
- **Connection/loop/rejoin:** inspect successive occurrence endpoints and authored successor edges. Coincident geometry alone is insufficient.
- **Grid:** move the start forward, reduce eligible entries or select a wider corridor; do not shrink dimensions below the allowed vehicles.
- **Dirty publication:** validate and publish again. Failed generation leaves the prior binding intact.
- **AI mismatch:** export the current main traversal including all laps/runoff and regenerate the linked line. Its own conservative road-publication invalidation may require requalification after an upstream publication change.
- **Unknown schema:** schema 1 is supported; unknown versions are rejected, not silently rewritten. No earlier race-route schema exists to migrate.

Automated evidence and rerun instructions: `Tools/RaceRouteValidation/RESULTS.md` and `run.sh` at the project root. Interactive handle/overlay gestures still warrant an Editor smoke check on the target workstation; batch tests cannot prove pointer ergonomics.
