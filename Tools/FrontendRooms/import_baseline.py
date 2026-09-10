"""Import one recovered environment via Blender MCP; ROOM supplied by caller."""
import bpy,bmesh,json,re,math
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
ARCHIVES={'SafeHouse':'CAREER_SAFEHOUSE','Showroom':'CAR_LOT','Performance':'CUSTOMIZATION_SHOP_BACKROOM','Visual':'CUSTOMIZATION_SHOP'}
archive=ARCHIVES[ROOM];EX=ROOT/'Art/FrontendRooms/Artwork'/archive;DEST=ROOT/'Art/FrontendRooms'/ROOM
DEST.mkdir(exist_ok=True)
manifest=json.loads((EX/'manifest.json').read_text());textures={t['hash']:t for t in manifest['textures']}
s=bpy.data.scenes.new('MW05 | '+ROOM);bpy.context.window.scene=s
for old in list(bpy.data.scenes):
    if old!=s:bpy.data.scenes.remove(old)
bpy.data.orphans_purge(do_recursive=True)
collection=bpy.data.collections.new('01 | Recovered '+ROOM);s.collection.children.link(collection)
canonical={};missing=set();removed={};parts=[]
for model in manifest['models']:
    file=model['file'];name=file.split(archive+'-')[-1].removesuffix('.obj')
    if name not in {'BACKDROP_CRIB','BACKDROP_CAST_SHADOW_MAP','BACKDROP_CAST_SHADOW_MAP2','BACKDROP_CAST_SHADOW_MAP3'}:continue
    mtl={};key=''
    for line in (EX/'models'/(name+'.mtl')).read_text().splitlines():
        if line.startswith('newmtl '):key=line.split(' ',1)[1]
        elif line.startswith('map_Kd '):
            found=re.search(r'0x[0-9A-Fa-f]{8}',line)
            if found:mtl[key]=found.group().upper().replace('0X','0x')
    before=set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=str(EX/'models'/file))
    imported=[o for o in bpy.data.objects if o not in before]
    for o in imported:
        if o.type!='MESH':continue
        o.rotation_euler=(0,0,0);o.name=ROOM+' | '+name
        for c in list(o.users_collection):c.objects.unlink(o)
        collection.objects.link(o);parts.append(o)
        for slot in o.material_slots:
            sourceName=re.sub(r'\.\d{3}$','',slot.material.name)
            h=mtl.get(sourceName);meta=textures.get(h)
            identity=h or sourceName
            if identity in canonical:slot.material=canonical[identity];continue
            mat=bpy.data.materials.new(ROOM+' | '+(meta['name'] if meta else sourceName));mat.use_nodes=True
            mat['sourceTextureHash']=h or '';mat['sourceTextureName']=meta['name'] if meta else sourceName
            mat['sourceMaterial']=sourceName
            n=mat.node_tree.nodes;l=mat.node_tree.links;p=n.get('Principled BSDF')
            p.inputs['Base Color'].default_value=(.16,.17,.17,1);p.inputs['Roughness'].default_value=.68
            mat.use_backface_culling=True
            if meta:
                im=bpy.data.images.load(str(EX/'textures-png'/(h+'.png')),check_existing=True)
                tex=n.new('ShaderNodeTexImage');tex.image=im;l.new(tex.outputs['Color'],p.inputs['Base Color'])
                source=meta['name'].upper()
                # Specular data may occupy alpha on opaque base-colour textures.
                cutout=any(k in source for k in ['GRAF','BANNER','FENCE','GRIDER','GIRDER','RAIL','RAY','SHADOW','LEAF','TREE','WEED','FLAG','LOGO','SIGN','CARPET','CABLE','SKIDS','WHITELINE','CRACKS'])
                if meta['alphaExtrema'][0]<255 and cutout:
                    l.new(tex.outputs['Alpha'],p.inputs['Alpha']);mat.surface_render_method='DITHERED';mat.use_backface_culling=False
                if any(k in source for k in ['PIPE','METAL','GRIDER','TOOL','RIM','VENT']):p.inputs['Metallic'].default_value=.55;p.inputs['Roughness'].default_value=.4
                normal=next((t for t in manifest['textures'] if t['name']==source+'_NORMAL'),None)
                if normal:
                    nt=n.new('ShaderNodeTexImage');nt.image=bpy.data.images.load(str(EX/'textures-png'/(normal['hash']+'.png')),check_existing=True);nt.image.colorspace_settings.name='Non-Color'
                    nm=n.new('ShaderNodeNormalMap');nm.inputs['Strength'].default_value=.5;l.new(nt.outputs['Color'],nm.inputs['Color']);l.new(nm.outputs['Normal'],p.inputs['Normal'])
            else:missing.add(identity)
            canonical[identity]=mat;slot.material=mat
        bm=bmesh.new();bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
        remove=[]
        for f in bm.faces:
            m=o.data.materials[f.material_index];label=(m.get('sourceTextureName','')+' '+m.get('sourceMaterial','')).upper()
            if any(x in label for x in ['LIGHTRAY','LIGHT_RAY','LIGHT RAY','LIGHTRAYES','SHADOW','SKY','CARCOVER','CAR_COVER','CARCLOTH']):remove.append(f)
            elif name=='BACKDROP_CRIB' and 'CONCRETEFLOOR' not in label and max(v.co.z for v in f.verts)<.12 and min(v.co.z for v in f.verts)<-.2:remove.append(f)
        removed[o.name]=len(remove)
        bmesh.ops.delete(bm,geom=remove,context='FACES')
        loose=[v for v in bm.verts if not v.link_faces]
        if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
        bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.normal_update()
        for f in bm.faces:f.smooth=True
        for e in bm.edges:e.smooth=e.is_manifold and e.calc_face_angle()<math.radians(42)
        bm.to_mesh(o.data);bm.free()
        o['source_file']=file

