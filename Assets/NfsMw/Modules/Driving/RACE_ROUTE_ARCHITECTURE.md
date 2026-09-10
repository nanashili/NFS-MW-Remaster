# Race route ownership and implementation

Target: installed Unity 6000.6.0f1, URP 17.6, Splines 2.9 and Test Framework 1.8. No package upgrades or parallel road graph were introduced.

## Existing owners and adapters

| Concern | Owner | Route integration |
| --- | --- | --- |
| Directed topology, lane IDs, 3D samples, surfaces | `RoadNetworkAsset` / Road Editor | Source stores lane IDs and station intervals; search reads successor edges |
| Route legality and immutable gate geometry | `RaceRouteDefinition`, compiler, publication | Stable sector/path/occurrence IDs; no copied lane graph in authoring data |
| Attempt, laps, deadlines, outcomes, save identity | `MissionRuntime` via `MissionCourse` | Ordered 3D passages become normal checkpoint facts; branch/gate facts persist |
| Race start, pause, finish and restart | `FreeRoamSession` | Existing event definition binds publication; safe-start checks published grid envelopes |
| Vehicle/AI qualification | Racing Line Studio | Export traversal, unroll laps and append runoff; reuse isolated production rollout |
| Rewards, career and persistence | Existing mission settlement and career profile | Preview cash is zero and no real profile is created |
| Traffic and pursuit | Existing traffic/police components | Read-only inspection; no new tactics, spawn policy or population simulation |

Most Wanted (2005) remains the world/career/frontend reference; NFS (2015) remains the handling/opponent/pursuit reference. The synthetic fixtures do not claim measured parity with either game.

## Geometry and revisions

A sector owns ordered paths; each path owns directed lane occurrences. Branches share start/rejoin anchors and require all selected internal gates. Gate planes use road tangent/up vectors and real width/height. Segment-plane intersections are processed monotonically within a physics step, and mission facts are numerically padded before the existing deterministic event sorter consumes them. This prevents multi-sector high-speed crossings from reordering sector 10 ahead of sector 2.

The source fingerprint includes schema, network identity, authoring constraints and only referenced lanes' sampled data/successors. An unrelated lane revision within the same network does not change it. A missing or split lane remains unresolved until explicitly remapped. The immutable publication copies gates/grid poses and records road/source revisions. Editor publication rebuilds and rechecks the reviewed fingerprint before changing the source binding; failed outputs do not replace it. Existing published event bindings remain pinned. Mission restore includes the publication fingerprint in the mission definition hash.

This is validation against an explicit published road asset. It does not inspect unsaved upstream road edits or automatically switch to a newer road asset. Racing Line Studio still applies its existing conservative dependency hashing; this tool does not weaken physical mesh/collision qualification to achieve narrower line invalidation.

## Editor decisions

The window uses UI Toolkit controls, a virtualized catalog, IMGUI/Handles map rendering, an `EditorTool` for Scene picking and a Scene overlay. The source asset is the shared selection. Gates and endpoint arrows use the same resolved lane geometry as compilation. Drag previews remain transient until release. Expensive validation is explicit; the preview's existing rollout slices retain dependency checks.

Graph Toolkit was evaluated as a node-graph framework. This task's primary operations are spatial lane selection, route ordering and physical gate placement; adding a second graph editor/package would duplicate the road graph's presentation and introduce unnecessary dependencies. Standard Editor windows, tools, overlays and handles serve those operations directly.

Official references inspected for the installed Editor generation:

- [Editor extensions](https://docs.unity3d.com/6000.6/Documentation/Manual/extending-the-editor.html)
- [Graph Toolkit](https://docs.unity3d.com/6000.6/Documentation/Manual/gtk/gtk-index.html)
- [Workspace customization](https://docs.unity3d.com/6000.6/Documentation/Manual/CustomizingYourWorkspace.html)
- [Custom Editor tools](https://docs.unity3d.com/6000.6/Documentation/Manual/UsingCustomEditorTools.html)
- [Custom overlays](https://docs.unity3d.com/6000.6/Documentation/Manual/overlays-custom.html)
- [Gizmos and handles](https://docs.unity3d.com/6000.6/Documentation/Manual/gizmos-handles-programming.html)
- [Authoring context](https://docs.unity3d.com/6000.6/Documentation/Manual/define-context-for-aop.html)

## Limits and verification scope

Compilation allows 256 sectors, eight alternatives per sector, 1,024 spans per path and 20,000 gates total. Search cancels cooperatively and stops after 10,000 visited lanes. These are safety budgets, not measured city-scale latency promises. Suggestions use a bounded Dijkstra search over nonnegative distance/travel-time costs; they do not solve target-distance or scenic-quality optimization.

Automated cases cover flyovers, reverse/duplicate sweeps, different-length branches, lap occurrences, missing lane IDs, revision restore, stale publication, source Undo/Redo, placed-event Undo/Redo, repeated preview cleanup and source serialization. The sample qualifies the main straight route with actual vehicle physics and mission evaluation. Multi-entrant racing, traffic/police encounters, drift scoring, ghosts, sector painting, automatic recovery and statistical reliability heatmaps require their existing owners' additional adapters. They are not represented as completed capabilities in the UI.
