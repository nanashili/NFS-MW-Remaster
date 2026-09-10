# Road authoring implementation

Implementation checkpoint: 2026-09-05. Unity **6000.6.0f1**, Splines **2.9.0**, existing URP pipeline and vehicle physics.

This is the first working authoring slice from the [architecture](ROAD_AUTHORING_ARCHITECTURE.md). It implements road creation, cross-section geometry, publication, and directed runtime queries. The complete twenty-milestone transportation platform remains in development; intersections, terrain editing, and the full gameplay migration are not delivered by this checkpoint.

## Use the editor

1. Open **NFS MW Remaster → Roads → Road Network Editor**.
2. Choose **Two lane preset** or **Local street with curbs and sidewalks**. Profiles are ordinary assets under `Assets/NfsMw/Modules/Driving/Data/Roads`; configure ordered bands, materials, surfaces, direction, width, height, crossfall, and shape in their inspector.
3. Select **Draw road**, click points in the Scene view, then press Enter or double-click to commit. Escape cancels. Surface snapping and Ctrl grid snapping are available. Drawn knots use Auto Smooth; Unity's native Spline tools edit tangent modes and handles. The road inspector also moves knots and inserts a shape-preserving midpoint.
4. Select roads and choose **Create network from selected roads**. Set the window's Network field to add subsequent roads directly to that network. **Add selected roads to network** adopts unassigned roads from the same scene.
5. Use **Connect matching lane ends** to add explicit directed continuations. Ends must meet within 5 cm, with compatible tangent and width. Connections can be removed in the network inspector. Crossing centerlines do not create connections. Author a separate transition road for a bend between approaches; there is no junction surface generator yet.
6. Select **Bake selected network**, choose an asset location, then save the scene. Each bake creates a new immutable asset revision and replaces owned scene geometry as one Undo group. Undo/Redo restores both geometry and the runtime publication. Earlier asset revisions remain available; automatic revision garbage collection is not implemented.

Preview meshes are temporary and have no collision. While previewing, the corresponding baked chunks are hidden in the Scene view to avoid overlapping renderers; existing hidden state is retained and temporary visibility changes are restored when preview ends. Play and builds require a current bake with matching generated collision and the matching runtime service. Invalid data produces a `ROAD_*` diagnostic and cannot publish.

Do not use Unity's generic Duplicate command for source roads: copied persistent identities are rejected. Create another road with the tool. Full structural duplicate/split/merge commands, endpoint snapping and prefab workflow coverage belong to the remaining editor milestone.

## Examples

- `Assets/NfsMw/Modules/Driving/Examples/ValidatedRoadAuthoringDemo.unity`: a roughly 500 m curved hillside loop with explicit self-continuations, the existing player vehicle, camera and HUD.
- `Assets/NfsMw/Modules/Driving/Examples/RoadDrivingValidation.unity`: a 300 m straight road, starting the shared vehicle at station 90 m so it drives across the 100 m mesh-chunk boundary.

Use WASD/arrows, Space for handbrake, Shift for nitrous, C for camera and R to reset. These are geometry and vehicle fixtures, without traffic demand, opponents, signalized junctions, terrain, or scenery. Existing gameplay scenes have not been migrated. The original `RoadAuthoringDemo.unity`, if present from an earlier bake, is preserved and can be rebaked through its network inspector.

## Implemented contracts

| Area | Behavior |
| --- | --- |
| Source authority | One `SplineContainer` per road, reusable `RoadProfile`, road-instance band bindings and persistent GUID identities. Identity generation occurs in explicit creation/reconciliation commands. |
| Coordinates | Metres; reference XZ length is station, 3D length is travel distance; positive lateral is left looking forward. Spline Y owns elevation. Bank is a separate station curve. Unit scale is required through the hierarchy. |
| Geometry | Adaptive reference and cross-section sampling; planar station/3D distance mapping; chunk-local meshes; ordered driving/shoulder/curb/sidewalk and custom bands; profile shape heights, crossfall, per-band material and surface; variable positive width and bank curves; shared seam normals and station UVs. |
| Validation | Finite values, identities/ownership, profile reconciliation, curve extrema, reversing cusps, vertical/degenerate spans, local band folds, sample/chunk budgets and endpoint continuity. This does not establish comprehensive global self-intersection or civil-engineering clearance validation. |
| Publication | Schema-2 `RoadNetworkAsset` owns immutable lane records and mesh subassets. A canonical SHA-256 source fingerprint includes geometry, profile fields, asset identities, surface physics, generator revision and Unity version. Runtime consumes the published asset. |
| Identity | Road/lane GUIDs survive edits, save/reload and rebakes. Retained lanes keep compatibility integers; new integers advance monotonically from the previous revision. Old schema-1 demonstration bakes rebuild their compatibility table. A full migration/tombstone ledger is not yet implemented. |
| Runtime query | Stable-ID lookup, lane sampling/projection, nearest lane in 3D, directed partial-lane routes, explicit loops, closures and minimum-width constraints. Caller-owned route buffers provide bounded capacity and distinct failure results. Nearest queries are currently linear scans. |
| Existing consumers | `RoadNetwork` explicitly selects legacy or published data. Published lane geometry derives the traffic adapter and map samples; GPS-style point routes use the same directed publication. Traffic closure leases also affect those routes. Police lane-point sampling does not apply the old additional 3 m offset to published lane centers. |
| Build ownership | Build/play checks reject stale publications, orphan sources, missing/duplicated/moved/disabled generated chunks, changed collider/material/surface bindings and mismatched runtime authority. Build processing strips authoring and spline components from scene copies. |

