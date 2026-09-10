"""Export a geometry-only glTF with shared materials and external textures."""
import bpy
import bmesh
import json
from pathlib import Path

ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
scene=bpy.context.scene
remake=scene.name=='MW05 | Modern warehouse remake'
assert remake or scene.name=='MW05 | Unity warehouse updated'
DEST=ROOT/('Assets/NfsMw/Content/Frontend/Models/FrontendWarehouseRemake' if remake else 'Assets/NfsMw/Content/Frontend/Models/FrontendWarehouse')
REPORT=ROOT/('Art/FrontendCourtyard/Remake' if remake else 'Art/FrontendCourtyard/Updated')
DEST.mkdir(parents=True,exist_ok=True)
original=[o for o in scene.objects if o.type=='MESH']
canonical={}
for o in original:
    for slot in o.material_slots:
        m=slot.material
        key=m.get('sourceTextureHash') or m.name
        if key not in canonical:canonical[key]=m
        slot.material=canonical[key]

# Keep editable Blender parts, but export one static mesh with one submesh per
# material. The small courtyard is always viewed together from the menu camera.
copies=[]
bpy.ops.object.select_all(action='DESELECT')
for o in original:
    c=o.copy();c.data=o.data.copy();scene.collection.objects.link(c)
    c.select_set(True);copies.append(c)
bpy.context.view_layer.objects.active=copies[0]
bpy.ops.object.join()
combined=bpy.context.object
combined.name='MW_Warehouse_Remake' if remake else 'MW_Warehouse_Updated'
bm=bmesh.new();bm.from_mesh(combined.data)
bmesh.ops.triangulate(bm,faces=list(bm.faces))
degenerate=[f for f in bm.faces if f.calc_area()<1e-9]
if degenerate:bmesh.ops.delete(bm,geom=degenerate,context='FACES')
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.to_mesh(combined.data);bm.free()
for m in combined.data.materials:
    if m is not None:
        m.name='MW_'+(m.get('sourceTextureName') or m.name).removeprefix('QRACE_')
combined.data.calc_loop_triangles()
triangles=len(combined.data.loop_triangles)
vertices=len(combined.data.vertices)
scene.unit_settings.system='METRIC'
scene.unit_settings.scale_length=1
bpy.ops.export_scene.gltf(filepath=str(DEST/'Warehouse.gltf'),export_format='GLTF_SEPARATE',export_texture_dir='Textures',use_selection=True,export_apply=True,export_texcoords=True,export_normals=True,export_tangents=True,export_materials='EXPORT',export_cameras=False,export_lights=False,export_animations=False,export_extras=False)
bpy.data.objects.remove(combined,do_unlink=True)
bpy.data.orphans_purge(do_recursive=True)

path=DEST/'Warehouse.gltf'
gltf=json.loads(path.read_text())
material_details=[]
for m in gltf.get('materials',[]):
    n=m.get('name','')
    # Foliage/rail cutouts use depth-writing alpha test, while soft graffiti,
    # tire marks and shadows retain blended alpha.
    if any(key in n for key in ['WEED','FENCE','FALLTREETOP','TRANSPROOF','MAPLEBCH']):
        m['alphaMode']='MASK';m['alphaCutoff']=.4;m['doubleSided']=True
    material_details.append({'name':n,'alphaMode':m.get('alphaMode','OPAQUE'),'doubleSided':m.get('doubleSided',False)})
path.write_text(json.dumps(gltf,indent=2))
stats={'mesh_triangles':triangles,'blender_vertices':vertices,'export_vertices':sum(gltf['accessors'][p['attributes']['POSITION']]['count'] for mesh in gltf['meshes'] for p in mesh['primitives']),'mesh_count':len(gltf['meshes']),'material_count':len(gltf.get('materials',[])),'primitive_count':sum(len(m['primitives']) for m in gltf['meshes']),'texture_images':len(gltf.get('images',[])),'cameras':len(gltf.get('cameras',[])),'lights':len(gltf.get('extensions',{}).get('KHR_lights_punctual',{}).get('lights',[])),'materials':material_details}
(REPORT/'export-report.json').write_text(json.dumps(stats,indent=2))
assert stats['cameras']==0 and stats['lights']==0
assert stats['mesh_triangles']<(30000 if remake else 20000)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Art/FrontendCourtyard'/('warehouse-modern-remake.blend' if remake else 'warehouse-unity-updated.blend')))
print(json.dumps({k:v for k,v in stats.items() if k!='materials'}))
