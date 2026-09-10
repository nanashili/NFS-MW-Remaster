# World / City Builder

Open **NFS MW Remaster → World → City Builder**, or **Window → NFS MW Remaster → World & City Builder**. The dockable UI contains Rockport Map, Districts, Blocks, Parcels, Structures, Dressing, Streaming, Preview and Validation workspaces.

## Rockport free-roam map

`Assets/NfsMw/Scenes/World/RockportMap.unity` is the sole free-roam entry scene. It owns the
gameplay services, player, camera, lighting and global ocean. The spatial world
lives in 39 terrain-aligned additive scenes under
`Assets/NfsMw/Scenes/World/Streaming/`; each cell owns its local terrain, road
renderers and colliders, buildings, tree terrain, breakable trees and
destruction registry.

The **Rockport Map** workspace opens the entry scene, runs the migration when an
unmigrated checkout still has the authoring source, and validates the current
entry/cell manifest. Run validation after changing cells, Build Profiles or
map textures. It checks all 39 scene paths, build inclusion, missing scripts,
legacy dependencies and renderer texture mip-streaming enrollment.

`RockportWorldStreamer` chooses cells around the current and predicted player
position. Current settings are a 384 m load radius, 768 m unload radius, six
resident cells, 2.5 seconds of velocity lookahead capped at 300 m, and an
8-second coalescing delay before unused assets are reclaimed. Recovery and event
relocations preload the destination and keep the Rigidbody kinematic until at
least one destination cell is resident.

The migration moved 5,632 building objects, 11,764 road objects, 39 terrain
tiles, 31 tree terrains and 481 breakable trees into those cells. It enrolled
982 renderer-bound map textures in Unity mip streaming. Terrain-layer textures
remain governed by Unity Terrain and the quality mip limit because Unity's
texture streaming system does not support Terrain textures.

### Migration and removal

The migration stores an external pre-partition scene backup and reports under
`Artifacts/RockportStreaming/`, updates `GameFlowSettings` and Build Profiles,
then verifies retained scenes before removing the superseded free-roam scene,
old chunk scenes, old city-builder example and old city-model folder. The small
weather-demo circuit formerly nested under the old folder is retained at
`Assets/NfsMw/Content/World/Models/WeatherDemoCircuit/` with the same GUID.

The retained Rockport building, road, terrain and replacement-tree asset folders
are dependencies of the new cell scenes. Their authored geometry and transforms
remain unchanged; the existing Road Editor lane topology remains a separate
gameplay contract.

## Author districts and parcels

Create starter styles in **Districts**, choose one, and create a district. Alternatively create a boundary around an assembled section. Districts use metres in local X/Z, local height in Y, a translation-only root, stable identities and deterministic seeds.

Assign an existing published Road Network to derive closed surface-level block candidates. Preview and approve candidates in **Blocks**. Open road networks do not fabricate closed blocks. Roads above the selected surface tolerance do not divide the surface. Manual rectangular blocks are also supported.

Subdivide approved blocks, create a single parcel, or select a parcel and use scene vertex handles. Arbitrary line cuts and adjacent parcel merges are undoable. Locked boundaries and anchored overrides prevent incompatible splits/merges. Splits create new parcel identities; merges preserve the first parcel identity and retire the second. Remap external references deliberately after those operations.

Use **Parcels** to choose land use, kit, floor count, setback, coverage, height and entrance settings. Concave boundaries and holes participate in clipping and setbacks. Unsatisfiable envelopes produce diagnostics instead of fabricated geometry.

Entrances require an explicit published lane reference and public-access approval. The checker evaluates the selected anchor, width/height margins, approach distance and slope. It is not a swept vehicle turning or navigation simulation. A service-road proposal creates editable Road Editor source through its command API; bake/connect/review it there before updating the district's road publication.

## Generate and preserve local art

**Structures** edits kit dimensions, roof family, shared materials, authored exteriors and procedural exterior settings. **Dressing** edits weighted prefabs, yard/park placement and protected reservations. Supplied styles share a simple starter kit intended to be replaced or customized with the existing city art.

Use **Preview → Generate reversible preview** to inspect deterministic changes. Temporary previews have no collision and use bounds for arbitrary prefabs. Review Create/Update/Delete/Preserve lists and diagnostics, then commit an immutable publication asset. Publication, runtime location reference and generated objects share one scene Undo transaction. Previous publication assets remain available for Undo; they are not automatically deleted.

Generated instances have four ownership states:

- **Generated:** updated by the generator. Unrecorded transform edits block regeneration.
- **Overridden / Pinned:** preserves the existing object. Capture the state after editing; missing objects or retired generator anchors require explicit repair/release.
- **Detached:** becomes manual scene content and leaves a suppression record so regeneration does not duplicate it. Releasing that record allows the slot to generate again.

Manual siblings remain untouched. Remove or detach manually added children before replacing primitive generated objects. Custom behavior should be placed on authored prefabs or detached content.

## Terrain, cells and runtime integration

Terrain grading is a disposable preview on a cloned Unity TerrainData, with cut/fill estimates and road/reservation precedence. Closing the tool, cancelling or entering Play removes the preview and restores visibility. There is no permanent terrain composition owner in this project, so grading does not overwrite the source terrain.

**Streaming** displays procedural cell coverage and generation budgets. It
publishes stable parcel/location metadata through `CityPublication` and
`CityLocations`. Runtime Rockport loading is owned separately by
`RockportWorldStreamer`; the procedural city tooling does not create a second
loader or floating-origin system. Rockport geometry remains outside procedural
parcel budgets.

Validation blocks unsupported source schemas, invalid polygons/identities, stale roads/publications, missing generated output and unresolved ownership records. Builds validate authored districts before stripping their editor source component. Use Validation before Play; build validation does not prove unloaded scenes or physical vehicle paths.

## Reproduce validation

Run `Tools/CityValidation/validate.sh`. It copies the project to a fresh temporary directory, runs City EditMode tests, then validates the Rockport entry scene and its 39-cell manifest. It never runs batch Unity against the live project. Set `UNITY_EDITOR` if the installed editor path differs; licensed unattended environments can pass `UNITY_LICENSE_IPC` externally.

See `Tools/CityValidation/RESULTS.md` for the measured checks from this implementation. The polygon implementation vendors Clipper2 2.0.1 under the Boost Software License; source and license are under `Editor/City/Vendor/Clipper2`.
