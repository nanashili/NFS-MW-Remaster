"""Camera-visible industrial detail on the original warehouse footprint."""
import bpy,bmesh,math,json
from mathutils import Vector
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');ART=ROOT/'Art/FrontendCourtyard/Remake'
s=bpy.context.scene
assert s.name=='MW05 | Modern warehouse remake'
report={'removed_faces':{},'new_objects':[]}
detail=bpy.data.collections.new('02 | Remake fittings');s.collection.children.link(detail)
def material(name,color,metal,rough):
    m=bpy.data.materials.new(name);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    return m
steel=material('Remake | painted structural steel',(.052,.065,.066),.65,.4)
edge=material('Remake | zinc fittings',(.26,.285,.29),.85,.28)
rubber=material('Remake | dark gaskets',(.018,.021,.021),0,.8)
warm=material('Remake | sodium lamp glass',(.9,.5,.14),0,.3)
p=warm.node_tree.nodes.get('Principled BSDF');p.inputs['Emission Color'].default_value=(1,.48,.13,1);p.inputs['Emission Strength'].default_value=4
def moved(o,name,mat):
    o.name=name
    for c in list(o.users_collection):c.objects.unlink(o)
    detail.objects.link(o);o.data.materials.append(mat);report['new_objects'].append(name)
    return o
def uv(o):
    bpy.context.view_layer.objects.active=o;bpy.ops.object.select_all(action='DESELECT');o.select_set(True)
    if not o.data.uv_layers:
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
def ring(name,c,r,minor,mat,axis=(0,0,1),ellipse=1,sides=32):
    bpy.ops.mesh.primitive_torus_add(major_radius=r,minor_radius=minor,major_segments=sides,minor_segments=6,location=c)
    o=moved(bpy.context.object,name,mat);o.scale.y=ellipse
    o.rotation_euler=Vector(axis).to_track_quat('Z','Y').to_euler()
    for p in o.data.polygons:p.use_smooth=True
    return o
def box(name,c,dim,mat,bevel=.018):
    bpy.ops.mesh.primitive_cube_add(size=1,location=c);o=moved(bpy.context.object,name,mat);o.dimensions=dim
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Manufactured edge','BEVEL');mod.width=bevel;mod.segments=2
        bpy.ops.object.modifier_apply(modifier=mod.name)
        mod=o.modifiers.new('Weighted faces','WEIGHTED_NORMAL');bpy.ops.object.modifier_apply(modifier=mod.name)
    return o
def cyl(name,c,r,depth,mat,axis=(0,0,1),sides=8):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=r,depth=depth,location=c)
    o=moved(bpy.context.object,name,mat);o.rotation_euler=Vector(axis).to_track_quat('Z','Y').to_euler();return o
