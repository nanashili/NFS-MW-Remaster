"""Export each room independently as static geometry, preserving editable source parts."""
import bpy,bmesh,json
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');s=bpy.context.scene;ROOM=s['room'];ART=ROOT/'Art/FrontendRooms'/ROOM;DEST=ROOT/'Assets/NfsMw/Content/World/Models'/('Frontend'+ROOM+'Remake');DEST.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='DESELECT');copies=[]
for o in list(s.objects):
    if o.type!='MESH':continue
    c=o.copy();c.data=o.data.copy();s.collection.objects.link(c);c.select_set(True);copies.append(c)
bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();combined=bpy.context.object;combined.name='MW_'+ROOM+'_Remake'
bm=bmesh.new();bm.from_mesh(combined.data);bmesh.ops.triangulate(bm,faces=list(bm.faces))
bad=[f for f in bm.faces if f.calc_area()<1e-9]
if bad:bmesh.ops.delete(bm,geom=bad,context='FACES')
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.to_mesh(combined.data);bm.free();triangles=len(combined.data.polygons)
export_objects=[combined]
# Keep each static mesh below Unity's secondary-UV vertex expansion limit.
# Preserve connected pieces and their original UV/material data when batching.
if triangles>20000 and s.get('solidPropsRebuilt',False):
    bm=bmesh.new();bm.from_mesh(combined.data);bm.faces.ensure_lookup_table();bm.faces.index_update()
    unseen=set(bm.faces);islands=[]
    while unseen:
        seed=unseen.pop();stack=[seed];indices=[]
        while stack:
            face=stack.pop();indices.append(face.index)
            for edge in face.edges:
                for neighbor in edge.link_faces:
                    if neighbor in unseen:unseen.remove(neighbor);stack.append(neighbor)
        islands.append(indices)
    bm.free();islands.sort(key=lambda indices:min(indices));batches=[];batch=[]
    for indices in islands:
        for offset in range(0,len(indices),12000):
            piece=indices[offset:offset+12000]
            if batch and len(batch)+len(piece)>12000:batches.append(batch);batch=[]
            batch.extend(piece)
    if batch:batches.append(batch)
    combined.select_set(False);export_objects=[]
    for index,indices in enumerate(batches):
        part=combined.copy();part.data=combined.data.copy();s.collection.objects.link(part)
        part.name='MW_'+ROOM+'_Static_'+str(index+1).zfill(2)
        bm=bmesh.new();bm.from_mesh(part.data);bm.faces.ensure_lookup_table();bm.faces.index_update();keep=set(indices)
        bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.index not in keep],context='FACES')
        loose=[v for v in bm.verts if not v.link_faces]
        if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
        bm.to_mesh(part.data);bm.free();part.select_set(True);export_objects.append(part)
    bpy.data.objects.remove(combined,do_unlink=True);bpy.context.view_layer.objects.active=export_objects[0]
if s.get('referencePropsRefined',False):
    # Supply a non-overlapping lightmap channel. Unity 6000.6's unwrapper asserts
    # when welding shared boundary vertices across glTF primitive base offsets.
    for part in export_objects:
        bpy.ops.object.select_all(action='DESELECT');part.select_set(True);bpy.context.view_layer.objects.active=part
        if len(part.data.uv_layers)<2:part.data.uv_layers.new(name='LightmapUV')
        part.data.uv_layers.active_index=1
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=.66,island_margin=.012,area_weight=0.0,correct_aspect=True,scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT');part.data.uv_layers.active_index=0;part.data.uv_layers[0].active_render=True
    bpy.ops.object.select_all(action='DESELECT')
    for part in export_objects:part.select_set(True)
s.unit_settings.system='METRIC';s.unit_settings.scale_length=1
bpy.ops.export_scene.gltf(filepath=str(DEST/(ROOM+'.gltf')),export_format='GLTF_SEPARATE',export_texture_dir='Textures',use_selection=True,export_apply=True,export_normals=True,export_tangents=True,export_texcoords=True,export_materials='EXPORT',export_cameras=False,export_lights=False,export_animations=False,export_extras=False)
for obj in export_objects:bpy.data.objects.remove(obj,do_unlink=True)
path=DEST/(ROOM+'.gltf');g=json.loads(path.read_text())
for m in g.get('materials',[]):
    if any(k in m['name'] for k in ['FENCE','GIRDER |','MAPLEBCH','FALLTREETOP','TREE_DEC','CABLE','FAN']):
        if m.get('alphaMode')=='BLEND':m['alphaMode']='MASK';m['alphaCutoff']=.4;m['doubleSided']=True
path.write_text(json.dumps(g,indent=2))
stats={'room':ROOM,'mesh_triangles':triangles,'export_vertices':sum(g['accessors'][p['attributes']['POSITION']]['count'] for mesh in g['meshes'] for p in mesh['primitives']),'mesh_count':len(g['meshes']),'material_count':len(g.get('materials',[])),'primitive_count':sum(len(m['primitives']) for m in g['meshes']),'texture_images':len(g.get('images',[])),'cameras':len(g.get('cameras',[])),'lights':len(g.get('extensions',{}).get('KHR_lights_punctual',{}).get('lights',[]))}
assert stats['cameras']==0 and stats['lights']==0 and triangles<s.get('triangleBudget',50000)
(ART/'export-report.json').write_text(json.dumps(stats,indent=2))
(ART/'preview-lighting-reference.json').write_text(json.dumps({'blenderPreviewOnly':True,'lights':[{'name':o.name,'position':list(o.location),'rotationRadians':list(o.rotation_euler),'energyWatts':o.data.energy,'color':list(o.data.color),'size':o.data.size} for o in s.objects if o.type=='LIGHT'],'camera':{'position':list(s.camera.location),'rotationRadians':list(s.camera.rotation_euler),'lens':s.camera.data.lens}},indent=2))
s.render.resolution_percentage=100;s.cycles.samples=80;s.render.filepath=str(ART/'remake-preview.png')
bpy.data.orphans_purge(do_recursive=True);bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ART/(ROOM+'-modern-remake.blend')))
def render():bpy.ops.render.render(write_still=True);return None
bpy.app.timers.register(render,first_interval=.2)
print(json.dumps(stats))
