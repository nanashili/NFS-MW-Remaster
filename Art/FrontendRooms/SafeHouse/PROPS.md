# Safe-house prop rebuild

The original prop layout has been rebuilt with real volume and mechanical details. Painted surface graphics remain suitable for posters, graffiti and banners. Existing folded pizza boxes and draped rags retain their modeled shapes and artwork.

The added drum and cylinder lettering has been removed. `removed-prop-text-report.json` records the 20 removed lettering objects; `no-prop-text-unity-import.log` verifies the resulting export.

- 16 hollow tires with flat tread crowns, compact shoulders and three recessed circumferential channels. The user-supplied normal map adds the fine tread pattern, mapped across the tread width with 26 repeats around the circumference. Seven visible wheels have ten swept alloy spokes, hubs and lug bolts. The old raised tread blocks have been removed.
- Eight round drums with narrow rolled seams, recessed lids, hex bung plugs and worn black paint with a red center band.
- Two red pressure cylinders with rounded shoulders, foot rings, brass valves, handwheels and labels; the two modeled extinguishers remain.
- Eleven round cable runs, two fans with blades and guards, and four recessed vent grilles with individual louvers.
- Two tool cabinets with drawers, pulls and casters; a workbench with legs, shelf, vise, tools and tins.
- Two gray fabric sofas with puffed cushions, tufted backs, stitched piping and tapered feet, including one chaise extension. Refrigerator, CRT television and fuel pump retain separated panels and controls.
- Cartons, wooden crates, wastebasket, paper stacks, beverage cans, storage bins and individual stacked bricks.

The current export contains 183,552 triangles, 17 static meshes, 63 shared materials across 126 submeshes, and 60 referenced texture images. Geometry and import counts are recorded in `export-report.json` and `unity-import-report.json`. Connected pieces are batched below 12,000 triangles per mesh. A separate lightmap UV channel is packed in Blender and exported as `TEXCOORD_1`; Unity consumes it directly, avoiding its automatic unwrapper's assertions when welding shared boundaries across glTF material primitives. The larger geometry budget replaces the old image stand-ins; runtime FPS still requires profiling in the complete game. Source-only textures left from preceding versions are not referenced by the new model. Import reports count only textures referenced by the glTF.

The five supplied reference images are preserved in `References/`. The supplied tire, couch fabric, brick and floor normal maps are preserved without pixel changes in `ReferenceTextures/user_*_Normal.*`; The brick normal change has been reverted; the supplied brick file remains archived as a reference. Unity imports the active exported normals as linear BC5 with a 2048-pixel size cap, mipmaps and streaming. Chipped paint, fabric base color and lid surfaces use native Cycles material bakes. `reference-refinement-report.json` records the geometry methods.

The couch uses the supplied fabric normal at strength 0.24 with 12 UV repeats. The supplied floor normal uses strength 0.28 and six repeats per three-meter UV tile, retaining the floor's existing base color and metallic value. A separate material limits it to 270 ground polygons. The two wall materials and the loose-brick material have been restored from `SafeHouse-before-user-surface-normals.blend`, including their previous base colors and normals. `user-surface-normals-report.json` records these settings. Geometry is unchanged by this surface pass.

The prior Blender model is retained as `SafeHouse-before-prop-rebuild.blend`. The current model remains `SafeHouse-modern-remake.blend`; the Unity path remains `Assets/NfsMw/Content/Frontend/Models/FrontendSafeHouseRemake/SafeHouse.gltf`.

`SafeHouse-before-reference-refinement.blend` preserves the first prop pass. To reproduce the current refinement, open that file, run `Tools/FrontendRooms/refine_safehouse_references.py` through Blender, wait for its native material bakes, then run `apply_safehouse_surface_normals.py` to apply only the retained couch and floor maps. Finally run `export_room.py` and `render_safehouse_prop_details.py` from the same directory. `SafeHouse-before-user-surface-normals.blend` preserves the model immediately before applying the three surface maps.

## Tires and wheels

![Modeled tires and wheels](</Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse/tires-detail.png>)

## Opposite side of the room

![Safe house prop layout](</Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse/props-reverse-view.png>)

## Drums

![Round drums](</Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse/barrels-detail.png>)

## Upholstery

![Gray upholstered sofas](</Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse/sofas-detail.png>)

## Gas cylinders

![Reference-based gas cylinders](</Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse/cylinders-detail.png>)

## Supplied surface normals

![Supplied fabric normal on upholstery](</Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse/fabric-detail.png>)

![Supplied floor surface normal](</Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse/floor-detail.png>)
