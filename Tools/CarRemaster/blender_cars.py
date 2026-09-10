"""Run build_car(car_id) through Blender MCP. All dimensions are in metres.

Retains source silhouettes, UVs and stock parts. Authors a distinct PBR derivative
with constrained subdivision, machined wheel hardware and geometric tyre tread.
"""
import bpy
import bmesh
import math
import json
import re
import contextlib
import io
import hashlib
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path('/Users/tihan-nico/NFS MW Remaster')
OUT = ROOT / 'Art/Cars'
PALETTE = [(0.035,0.10,0.20,1),(0.32,0.015,0.025,1),(0.32,0.34,0.36,1),(0.018,0.024,0.03,1),
           (0.65,0.28,0.018,1),(0.055,0.19,0.12,1),(0.55,0.57,0.58,1)]

def hash_name(text):
    value = 0xffffffff
    for char in text:
        value = (value * 33 + ord(char)) & 0xffffffff
    return f'{value:08x}'

def material(name, color, metal=0, rough=0.5, coat=0, alpha=1):
    m=bpy.data.materials.new(name)
    m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=color
    p.inputs['Metallic'].default_value=metal
    p.inputs['Roughness'].default_value=rough
    p.inputs['Coat Weight'].default_value=coat
    p.inputs['Coat Roughness'].default_value=0.12
    p.inputs['Alpha'].default_value=alpha
    m.diffuse_color=(*color[:3],alpha)
    if alpha<1:
        m.surface_render_method='DITHERED'
    return m

def source_material(state, solid, slot):
    key=state['source']['materials'].get(solid+'/'+slot)
    textures=state['source']['textures']
    car=state['car']
    category='paint'
    if any(t in slot for t in ('WINDOW','GLASS')):category='glass'
    elif 'DOORLINE' in slot:category='seam'
    elif 'CARBON' in slot:category='carbon'
    elif 'CHROME' in slot or 'ALUMIN' in slot or slot.startswith('RIM_'):category='metal'
    elif 'TIRE' in slot or 'TREAD' in slot:category='rubber'
    elif any(t in slot for t in ('BULB','BRAKELIGHT','HEADLIGHT')):category='lamp'
    elif any(t in slot for t in ('MOLD','PLASTIC','BOTTOM','GRILL')):category='plastic'
    elif any(t in slot for t in ('INTERIOR','CLOTH','DRIVER')):category='interior'
    elif any(t in slot for t in ('BADG','LICENSE','SKIN','TEXTURE','TRAFFIC','COP_')):category='textured'
    if key and key in textures and 'status' not in textures[key] and category=='paint':
        # Dynamic skin targets are absent from the source packs; real skins stay textured.
        texname=textures[key].get('name','')
        if 'SKIN' in texname:category='textured'
    if ('HEADLIGHT' in slot or 'BRAKELIGHT' in slot) and (not key or 'status' in textures.get(key,{})):
        token='BRAKELIG' if 'BRAKELIGHT' in slot else 'HEADLIGH'
        candidates=[k for k,t in textures.items() if t.get('name','').startswith(car+'_KIT00_'+token) and 'status' not in t]
        # Select exact runtime replacement suffix when recoverable; otherwise retain authored shader.
        for suffix in ('_OFF','_ON',''):
            candidate=hash_name(car+'_KIT00_'+('BRAKELIGHT' if token=='BRAKELIG' else 'HEADLIGHT')+suffix)
            if candidate in candidates:
                key=candidate;break
    signature=(category,key if category in ('textured','interior','lamp','carbon','plastic','metal','seam','rubber') else None,slot if category=='lamp' else '')
    if signature in state['materials']:return state['materials'][signature]
    values={
        'paint':(state['paint'],0.60,0.29,1,1),
        'textured':((1,1,1,1),0.32,0.30,0.8,1),
        'glass':((0.035,0.055,0.065,1),0.0,0.09,1,0.30),
        'seam':((0.012,0.014,0.016,1),0,0.48,0,1),
        'carbon':((0.12,0.12,0.12,1),0.2,0.32,0.7,1),
        'metal':((0.55,0.58,0.62,1),0.95,0.20,0.4,1),
        'rubber':((0.018,0.020,0.023,1),0,0.80,0,1),
        'lamp':((0.45,0.012,0.009,1) if 'BRAKE' in slot else (0.75,0.80,0.85,1),0.2,0.20,1,1),
        'plastic':((0.11,0.12,0.13,1),0,0.56,0,1),
        'interior':((0.40,0.40,0.40,1),0,0.72,0,1)}
    m=material(car+'_'+category+'_'+str(len(state['materials'])),*values[category])
    m['source_slot']=slot
    m['source_texture']=key or ''
    m['surface_category']=category
    path=OUT/'Prepared'/car/'Textures'/((key or '')+'.png')
    if category in ('textured','interior','lamp','carbon','plastic','metal','rubber') and path.is_file():
        tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(path),check_existing=True)
        p=m.node_tree.nodes.get('Principled BSDF');m.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
    state['materials'][signature]=m
    return m

