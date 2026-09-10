"""Run via Blender MCP after export_map.py; source meshes stay at game coordinates."""
import bpy, json
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
old=bpy.data.scenes.get('Rockport Buildings')
scene=bpy.data.scenes.new('Rockport Buildings staging')
if old:
    old_meshes={o.data for o in old.objects if o.type=='MESH'}
    for obj in list(old.objects):bpy.data.objects.remove(obj,do_unlink=True)
    bpy.data.scenes.remove(old)
    for mesh in old_meshes:
        if mesh.users==0:bpy.data.meshes.remove(mesh)
scene.name='Rockport Buildings'
bpy.context.window.scene=scene
import contextlib,io
with contextlib.redirect_stdout(io.StringIO()):
    bpy.ops.import_scene.gltf(filepath=str(ROOT/'Assets/NfsMw/Content/World/Models/RockportBuildings/RockportBuildings.gltf'))
objects=[o for o in scene.objects if o.type=='MESH']
for obj in objects:obj.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
low=Vector((float('inf'),)*3);high=Vector((float('-inf'),)*3)
for obj in objects:
    for corner in obj.bound_box:
        point=obj.matrix_world@Vector(corner)
        for axis in range(3):low[axis]=min(low[axis],point[axis]);high[axis]=max(high[axis],point[axis])
center=(low+high)/2
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        space=area.spaces.active;space.clip_end=30000;space.region_3d.view_distance=max(high-low)*0.75;space.region_3d.view_location=center
        space.shading.type='SOLID';space.shading.color_type='MATERIAL'
report={'meshObjects':len(objects),'vertices':sum(len(o.data.vertices) for o in objects),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects),'boundsMin':list(low),'boundsMax':list(high),'source':'Imported original placements from RockportBuildings.gltf; Blender Z up','scene':scene.name}
(ROOT/'Art/RockportBuildings/Source/blender-validation.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Art/RockportBuildings/RockportBuildings.blend'))
print(json.dumps(report))
