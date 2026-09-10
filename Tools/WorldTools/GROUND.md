# Rockport roads and terrain

Open `Assets/NfsMw/Scenes/World/RockportMap.unity`. This combines the original building assembly with paved streets, highways, parking areas, sidewalks, dirt paths, grass/off-road routes, sewer ground, bridges, tunnels, ramps and road barriers in their original world positions. Race-only invisible blockers, vehicles and loose props are excluded. Native trees were added separately as described below. The earlier `RockportBuildings.unity` scene and Blender building file remain separate.

The combined scene has seven roots:

| Root | Contents |
| --- | --- |
| Rockport Buildings - Original Game Layout | 5,632 building instances, 966,506 triangles |
| Rockport Roads - Paved and Unpaved Routes | 11,764 road/surface/structure instances, 920,152 triangles |
| Rockport Original Ground - Exact Source Meshes | 3,334 natural-ground instances, 213,889 triangles; disabled for painted terrain |
| Rockport Generated Terrain - 1m Heightmap | 39 painted, editable Unity Terrain tiles; enabled |
| Rockport Trees - Terrain Openings | Invisible native Terrain supports for original trees over ground holes; no ground rendering or collision |
| Rockport Breakable Trees | 481 original campus tree placements with vehicle-impact knockdown and buoyancy |
| Rockport Ocean - Original Coastline | Original ocean footprint and sea level, with HDRP waves and Rigidbody buoyancy |

Native Terrain trees now reproduce the original tree placements; see [TREES.md](TREES.md) for source counts, transform precision and editing details.

The separate ocean layer uses the original water polygons; see [OCEAN.md](OCEAN.md) for wave simulation, physics and validation.

The source roads and natural ground have static MeshColliders derived from their visible geometry. This provides collision surfaces for later driving integration; it does not reconstruct the original game's collision-specific geometry or handling. No player spawn, scene streaming, Road Editor splines, AI navigation, or build-settings changes are included.

## Using the heightmaps

`Art/RockportGround/RockportHeightmap.png` is the full **16-bit, 7169 × 8193** heightmap. `RockportCoverage.png` marks valid surface samples; black outside coverage means **no source surface**, not sea level. `heightmap-preview.png` is only an 8-bit reduced preview.

For Unity, use the existing TerrainData assets or the 39 tiles in `Assets/NfsMw/Content/World/Maps/Rockport/Heightmaps`. Each tile is **1025 × 1025 samples**, covers **1024 × 1024 metres**, and shares identical border samples with its neighbors. `metadata.json` gives the original coordinate origin and every tile's location.

- Horizontal spacing: 1 metre.
- Combined grid origin: Unity X **-5120**, Z **-6144**.
- Height offset: **-31 metres**; normalized height range: **489 metres**.
- Decode height: `worldY = -31 + (sample / 65535) * 489`.
- RAW: unsigned 16-bit, little-endian; row zero is minimum Unity Z.
- PNG: row zero is maximum Unity Z. Do not treat PNG row order as RAW row order.
- Source coordinate conversion: game `(X,Y,Z)` becomes Unity `(-X,Z,-Y)`.
- `.coverage`: one byte per sample, 255 for source ground or road coverage.
- `.terraincoverage`: one byte per sample, 255 for natural-ground coverage only.

The saved scene enables **Rockport Generated Terrain - 1m Heightmap** and disables **Rockport Original Ground - Exact Source Meshes**. Keep the road root enabled. Terrain cells require four natural-ground sample corners; road-only and unobserved cells remain holes so the original road meshes supply those surfaces. Toggle these two ground roots to compare the painted heightfield with the exact source meshes.

## Terrain painting

All 39 TerrainData assets have eight editable layers using original grass, dark grass, golf rough, fairway, rock, cliff, sand and dirt textures. The 512 × 512 alphamaps sample original natural-ground material IDs at two-metre pixel centres, selecting the highest triangle consistently with the heightfield. The explicit mapping from 41 source materials to eight representative textures is recorded in `Assets/NfsMw/Content/World/Maps/Rockport/PaintMaps/metadata.json`. This approximates the original transition textures and UV layouts; it is not a pixel-identical reconstruction of the original ground shader.

Texture repetition scales come from source triangle world/UV areas and are snapped to whole repeats across each 1024-metre tile to avoid texture-phase seams. Every painting batch compared all height samples and holes before and after: unchanged. Unity validation read all 10,223,616 paint samples, confirmed normalized weights and zero mismatch against the generated maps, and checked all eight texture references on every tile.

`Art/RockportGround/unity-painted-terrain-downtown.png` and `unity-painted-terrain-overview.png` are actual HDRP renders with the painted native Terrain enabled and original ground meshes disabled. Buildings and roads remain visible. Preview lighting is temporary.

To reproduce painting, run `python3 Tools/WorldTools/paint_terrain.py`, set `Art/RockportGround/Source/paint-next.txt` to `0`, then run `paint_terrain_unity.cs` through `run_unity.py` seven times. Finally run `verify_paint_unity.cs` and `render_painted_terrain_unity.cs` through the same Unity MCP helper. The source raster and editor verification reports are in `Art/RockportGround/Source/paint-raster-verification.json` and `paint-unity-verification.json`.

