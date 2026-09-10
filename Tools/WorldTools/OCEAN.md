# Rockport ocean

`Assets/NfsMw/Scenes/World/RockportMap.unity` contains `Rockport Ocean - Original Coastline`. Its water geometry comes from the original `TRN_OCEAN_A` and `TRN_OCEAN_B` placements in `STREAML2RA.BUN`. Mean sea level remains Unity Y = 0. The decoding retains 608 nondegenerate source triangles and clips a 32 m grid to them, producing 112,199 vertices and 222,144 render triangles. It preserves the original shoreline, including holes; uncovered inland areas are not filled with water. The ocean covers approximately 92.47 km².

The original material is replaced by Unity HDRP 17.6 `WaterSurface`: spectral large waves, ripples, foam, refraction and underwater rendering. All five project HDRP profiles have water enabled, with CPU script interaction at resolution 128; camera and reflection default frame settings enable water. The custom MeshRenderer is disabled for ordinary rendering because HDRP submits the configured water geometry itself.

## Physics

`RockportOcean` queries the actual HDRP CPU wave simulation after checking the original polygon footprint. `OceanBuoyantBody` divides a local body bound into eight volume samples. Each FixedUpdate applies Archimedes buoyancy at the samples using seawater density 1,025 kg/m³, plus quadratic drag relative to the authored 0.2 m/s current. Forces and torque go through the Rigidbody and PhysX; there is no position snapping to an animated plane.

Dynamic rigidbodies entering the ocean trigger are enrolled automatically. Known shapes should call `Configure` with their local bounds and displaced volume in cubic metres. Automatic enrollment estimates effective volume as 65% of the collider bounds. Wheel colliders, triggers, and colliders owned by another body are excluded. The 481 breakable trees have authored cylinder-volume estimates; intact kinematic trees receive no forces. Dry positions outside the original water polygons receive no buoyancy or drag even inside the broad trigger bounds.

This is a game ocean model, not a full fluid solver. It does not simulate coastal breaking waves, fluid pressure on detailed hull meshes, flooding, spray forces, or the water's response to submerged objects. The source polygons are exact at rest; HDRP waves displace the rendered boundary. The underwater volume is a broad bounding box and is intended for cameras over the ocean footprint.

## Verification and reproduction

Run from the project root:

```bash
python3 Tools/WorldTools/prepare_ocean.py
python3 Tools/WorldTools/verify_ocean.py
```

With RockportMap open in Unity, run `configure_ocean_unity.cs` once through the configured relay. It refuses to create a duplicate ocean root. Run `run_ocean_smoke_unity.cs` to start temporary Play Mode fixtures; the test stops Play Mode after recording the result, restoring the edit scene. It checks changing HDRP wave heights, automatic Rigidbody enrollment, floating, sinking, lateral drag, no forces on dry land, vehicle-collision tree knockdown, and tree reset. `verify_terrain_trees_unity.cs` checks every native and physical tree placement. `run_ocean_preview_unity.cs` captures the animated water in Play Mode with temporary lighting and cameras, then returns to edit mode. `render_ocean_map_unity.cs` is a faster still-render alternative.

Evidence is stored in `Art/RockportOcean/Source`: `ocean-source-verification.json` records archive hash, source placement identities, counts, area and bounds; `ocean-mesh-verification.json` independently checks the binary mesh, triangle area, edges and sampled source containment; `ocean-unity-setup.json` records saved Unity configuration; `ocean-playmode-verification.json` records runtime results. Preview images are under `Art/RockportOcean`.

The 20-second Play Mode run after the independent tree replacement passed: 1,582 successful fixed-position wave samples spanned 0.377 m in height. The lighter cube remained floating at Y = −0.438 m while the denser cube sank to Y = −100.560 m. Automatic enrollment, lateral drag, dry-land exclusion, vehicle collision knockdown and tree reset all passed. These fixtures establish the mechanics; they are not a full vehicle water-handling or performance benchmark.
