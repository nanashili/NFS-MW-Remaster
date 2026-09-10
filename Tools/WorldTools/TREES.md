# Rockport trees

Trees are **temporarily disabled** in RockportMap and all 39 streaming cells as of 2026-09-10. All 70 Terrain components have tree drawing disabled, all 39 TerrainColliders have tree collisions disabled, the 31 tree-only support objects are inactive, and all 481 breakable-tree objects are inactive. Ground rendering and ground collision remain enabled. The source assets and placement data remain available for restoration.

`Art/RockportTrees/Disabled-20260910/removal-report.json` records the checked counts. Its `SceneBackups` directory contains the scenes before this change; restore only the relevant tree flags when re-enabling trees so later scene edits are preserved. `disable_rockport_trees_unity.cs` applies and verifies the temporary removal through Unity MCP.

The retained tree content uses newly modeled Unity trees, with no original game meshes or textures. The original data supplies planting locations, approximate sizes and tree-row layout only. The sections below describe that retained content, not currently active trees.

There are **9,427 authored placement records**: **8,946 native Terrain placements** and **481 physical breakable trees**. Some records represent an entire tree row or group. The replacement planting plan contains 46,556 individual modeled trees across those records; this is a derived planting count, not a claim about the original game's number of individual trees.

## Replacement assets

`Assets/NfsMw/Content/World/Maps/Rockport/Trees/Replacement` contains the independent mesh library, generated bark and foliage textures, HDRP materials and 2,004 placement-compatible prefabs. The mesh library has five families with four variants each: broadleaf, conifer, slender, breakable sapling and dead tree. Each has three actual mesh LODs. Models use tapered trunks, branches, twigs and alpha-cutout leaf or needle clusters; bark, leaf and needle textures are generated from scratch. No original vertices, UVs, normals or texture pixels are copied into the new models.

`RockportReplacementTrees.cs` generates these assets in Unity. The 60 shared meshes keep repeated tree geometry reusable across group prefabs. Native Terrain rendering retains instancing support. The generated models are static; wind deformation, seasonal transitions and camera-facing billboards are not implemented.

`prepare_replacement_trees.py` reads the archived source prototype topology only to derive planting markers. Trunk bases become tree markers. Original billboard rows become spaced markers along their lower edges. Size and species are estimated, and nearby markers are deduplicated. This preserves the authored placement layout while replacing the visual geometry. It does not claim identical individual silhouettes or an exact one-to-one reconstruction of every tree inside a source billboard group.

## Terrain placements

The existing 39 painted terrain tiles remain in use. Original placement positions, yaw, width and height fields are preserved. Trees are not snapped to the resampled heightfield. Unity removes native tree instances whose pivots fall in terrain holes; the 31 invisible support terrains under `Rockport Trees - Terrain Openings` retain those placements without rendering ground or adding collision. Their `drawHeightmap` is false and they have no TerrainCollider.

Saved-scene validation compares every Terrain instance and physical tree against the placement manifest, including prototype identity and transform, and checks that their active meshes and materials come from the replacement directory. Source decoding and the earlier original-mesh precision reports remain archival evidence; those geometry precision claims do not apply to the newly modeled trees.

## Breakable trees

The 207 `XO_CT_CAMPUSSMACKTREE_1B_JG_00` and 274 `XO_CT_CAMPUSSMACKTREEB_1B_JG_00` source placements have matching original damaged and fragment solids. These 481 placements are under `Rockport Breakable Trees`, using the newly modeled tree visuals. They are absent from both Terrain arrays to prevent duplicate rendering. Ordinary trees remain Terrain instances.

Breakable trees use the existing `DestructibleProp` WholeBody behavior: a capsule trunk collider and kinematic Rigidbody become dynamic on a sufficient vehicle collision, then fall and collide through PhysX. Wood mass is estimated from the trunk cylinder at 650 kg/m³, with an 8 kg minimum. `ResetProp` restores the original pose and collision state. `OceanBuoyantBody` makes released trees respond to waves, buoyancy and drag. Breaking currently means whole-tree knockdown; stump splitting and fragment generation are not implemented.

## Reproduce and verify

From the project root:

```bash
python3 Tools/WorldTools/prepare_replacement_trees.py
```

With RockportMap open, run `create_replacement_tree_library_unity.cs` through the configured Unity relay, then `create_replacement_tree_prefabs_unity.cs` in bounded batches until `replacement-prefab-progress.txt` reaches `2004/2004`. Run `apply_replacement_trees_unity.cs` to replace every Terrain prototype and the 481 physical tree visuals without changing placement transforms or physics components.

Run `verify_terrain_trees_unity.cs` and `verify_replacement_tree_dependencies_unity.cs` to check placement preservation and absence of original tree dependencies. Run `run_ocean_smoke_unity.cs` for Play Mode collision, tree reset, buoyancy and drag checks. Render tools create temporary cameras and lighting; the saved map does not retain preview objects.

Evidence lives in `Art/RockportTrees/Source`:

- `replacement-tree-markers.json`: marker count, source interpretation and independent-generation policy.
- `replacement-tree-installation.json`: installed prefab and placement counts.
- `replacement-tree-dependencies.json`: replacement asset dependencies and mesh LOD validation.
- `unity-tree-verification.json`: actual Terrain and physical tree transform comparison.
- `breakable-tree-source.json`: source damage-family classification.
- `breakable-tree-import.json`: original physical conversion provenance.

`Art/RockportOcean/Source/ocean-playmode-verification.json` records the physical smoke results. Original exports under `Assets/NfsMw/Content/World/Models/RockportTrees` and the old prefab/material folders are retained as offline reference assets; the current map uses the replacement tree assets.