## What “exact” means here

The retained source mesh triangles and packed original placements are preserved, including grade changes, elevated highways, tunnel geometry and non-heightfield cliffs. The heightmaps are **derived from these triangles**, not an original heightmap file.

A Unity heightfield holds one elevation at each X/Z coordinate. It cannot exactly reproduce bridges over roads, tunnels beneath ground, vertical walls, overhangs, or arbitrary triangle boundaries. The raster keeps the highest natural-ground intersection; when none exists, it uses the lowest road-surface intersection. It does not invent missing land or interpolate across unmapped areas. Distant panorama/backdrop objects and water are excluded.

The multilevel mask flags source samples whose intersections span more than 0.5 metres. There are 545,417 such samples in this decoding. Keep the source meshes as the geometry reference where the heightfield loses information. One-metre horizontal sampling can introduce shape error near sharp features, independent of vertical encoding precision.

Measured encoding precision:

- Exported 16-bit sample error: at most **3.731 mm** relative to the sampled float heights.
- Unity Terrain import adds at most **7.462 mm** relative to the exported normalized samples.
- Neighboring exported tile borders are bit-identical. Unity Terrain neighbor links were checked.

## Source and validation

The decoding reads the installed game's `TRACKS/L2RA.BUN` and `TRACKS/STREAML2RA.BUN` using the existing complete map inventory. Archive hashes were checked before/after reading. Rotation, scale and mirror are baked into vertices using the original packed transform; translations are unchanged. Source and Unity counts/bounds match after coordinate conversion.

`ground_classification.py` makes a recorded decision for every source material. Its selection uses object, material and original diffuse texture names; the full city was visually inspected in Unity, but this is not a manual certification of every polygon or an in-game drive through every shortcut. Grass-covered drivable areas belong to the natural-ground layer; named dirt/gravel paths belong to the road layer.

One fish-market ramp diffuse texture (`61a56b42`, `FISHMARKETRAMPB_CHOP_S17_R0`) was unavailable in the inventoried texture packs. Its geometry remains with a named neutral material. The road and natural-ground layers have no unresolved retained solids, invalid retained placements or fallback LODs. The earlier building layer retains its separately documented texture/LOD exceptions.

The original game's shader lighting, animated layers and specular effects are not recreated. The glTF materials retain original diffuse textures and applicable punch-through alpha for HDRP relighting.

Evidence is in `Art/RockportGround/Source`:

- `roads-decoding.json` and `terrain-decoding.json`: source placements, material decisions, hashes and exceptions.
- `ground-verification.json`: finite/bounded glTF data, layout, independent barycentric sampling, RAW precision and every tile seam/coverage sample.
- `unity-ground-validation.json`: actual Unity mesh, triangle, material and bounds measurements.
- `unity-terrain-validation.json`: all 40,974,375 Unity height samples, hole masks and neighbor checks.
- `unity-collision-validation.json`: all 15,098 source collision components and 500 successful road/ground raycasts.
- `heightmap.f32`: full world-height grid, little-endian float32, minimum-Z row first; NaN denotes no source sample.
- `heightmap.coverage`: full grid, 0 absent / 1 natural / 2 road-only.
- `heightmap.multilevel`: full grid, 1 where multiple source elevations differ by more than 0.5 metres.

`unity-downtown.png` and `unity-overview.png` are renders of the combined scene through Unity MCP with temporary preview lighting that is removed afterward.

During initial collision setup, Unity reported an Undo-stack overflow after 8,000 colliders. Per-component Undo snapshots were then removed from the batch command. All 15,098 saved collision components were checked afterward; 500 sampled raycasts passed. Unity's earlier Undo history may have been truncated. The separate building scene was preserved.

## Reproduction

From the project root, using the already-created full source inventories:

```bash
python3 Tools/WorldTools/export_map.py roads
python3 Tools/WorldTools/export_map.py terrain
python3 Tools/WorldTools/heightmap.py
python3 Tools/WorldTools/verify_ground.py
```

The height rasterizer builds natively with `clang++`; its synthetic checks and independent source-triangle checks do not need Unity. The scripts reuse the original verified game texture/geometry readers.

Run `import_ground_unity.cs` through Unity MCP from a clean, open building scene to create the combined scene. It refuses to overwrite an existing combined scene. Run `create_terrain_unity.cs` seven times to generate its 39 tiles in bounded batches. Run `ground_colliders_unity.cs` four times for the 15,098 road/ground colliders. Then run `validate_ground_unity.cs`, `verify_unity_collision.cs`, and `render_ground_unity.cs`. Finish with `python3 Tools/WorldTools/finalize_ground_verification.py` to check the Unity results, original placement inventory and every full-image/tile row. `run_unity.py <script.cs>` submits those editor commands through the configured local Unity MCP relay. These tools live outside Assets and add no gameplay scripts.
