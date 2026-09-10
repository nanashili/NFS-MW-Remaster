# World / City Builder

2026-09-05. Implementation follows the supplied city-builder product brief; game-reference wording is a design requirement, not a claim about proprietary game implementations.

## Repository audit and decisions

Unity 6000.6.0f1, HDRP 17.6.0, Splines 2.9.0, Test Framework 1.8.0. Existing Runtime/Editor/test assemblies and UI Toolkit road window remain. `RoadNetworkAsset` owns published lanes and actual band meshes. `RoadAuthoringCommands` remains the only road creation path. `WorldLocation` owns existing gameplay locations; the city publishes optional spatial descriptions without spawning missions, traffic, rewards or save state. `RockportWorldStreamer` owns additive world-cell residency; Traffic's spatial registry remains agent population infrastructure rather than a second world loader.

The current Rockport free-roam source was partitioned along its 39 authored
Terrain tiles. `Assets/NfsMw/Scenes/World/RockportMap.unity` owns global and gameplay
objects; `Assets/NfsMw/Scenes/World/Streaming/Cell_xNN_zNN.unity` owns spatial map
objects. The superseded numbered map source, chunk scenes and assembly example
were removed after dependency validation. Procedural districts complement the
authored city and do not reconstruct or replace it.

City source is a scene district with city-local polygons, approved blocks/parcels, style/kit references, explicit lane anchors and generator overrides. City publication is an immutable versioned asset with location footprints, road revision and cell membership. Meshes and prefabs remain scene output; runtime queries do not require their renderers to be active. Coordinates are metres, angles degrees; district transforms translate only. Copies made through city commands get fresh IDs. Generic duplicated IDs are validation errors.

The implementation is divided into geometry, deterministic planning, scene commands/transaction, publication/validation and Editor workspace modules. UI calls the same commands tested by automation. No gameplay logic lives in the window. Generation plans capture input fingerprints; stale plans are rejected. Stable keys are parcel ID plus generator slot, independent of enumeration order. Scene changes use a single Undo group and prefab override recording. New publication assets are retained for Undo; only an unsuccessful transaction's own asset is deleted.

## Geometry research

[Clipper2](https://www.angusj.com/clipper2/Docs/Overview.htm) provides polygon Boolean, offset and triangulation operations. Its C# implementation was selected at version 2.0.1 with Boost 1.0 license, approximately 275 KiB, Editor-only. Millimetre quantization happens after world-to-district conversion. Concave outlines and holes are represented explicitly. Invalid/self-crossing source rings are rejected before clipping. [CGAL arrangements](https://doc.cgal.org/latest/Arrangement_on_surface_2/index.html) explain general planar subdivisions, but a native C++ dependency is unnecessary here: subtracting published surface-band footprints from a district gives reviewable bounded faces, preserving actual widths and sidewalks. Faces touching the district border are excluded as unbounded candidates. Open-road layouts can therefore report no enclosed block. Grade is filtered before projection; geometric faces never create road links.

Setbacks use polygon erosion; manual cuts use half-plane clipping; parcel merges require one connected result. A rectangular modular structure must fit fully within the eroded parcel, including holes and road reservations; otherwise generation reports an unsatisfiable envelope. Prefab kits are preferred for authored art. The included industrial kit is a labelled synthetic modular exterior fixture, not finished environment art.

## Editor and ownership

Dockable Districts, Blocks, Parcels, Structures, Dressing, Streaming, Preview and Validation views. Scene handles edit polygons and access envelopes. Preview is temporary and collider-free; cancellation, window closure, assembly reload, scene closure and Play transitions dispose it. Opening the window is read-only. Generated, overridden, pinned and detached are explicit ownership states. Detachment transfers ownership and suppresses regeneration of that key. Pins retain transforms; invalid anchors remain errors requiring deliberate remapping.

[Unity 6.6 Undo](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Undo.html) handles object changes, not arbitrary filesystem transactions. [Prefab overrides](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/PrefabUtility.RecordPrefabInstancePropertyModifications.html) are recorded after source edits. UI construction follows Unity's [Editor-window lifecycle](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-HowTo-CreateEditorWindow.html); compile and lifecycle tests target the installed editor.

## Dependency order and verification

1. Source, polygons, IDs and serialization; invalid/concave/holed geometry fixtures.
2. Surface block candidates, parcel cut/merge/subdivision, explicit frontage/access diagnostics.
3. Industrial kit, deterministic plans, constraints, collision and semantic locations.
4. Reviewable create/update/delete/preserve diff, pins/detach/overrides, stale/cancelled commit and Undo tests.
5. Parking/dressing/reservations and explicit road-command proposals; terrain grading preview without destructive terrain ownership.
6. Cell membership and budgets, fixed traversal capture; integration with the separately owned `RockportWorldStreamer` rather than a second loader.
7. Immutable semantic publication and build validation.
8. Synthetic mixed district, failure/reload/lifecycle tests, measured reports and usage documentation.

Resource limits are authoring guards, not performance claims. Runtime streaming
is integrated, while terrain composition and finished modular art remain
separate content concerns. Player memory, loading cadence and frame timing still
require Development Player profiling on target hardware.