def light(name,c,target,power,color,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.color=color;data.shape='DISK';data.size=size
    o=bpy.data.objects.new(name,data);s.collection.objects.link(o);o.location=c;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o

# These are raster-era presentation surfaces, not structural mesh.
for o in list(s.objects):
    if o.type!='MESH':continue
    bm=bmesh.new();bm.from_mesh(o.data)
    gone=[f for f in bm.faces if o.data.materials[f.material_index].get('sourceTextureName') in {'QRACE_SHADOWMAP','QRACE_SHADOWMAP2','QRACE_SHADOWMAP3','QRACE_LIGHTRAYS01','QRACE_INDUSTRIAL_SCENE'}]
    report['removed_faces'][o.name]=len(gone)
    bmesh.ops.delete(bm,geom=gone,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bm.to_mesh(o.data);bm.free()

body=bpy.data.objects['Warehouse | original architecture']
tank=next(m for m in body.data.materials if m.get('sourceTextureName')=='QRACE_WATERTOWER')
tanks=[(-.92185,-7.7636,1.26495,1.1998,2.851,6.699),(-5.3494,-7.7636,.9487,.8998,2.395,4.225),(5.3137,-14.465,1.26495,1.1998,2.851,6.699)]
body.data.materials.append(steel);steel_slot=len(body.data.materials)-1
bm=bmesh.new();bm.from_mesh(body.data)
for index,(cx,cy,rx,ry,z0,z1) in enumerate(tanks,1):
    def inside(v):return abs(v.co.x-cx)<rx+.14 and abs(v.co.y-cy)<ry+.14
    old=[f for f in bm.faces if body.data.materials[f.material_index]==tank and all(v.co.z>=z1-.002 and inside(v) for v in f.verts)]
    bmesh.ops.delete(bm,geom=old,context='FACES')
    for f in bm.faces:
        if body.data.materials[f.material_index]==tank and all(v.co.z<z0+.001 and inside(v) for v in f.verts):f.material_index=steel_slot
    for j,z in enumerate([z0+.025,z0+(z1-z0)*.5,z1-.025]):
        ring('Tank %d | rolled seam %d'%(index,j),(cx,cy,z),rx+.008,.024,edge,ellipse=ry/rx,sides=48)
    # Rounded conical roof; native manufactured profile, matching source footprint.
    profile=[(1,0),(1,.05),(.97,.12),(.68,.43),(.3,.73),(.055,.82)]
    verts=[];faces=[];sides=48
    for rr,zz in profile:
        for j in range(sides):
            a=2*math.pi*j/sides;verts.append((cx+rx*rr*math.cos(a),cy+ry*rr*math.sin(a),z1+zz))
    for k in range(len(profile)-1):
        for j in range(sides):faces.append((k*sides+j,k*sides+(j+1)%sides,(k+1)*sides+(j+1)%sides,(k+1)*sides+j))
    faces.append(tuple(range((len(profile)-1)*sides,len(profile)*sides)))
    mesh=bpy.data.meshes.new('Rounded tank roof');mesh.from_pydata(verts,[],faces)
    o=bpy.data.objects.new('Tank %d | rounded roof'%index,mesh);detail.objects.link(o);o.data.materials.append(tank)
    report['new_objects'].append(o.name)
    for f in mesh.polygons:f.use_smooth=True
    uv(o)
bm.to_mesh(body.data);bm.free()

routes=json.loads((ROOT/'Art/FrontendCourtyard/Updated/geometry-report.json').read_text())['pipes']
for i,route in enumerate(routes,1):
    points=[Vector(p) for p in route['centerline']];r=route['radius']
    segments=[(a,b) for a,b in zip(points,points[1:]) if (b-a).length>2]
    for si,(a,b) in enumerate(segments):
        axis=(b-a).normalized();c=a+(b-a)*.5
        ring('Pipe %d.%d | coupling'%(i,si),c,r+.025,.045,edge,axis=axis)
        u=axis.cross(Vector((0,0,1))).normalized();v=axis.cross(u)
        for k in range(6):
            angle=k*math.tau/6;pos=c+(r+.057)*(math.cos(angle)*u+math.sin(angle)*v)
            cyl('Pipe %d.%d | fastener %d'%(i,si,k),pos,.027,.10,steel,axis=axis,sides=6)

# A few physical housings along the existing loading canopy, plus preview lights.
for i,y in enumerate([-1.8,1.9,-5.5]):
    box('Loading bay | fixture %d'%i,(8.18,y,3.16),(.30,.64,.16),steel)
    box('Loading bay | lens %d'%i,(8.18,y,3.073),(.21,.50,.024),warm,.009)
    light('Preview | canopy lamp %d'%i,(8.18,y,3.02),(7.2,y,0),90,(1,.56,.24),.5)
# Sharper contact detail on the original door frame.
for y in [-2.23,1.38]:box('Loading door | steel jamb',(10.28,y,2.47),(.12,.09,3.12),steel)
box('Loading door | lintel',(10.28,-.425,4.02),(.12,3.72,.10),steel)

for o in list(s.objects):
    if o.type=='LIGHT' and not o.name.startswith('Preview | canopy'):bpy.data.objects.remove(o,do_unlink=True)
light('Preview | warm courtyard key',(-3,-3,15),(2,-6,1),4800,(1,.81,.56),7)
light('Preview | cool open sky',(-7,4,9),(2,-5,3),2600,(.53,.70,1),12)
light('Preview | roof rim',(10,-15,16),(1,-7,4),6000,(1,.77,.45),6)
s.world.use_nodes=True;n=s.world.node_tree.nodes;l=s.world.node_tree.links;n.clear()
out=n.new('ShaderNodeOutputWorld');bg=n.new('ShaderNodeBackground');bg.inputs['Strength'].default_value=.18
sky=n.new('ShaderNodeTexSky');sky.sky_type='MULTIPLE_SCATTERING';sky.sun_elevation=math.radians(12);sky.sun_rotation=math.radians(145);sky.air_density=1.2
l.new(sky.outputs['Color'],bg.inputs['Color']);l.new(bg.outputs[0],out.inputs[0])
s.render.film_transparent=False;s.view_settings.exposure=0;s.view_settings.look='AgX - Medium High Contrast'
s.camera.data.lens=23
s.render.resolution_x=1600;s.render.resolution_y=900;s.render.resolution_percentage=60;s.cycles.samples=32
s.render.filepath=str(ART/'warehouse-remake-preview.png')
for o in detail.objects:
    if o.type=='MESH':uv(o)
bpy.ops.object.select_all(action='DESELECT')
bpy.data.orphans_purge(do_recursive=True);bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Art/FrontendCourtyard/warehouse-modern-remake.blend'))
(ART/'geometry-report.json').write_text(json.dumps(report,indent=2))
def render():bpy.ops.render.render(write_still=True);return None
bpy.app.timers.register(render,first_interval=.5)
print('Remake fittings saved; preview queued. Added %d detail objects'%len(report['new_objects']))