The main runtime assembly has no Splines dependency. Authoring types have their own assembly; all mesh generation, Scene tools, asset creation and build processing live in the Editor assembly. Existing runtime road graphs remain only for explicitly unmigrated legacy networks.

## Verification and remaining gates

The reproducible [validation runner](../../Tools/RoadValidation/README.md) uses an isolated project. Road tests cover station versus travel distance, mesh dimensions/winding/seams, knot insertion and Undo, cusp rejection, profile surface collision, save/reload, stable rebakes and Undo/Redo, failed-publication recovery, build rejection of edited output, banked lane frames, explicit connections and grade separation, partial/directed/cyclic routes, shared closures, narrow-lane clearance and atomic asset initialization.

The isolated full EditMode run recorded **346 passed, 0 failed, 2 skipped**, including all **13 road tests**. The skips belong to the existing racing integration fixtures, which declined to change an edited scene. Final API modernization compiled in the subsequent scene preparation and player build. See the [verification record](../../Tools/RoadValidation/Evidence/README.md) for the exact scope and evidence.

Physical validation uses the actual `VehicleController` and `VehicleWheel` implementation. A rendered **macOS Arm64 Mono Development player** travelled **74.59 m** across a chunk boundary, with all four wheels grounded for **400/400 physics steps**, no surface mismatches and zero authoring components. The [JSON report](../../Tools/RoadValidation/Evidence/RoadDriving.json) and [capture](../../Tools/RoadValidation/Evidence/RoadDriving.png) preserve the result. The asphalt/concrete test uses physics raycasts; mixed-surface driving, driving over curbs and physical hill/bank tests remain open. Editor commands and scene persistence are automated; interactive Scene-tool/prefab/multi-scene usability still requires hands-on coverage.

| Milestones | Status at this checkpoint |
| --- | --- |
| M1 | Architecture and research delivered in the preceding checkpoint. Its repository audit is historical; concurrent racing work is not an absence claim about the current checkout. |
| M2 | Core drawing, native spline editing, identity, station evaluation, preview, save/reload and player compatibility implemented and tested at the command/fixture level. |
| M3–M4 | Initial cross-section implementation and immutable directed graph delivered. Full preset coverage, mixed-wheel tests, access policy and consumer migration remain open. |
| M5–M6 | Window/inspectors, basic Scene tools, Undo and positive width/bank curves delivered. Structural commands, endpoint snapping, lane sections, zero-width tapers, lane add/drop and lane-change windows remain open. |
| M7–M12 | Junctions, signals, semantic markings, decorators, terrain recovery and structure systems remain open. |
| M13–M17 | A shared-publication traffic/GPS adapter and police offset correction are present. Full traffic admission/control, racer/event/checkpoint, pursuit/hazard/site, map/streaming and off-road access integration remain open. |
| M18–M20 | Local chunking, bounded generation and build gates are present. Spatial indexing, incremental bake, jobs, complete validation, benchmarks, stress/hardening and the combined highway/junction/bridge/shortcut fixture remain open. |

Width curves require finite unweighted keys and clamped wrapping; zero or negative widths are rejected. Reference splines are open; closed routes use explicit endpoint connections. Junction turns require authoring a connecting road. Bake runs synchronously and retains old asset revisions. Runtime publication replacement while traffic simulations are live is not supported as a migration workflow. Compatibility is verified on the stated Unity/macOS target; Windows and other targets, large-city performance and production readiness have not been established.

No OpenDRIVE importer/exporter is implemented. The [source research](Research/ROAD_AUTHORING_SOURCE_NOTES.md) informs the design; the Unity spline source is not presented as an OpenDRIVE geometry implementation.