def area(name,pos,at,power,color,size):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color
    o=bpy.data.objects.new(name,d);s.collection.objects.link(o);o.location=pos;o.rotation_euler=(Vector(at)-o.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.cameras.new('Preview camera');o=bpy.data.objects.new('Preview camera',camera);s.collection.objects.link(o);s.camera=o
o.location=(6,8,1.8);target=(-3,-4,2.1)
if ROOM=='Showroom':o.location=(6,8,1.5);target=(-7,-7,2.2)
o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();camera.lens=22;camera.clip_end=500
area('Preview | soft key',(1,1,5),(0,0,0),1700,(1,.82,.62),5)
area('Preview | cool fill',(-6,-2,4),(0,0,1),1600,(.62,.76,1),5)
area('Preview | background',(-3,-8,5),(-2,0,1),1400,(1,.88,.72),4)
world=bpy.data.worlds.new('Preview world');s.world=world;world.use_nodes=True
world.node_tree.nodes.get('Background').inputs[0].default_value=(.42,.48,.58,1)
world.node_tree.nodes.get('Background').inputs[1].default_value=.3 if ROOM=='Showroom' else .06
s.render.engine='CYCLES';s.cycles.device='GPU';s.cycles.samples=24
s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=60
s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
s.render.image_settings.file_format='PNG';s.render.film_transparent=False;s.render.filepath=str(DEST/'baseline-preview.png')
s['sourceArchive']=str(EX);s['room']=ROOM;s['sourceSha256']=manifest['sha256'];s['noCar']=True
bpy.data.orphans_purge(do_recursive=True);bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(DEST/(ROOM+'-baseline.blend')))
report={'room':ROOM,'objects':[o.name for o in parts],'removedFaces':removed,'missingMaterials':sorted(missing),'materials':[{'name':m.name,'texture':m.get('sourceTextureName'),'hash':m.get('sourceTextureHash')} for m in canonical.values()],'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in parts)}
(DEST/'baseline-report.json').write_text(json.dumps(report,indent=2))
def render():bpy.ops.render.render(write_still=True);return None
bpy.app.timers.register(render,first_interval=.5)
print(json.dumps({'room':ROOM,'triangles':report['triangles'],'missing':report['missingMaterials'],'preview':'queued'}))