def read_obj(state, solid, lod, parent, slots=None):
    source=OUT/'Models'/state['car']/'GEOMETRY/models'/('DEFAULT-GEOMETRY.BIN-'+solid+'.obj')
    if not source.is_file():
        source=next((OUT/'Models'/state['car']/'GEOMETRY/models').glob('*-'+solid+'.obj'))
    vertices=[];uvs=[];faces=[];face_uv=[];mat_indices=[];names=[];current=0
    for line in source.read_text().splitlines():
        parts=line.split()
        if not parts:continue
        if parts[0]=='v':vertices.append(tuple(float(x) for x in parts[1:4]))
        elif parts[0]=='vt':uvs.append(tuple(float(x) for x in parts[1:3]))
        elif parts[0]=='usemtl':
            name=parts[1]
            if name not in names:names.append(name)
            current=names.index(name)
        elif parts[0]=='f':
            if slots is not None and not slots(names[current]):continue
            refs=[v.split('/') for v in parts[1:]]
            faces.append([int(v[0])-1 for v in refs]);face_uv.append([int(v[1])-1 for v in refs]);mat_indices.append(current)
    if not faces:return None
    mesh=bpy.data.meshes.new(solid+'_mesh');mesh.from_pydata(vertices,[],faces);mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    for polygon,refs,mi in zip(mesh.polygons,face_uv,mat_indices):
        polygon.material_index=mi
        for li,ui in zip(polygon.loop_indices,refs):uv.data[li].uv=uvs[ui]
    for name in names:mesh.materials.append(source_material(state,solid,name))
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.00001)
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bm.to_mesh(mesh);bm.free();mesh.update()
    pivot=state['source']['solids'][solid]['pivot']
    # Vertex coordinates already carry the source orientation; apply the pivot position only.
    mesh.transform(Matrix.Translation(Vector(pivot[12:15])))
    obj=bpy.data.objects.new(solid,mesh);bpy.context.scene.collection.objects.link(obj);obj.parent=parent
    obj['source_solid']=solid
    obj['source_archive_sha256']=state['source']['sourceSha256']
    for polygon in mesh.polygons:polygon.use_smooth=True
    mesh.set_sharp_from_angle(angle=math.radians(50))
    if lod==0 and ('BODY_' in solid or 'HOOD_' in solid or 'DOOR_' in solid or 'BUMPER_' in solid):
        # Limited quad recovery; sharp/boundary edges remain creased to preserve gaps.
        bm=bmesh.new();bm.from_mesh(mesh)
        bmesh.ops.join_triangles(bm,faces=list(bm.faces),angle_face_threshold=0.45,angle_shape_threshold=0.70,
                                 cmp_uvs=True,cmp_materials=True)
        crease=bm.edges.layers.float.new('crease_edge')
        for edge in bm.edges:
            if not edge.is_manifold or edge.calc_face_angle(0)>math.radians(38):edge[crease]=1.0
        bm.to_mesh(mesh);bm.free()
        sub=obj.modifiers.new('Surface refinement with protected panel boundaries','SUBSURF');sub.levels=1;sub.render_levels=1
        sub.uv_smooth='PRESERVE_BOUNDARIES'
    elif lod==0 and any(t in solid for t in ('MIRROR','SPOILER','BRAKE','HEADLIGHT')):
        bevel=obj.modifiers.new('Machined edge highlight','BEVEL');bevel.width=0.0012;bevel.segments=2;bevel.limit_method='ANGLE';bevel.angle_limit=0.7
    if lod==2:
        dec=obj.modifiers.new('Distance reduction','DECIMATE');dec.ratio=0.35
    return obj

def mesh_object(name,vertices,faces,mat,parent):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.materials.append(mat);mesh.update()
    for p in mesh.polygons:p.use_smooth=True
    o=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(o);o.parent=parent
    return o

def lathe(name,profile,mat,parent,segments=128,tread=False):
    # Source wheel axle is Y. Profile contains (axial position, radius).
    v=[];f=[]
    for j,(y,r) in enumerate(profile):
        for i in range(segments):
            a=2*math.pi*i/segments
            groove=0.0025 if tread and j>3 and j<len(profile)-4 and (i+j//3)%8==0 else 0
            rr=r-groove
            v.append((rr*math.cos(a),y,rr*math.sin(a)))
    for j in range(len(profile)-1):
        for i in range(segments):
            n=(i+1)%segments
            f.append((j*segments+i,j*segments+n,(j+1)*segments+n,(j+1)*segments+i))
    return mesh_object(name,v,f,mat,parent)

def hardware(state,parent,radius,width,segments):
    rubber=state['hardware']['rubber'];alloy=state['hardware']['alloy'];steel=state['hardware']['steel'];dark=state['hardware']['dark']
    w=width;r=radius
    profile=[(-.5*w,.72*r),(-.515*w,.82*r),(-.50*w,.92*r),(-.43*w,.985*r),(-.35*w,r)]
    for u in [-.28,-.12,.12,.28]:profile.extend([(u*w-.004,r),(u*w-.002,r-.006),(u*w+.002,r-.006),(u*w+.004,r)])
    profile.extend([(.35*w,r),(.43*w,.985*r),(.5*w,.92*r),(.515*w,.82*r),(.5*w,.72*r)])
    lathe('Performance tyre - grooved tread',profile,rubber,parent,segments,True)
    for side in [-1,1]:
        y=side*.512*w
        for rr in [.78*r,.84*r]:
            lathe('Moulded sidewall bead',[(y-.0008,rr-.001),(y,rr),(y+.0008,rr-.001)],rubber,parent,segments)
        lathe('Machined rim flange',[(side*.50*w,.70*r),(side*.505*w,.725*r),(side*.49*w,.74*r),(side*.475*w,.735*r)],alloy,parent,segments)
    # Vented rotor with two friction faces and radial cooling vanes.
    disc=.60*r;inner=.22*r;y=-.25*w
    lathe('Brake disc friction ring',[(y,inner),(y,disc),(y+.018,disc),(y+.018,inner),(y,inner)],steel,parent,segments)
    lathe('Brake disc aluminium hat',[(y-.012,.08*r),(y-.012,inner),(y,inner),(y,.08*r)],alloy,parent,segments)
    verts=[];faces=[]
    for i in range(48):
        a=2*math.pi*i/48
        # Dark recessed slots on the rotor, geometrically separated from the face.
        base=len(verts)
        for rr,aa in [(disc*.65,a),(disc*.95,a+.10),(disc*.95,a+.115),(disc*.65,a+.015)]:
            verts.append((rr*math.cos(aa),y-.00015,rr*math.sin(aa)))
        faces.append(tuple(range(base,base+4)))
    mesh_object('Rotor recessed slot inserts',verts,faces,dark,parent)
    # Source wheel designs retain their spoke count; five lug nuts are authored service hardware.
    for i in range(5):
        a=2*math.pi*i/5
        nut=lathe('Hex wheel fastener',[(0,.009),(.012,.009),(.014,.006)],alloy,parent,6)
        nut.location=(r*.19*math.cos(a),-.51*w,r*.19*math.sin(a))
    valve=lathe('Tyre valve stem',[(0,.004),(.019,.004),(.021,.005)],dark,parent,10)
    valve.location=(r*.64,-.51*w,0)

def selected_solids(source,lod):
    names=list(source['solids'])
    suffix='_A'  # Preserve the source's complete top-detail stock assembly before LOD reduction.
    candidates=[n for n in names if n.endswith(suffix) and ('_KIT00_' in n or '_KIT_' in n)]
    ordinary_body=any(n.endswith('_KIT00_BODY_A') for n in candidates)
    result=[]
    for n in candidates:
        if any(t in n for t in ('DECAL','DRIVER','_TIRE_','_BRAKE_','UNIVERSAL_SPOILER_BASE')):continue
        if 'DAMAGE' in n and (ordinary_body or 'DAMAGE0_' not in n):continue
        result.append(n)
    if not result:
        result=[n for n in names if n.endswith('_BODY_A')]
    return result

def needs_wheel_face(source):
    front=next(n for n in source['solids'] if n.endswith('_FRONT_TIRE_A'))
    return not any(k.startswith(front+'/RIM_') for k in source['materials'])

def wheel_face(state,parent,r,w):
    # Legacy traffic wheels have painted-on rims. Give those a separate geometric face.
    alloy=state['hardware']['alloy'];v=[];f=[]
    for i in range(8):
        angle=2*math.pi*i/8;base=len(v)
        for y in [-.50*w,-.44*w]:
            for rr,a in [(.14*r,angle-.14),(.68*r,angle-.055),(.68*r,angle+.055),(.14*r,angle+.14)]:
                v.append((rr*math.cos(a),y,rr*math.sin(a)))
        for ids in [(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]:f.append(tuple(base+j for j in ids))
    mesh_object('Authored eight spoke wheel face',v,f,alloy,parent)
    lathe('Wheel centre cap',[(-.515*w,0),(-.515*w,.16*r),(-.44*w,.16*r)],alloy,parent,48)

def empty(name,parent=None):
    o=bpy.data.objects.new(name,None);bpy.context.scene.collection.objects.link(o);o.parent=parent;return o

def build_car(car,replace_generated=False):
    source=json.loads((OUT/'Prepared'/car/'source.json').read_text())
    scene=bpy.data.scenes.new(car+' Remastered');bpy.context.window.scene=scene
    scene.unit_settings.system='METRIC'
    state={'car':car,'source':source,'materials':{},'paint':PALETTE[int(hash_name(car),16)%len(PALETTE)]}
    if car.startswith('BMW'):state['paint']=(0.025,0.065,0.14,1)
    state['hardware']={
        'rubber':material(car+'_TyreRubber',(.018,.021,.025,1),0,.78),
        'alloy':material(car+'_MachinedAlloy',(.48,.52,.58,1),.92,.23),
        'steel':material(car+'_BrakeSteel',(.23,.25,.28,1),.86,.39),
        'dark':material(car+'_HardwareBlack',(.014,.018,.02,1),.35,.48)}
    root=empty(car+'_Remastered');root.rotation_euler.z=-math.pi/2
    root['source_model']=car;root['derivative']='Source-based PBR and geometry detail pass; not a ground-up vehicle scan'
    lods=[];counts=[]
    tires=[n for n in source['solids'] if n.endswith('_FRONT_TIRE_A')]
    if not tires:raise ValueError(car+' has no source front wheel')
    front=tires[0];rear=next((n for n in source['solids'] if n.endswith('_REAR_TIRE_A')),front)
    chassis=source['chassis']
    if not chassis:raise ValueError(car+' has no recovered chassis')
    offsets=source['visual']['tireOffsets']
    if not offsets or len(offsets)!=4:raise ValueError(car+' needs four visual wheel offsets')
    ride_height=sum(chassis['RIDE_HEIGHT'])/200
    root.location.z=ride_height
    for lod in range(3):
        group=empty('LOD'+str(lod),root);lods.append(group)
        body=empty('Body',group)
        for name in selected_solids(source,lod):read_obj(state,name,lod,body)
        wheel_info=[]
        for axle,solid in enumerate([front,rear]):
            bounds=source['solids'][solid];lo=bounds['boundsMin'];hi=bounds['boundsMax']
            radius=max(hi[0]-lo[0],hi[2]-lo[2])/2;width=hi[1]-lo[1]
            center_y=(hi[1]+lo[1])/2
            for side in [1,-1]:
                wheel=empty(('Front' if axle==0 else 'Rear')+('Left' if side==1 else 'Right')+'Wheel',group)
                offset=offsets[(0 if side==1 else 1) if axle==0 else (3 if side==1 else 2)]
                wheel.location=(offset[0],offset[1]-side*center_y,offset[2]+offset[3]-ride_height)
                scale=offset[3]/radius
                wheel.scale=(scale,1,scale)
                # Original tyre local Y points inward on the left wheel.
                wheel.scale.y=-side
                obj=read_obj(state,solid,lod,wheel,slots=(lambda n:n.startswith('RIM_')) if lod==0 else None)
                if obj:obj.location.y=-center_y
                if lod==0:
                    hardware(state,wheel,radius,width,128)
                    if obj is None:wheel_face(state,wheel,radius,width)
                    # Consolidate authored hardware while retaining a separate wheel transform.
                    bpy.ops.object.select_all(action='DESELECT')
                    parts=[o for o in wheel.children if o.type=='MESH']
                    for part in parts:part.select_set(True)
                    bpy.context.view_layer.objects.active=parts[0]
                    bpy.ops.object.join()
                    parts[0].name='Detailed wheel assembly'
                wheel_info.append({'name':wheel.name,'position':list(wheel.location),'radius':radius,'width':width})
                extra=source['visual'].get('extraRearTireOffset',0)
                if axle==1 and extra:
                    additional=wheel.copy();bpy.context.scene.collection.objects.link(additional)
                    additional.name='Additional'+wheel.name;additional.parent=group;additional.location.x+=extra
                    for child in wheel.children:
                        copied=child.copy();bpy.context.scene.collection.objects.link(copied);copied.parent=additional
                    wheel_info.append({'name':additional.name,'position':list(additional.location),'radius':radius,'width':width})
        bpy.context.view_layer.update()
        deps=bpy.context.evaluated_depsgraph_get()
        meshes=[o for o in group.children_recursive if o.type=='MESH']
        triangles=sum(sum(len(p.vertices)-2 for p in o.evaluated_get(deps).data.polygons) for o in meshes)
        counts.append({'lod':lod,'triangles':triangles,'meshObjects':len(meshes)})
    # Bake the negative wheel transform into exported geometry; glTF records correct winding.
    report={'car':car,'sourceSha256':source['sourceSha256'],'stockParts':selected_solids(source,0),
            'authoredWheelFace':needs_wheel_face(source),
            'extraRearTireOffset':source['visual'].get('extraRearTireOffset',0),
            'sourcePivotPositionsApplied':True,
            'pipelineSha256':hashlib.sha256((ROOT/'Tools/CarRemaster/blender_cars.py').read_bytes()).hexdigest(),
            'lods':counts,'wheelAssembly':wheel_info,'chassis':chassis,'sourceTextureCount':sum('status' not in t for t in source['textures'].values()),
            'limitations':['Source-derived exterior and interior, not a ground-up scan or Forza-equivalent asset.',
                           'Original ecar offsets and radius; authored resting pose uses chassis ride height converted from centimetres.',
                           'Authored PBR values replace legacy game shaders; dynamic vinyl composites are not recovered.']}
    dest=ROOT/source['assetFolder']/'Remastered.glb'
    if dest.exists() and not replace_generated:raise FileExistsError(dest)
    bpy.ops.object.select_all(action='DESELECT')
    for o in [root]+list(root.children_recursive):o.select_set(True)
    bpy.context.view_layer.objects.active=root
    with contextlib.redirect_stdout(io.StringIO()):
        bpy.ops.export_scene.gltf(filepath=str(dest),export_format='GLB',use_selection=True,use_active_scene=True,export_apply=True,
                                  export_extras=True,export_cameras=False,export_lights=False,export_yup=True)
    for m in state['materials'].values():
        for node in m.node_tree.nodes:
            if node.type=='TEX_IMAGE' and node.image and not node.image.packed_file:node.image.pack()
    art=OUT/'Remastered'/car;art.mkdir(parents=True,exist_ok=True)
    report['asset']=str(dest.relative_to(ROOT))
    (art/'report.json').write_text(json.dumps(report,indent=2)+'\n')
    # Save only this scene and its dependencies, avoiding cross-car scene duplication.
    for group in lods[1:]:
        for obj in [group]+list(group.children_recursive):obj.hide_set(True);obj.hide_render=True
    bpy.data.libraries.write(str(art/(car+'.blend')),{scene},path_remap='RELATIVE_ALL')
    print(json.dumps(report))
    return state,scene,root
