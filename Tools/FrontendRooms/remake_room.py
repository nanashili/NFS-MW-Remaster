"""Modern material/edge pass on the recovered room, with original footprints and props."""
import bpy,bmesh,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');s=bpy.context.scene;ROOM=s['room'];DEST=ROOT/'Art/FrontendRooms'/ROOM
TEX=ROOT/'Art/FrontendRooms/SharedTextures';WARE=ROOT/'Art/FrontendCourtyard/Remake/Textures'
s.name='MW05 | '+ROOM+' modern remake'
def load(path,data=False):
    im=bpy.data.images.load(str(path),check_existing=True)
    if data:im.colorspace_settings.name='Non-Color'
    return im
def image_node(n,path,data=False):
    x=n.new('ShaderNodeTexImage');x.image=load(path,data);return x
def maps(m,colour,normal,orm):
    n=m.node_tree.nodes;l=m.node_tree.links;n.clear();p=n.new('ShaderNodeBsdfPrincipled');out=n.new('ShaderNodeOutputMaterial');l.new(p.outputs[0],out.inputs['Surface'])
    if colour:l.new(image_node(n,colour).outputs['Color'],p.inputs['Base Color'])
    else:p.inputs['Base Color'].default_value=(.18,.205,.22,1)
    if normal:
        nt=image_node(n,normal,True);nm=n.new('ShaderNodeNormalMap');nm.inputs['Strength'].default_value=.5;l.new(nt.outputs[0],nm.inputs['Color']);l.new(nm.outputs[0],p.inputs['Normal'])
    rt=image_node(n,orm,True);sep=n.new('ShaderNodeSeparateColor');sep.mode='RGB';l.new(rt.outputs[0],sep.inputs[0]);l.new(sep.outputs['Green'],p.inputs['Roughness']);l.new(sep.outputs['Blue'],p.inputs['Metallic'])
    m['modernMaps']=True
upgraded=[]
for m in list(bpy.data.materials):
    if not m.use_nodes:continue
    key=m.get('sourceTextureName','');n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF')
    if not p:continue
    albedo=next((x for x in n if x.type=='TEX_IMAGE' and x.image and x.image.colorspace_settings.name!='Non-Color'),None)
    if albedo and any(t in key for t in ['GIRDER','CABLE','SKIDS','WHITELINE','CRACKS','FAN']):
        l.new(albedo.outputs['Alpha'],p.inputs['Alpha']);m.surface_render_method='DITHERED';m.use_backface_culling=False
    if key in ['CRIB1_FLOOR','CSHOP_FLOOR','CARLOT_CONCRETEFLOOR']:
        maps(m,TEX/'concrete_BaseColor_2K.png',TEX/'concrete_Normal_2K.png',TEX/(ROOM+'_ORM_1K.png'));upgraded.append(key)
    elif key=='CRIB1_BRICK1':
        maps(m,TEX/'safehouse_wall_BaseColor_2K.png',TEX/'safehouse_wall_Normal_2K.png',TEX/'wall_ORM_1K.png');upgraded.append(key)
    elif key in ['CARLOT_WALLCONCONCRETE','CARLOT_RIPPLECONCRETE']:
        maps(m,TEX/'concrete_BaseColor_2K.png',TEX/'concrete_Normal_2K.png',TEX/'wall_ORM_1K.png');upgraded.append(key)
    elif key=='CSHOP_CORUGATEDMETAL':
        maps(m,WARE/'cladding_BaseColor_2K.png',WARE/'cladding_Normal_2K.png',WARE/'cladding_ORM_1K.png');upgraded.append(key)
    elif key in ['CSHOP_METAL01','CARLOT_WHITERUSTEDMETAL','CARLOT_BLACKRUSTEDMETAL','CRIB1_GIRDERNOALPHA']:
        maps(m,None,None,WARE/'pipe_ORM_1K.png');upgraded.append(key)
    elif 'PIPE' in key or 'VENT' in key or 'TANK' in key:
        p.inputs['Metallic'].default_value=.72;p.inputs['Roughness'].default_value=.35
    elif 'TOOLBOX' in key:
        p.inputs['Metallic'].default_value=.38;p.inputs['Roughness'].default_value=.28;p.inputs['Coat Weight'].default_value=.18
    elif 'WINDOW' in key:
        p.inputs['Roughness'].default_value=.24
    elif 'LAMP' in key or 'BLACKLIGHT' in key:
        p.inputs['Roughness'].default_value=.32
    # GREY is a deliberate solid-colour source material, absent as a texture in some archives.
    if key=='Standardmaterial':m['sourceTextureName']='GREY';p.inputs['Base Color'].default_value=(.12,.135,.15,1)
edge_count=0
for o in list(s.objects):
    if o.type!='MESH':continue
    me=o.data;uv=me.uv_layers.active
    for f in me.polygons:
        key=me.materials[f.material_index].get('sourceTextureName','')
        if uv and key in ['CRIB1_FLOOR','CSHOP_FLOOR','CARLOT_CONCRETEFLOOR','CSHOP_CORUGATEDMETAL','CARLOT_WALLCONCONCRETE','CARLOT_RIPPLECONCRETE']:
            for li in f.loop_indices:
                v=o.matrix_world@me.vertices[me.loops[li].vertex_index].co
                if abs(f.normal.z)>.7:uv.data[li].uv=(v.x/3,v.y/3)
                else:uv.data[li].uv=(v.y/2.4,v.z/3) if abs(f.normal.x)>.7 else (v.x/2.4,v.z/3)
    bm=bmesh.new();bm.from_mesh(me)
    # Preserve decal geometry and UVs. Bevel only genuine manifold metal edges.
    edges=[]
    for e in bm.edges:
        if not e.is_manifold or e.calc_face_angle()<.42:continue
        labels=[me.materials[f.material_index].get('sourceTextureName','') for f in e.link_faces]
        if labels[0]!=labels[1]:continue
        if any(t in labels[0] for t in ['PIPE','TANK','VENT','GIRDERNOALPHA','METAL01','RUSTEDMETAL','TOOLBOX']):edges.append(e)
    edge_count+=len(edges)
    if edges:bmesh.ops.bevel(bm,geom=edges,offset=.015,segments=3,affect='EDGES',clamp_overlap=True)
    bm.normal_update()
    for f in bm.faces:f.smooth=True
    for e in bm.edges:e.smooth=e.is_manifold and e.calc_face_angle()<.72
    bm.to_mesh(me);bm.free()
    # Explicitly split sharp boundaries; exported normals retain planar beams and smooth fillets.
    bpy.context.view_layer.objects.active=o;o.select_set(True)
    mod=o.modifiers.new('Preserve sharp metal boundaries','EDGE_SPLIT');mod.split_angle=.72;mod.use_edge_angle=True
    bpy.ops.object.modifier_apply(modifier=mod.name);o.select_set(False)
def solid(name,colour,metal=0,rough=.4,emit=0):
    m=bpy.data.materials.new(name);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*colour,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    if emit:p.inputs['Emission Color'].default_value=(*colour,1);p.inputs['Emission Strength'].default_value=emit
    return m
housing=solid(ROOM+' fixture | brushed metal',(.075,.095,.11),.8,.3)
warm=(1,.86,.68) if ROOM in ['SafeHouse','Performance'] else (.82,.91,1)
diffuser=solid(ROOM+' fixture | diffuser',warm,0,.36,3)
def box(name,loc,scale,mat):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat)
    be=o.modifiers.new('Rounded fixture edges','BEVEL');be.width=.012;be.segments=3;bpy.ops.object.modifier_apply(modifier=be.name);return o
for o in list(s.objects):
    if o.type=='LIGHT':bpy.data.objects.remove(o,do_unlink=True)
def area(name,pos,power,colour,size,target=None):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=colour;d.shape='RECTANGLE';d.size=size;d.size_y=size*.6
    o=bpy.data.objects.new(name,d);s.collection.objects.link(o);o.location=pos
    o.rotation_euler=(Vector(target or (pos[0],pos[1],0))-o.location).to_track_quat('-Z','Y').to_euler()
positions=[(-4,-6,4.8),(4,-6,4.8),(-4,4,4.8),(4,4,4.8)] if ROOM=='SafeHouse' else [(-3,-8,5.9),(3,-8,5.9),(-3,4,5.9),(3,4,5.9),(0,16,5.9)]
if ROOM=='Showroom':positions=[(-7,-6,3.4),(0,-6,3.4),(-7,5,3.4),(0,5,3.4)]
for i,pos in enumerate(positions):
    box(ROOM+' | LED housing '+str(i),pos,(.22,1.6,.07),housing)
    box(ROOM+' | LED diffuser '+str(i),(pos[0],pos[1],pos[2]-.043),(.16,1.52,.018),diffuser)
    area('Preview | fixture '+str(i),(pos[0],pos[1],pos[2]-.1),650 if ROOM=='Showroom' else 1400,warm,2.6)
area('Preview | broad bounce',(0,0,2.8),650,(.78,.85,1),6,(0,-6,1))
if ROOM in ['Performance','Visual']:
    for i,(x,y) in enumerate([(-9,-8),(9,-8),(-9,8),(9,8)]):
        area('Preview | workbay bounce '+str(i),(x,y,3.5),1100,warm,4,(x,y,1))
if ROOM=='Showroom':area('Preview | daylight opening',(6,-1,3),2400,(.7,.82,1),8,(-5,-2,1))
s.world.node_tree.nodes.get('Background').inputs[1].default_value=.12 if ROOM=='Showroom' else .035
poses={'SafeHouse':((6,8,1.65),(-3,-4,2.1),22),'Showroom':((5,7,1.5),(-6,-6,1.6),23),'Visual':((1.5,7,1.6),(-2,-8,2.3),23),'Performance':((-2,7,1.7),(3,-8,2.3),23)}
pos,target,lens=poses[ROOM];s.camera.location=pos;s.camera.rotation_euler=(Vector(target)-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.lens=lens
s.render.resolution_x=1600;s.render.resolution_y=900;s.render.resolution_percentage=60;s.cycles.samples=40;s.cycles.use_denoising=True
s.render.filepath=str(DEST/'remake-preview-draft.png')
bpy.data.orphans_purge(do_recursive=True);bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(DEST/(ROOM+'-modern-remake.blend')))
report={'room':ROOM,'sourceArchive':s['sourceArchive'],'sourceSha256':s['sourceSha256'],'upgradedSurfaces':upgraded,'beveledSourceEdges':edge_count,'fixtures':len(positions),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in s.objects if o.type=='MESH'),'noCar':True,'noSkyMesh':True,'normalMethod':'Inferred from generated colour with native Cycles bump bake, not measured surface scans'}
(DEST/'remake-report.json').write_text(json.dumps(report,indent=2))
(DEST/'preview-lighting-reference.json').write_text(json.dumps({'blenderPreviewOnly':True,'lights':[{'name':o.name,'position':list(o.location),'rotationRadians':list(o.rotation_euler),'energyWatts':o.data.energy,'color':list(o.data.color),'size':o.data.size} for o in s.objects if o.type=='LIGHT'],'camera':{'position':list(s.camera.location),'rotationRadians':list(s.camera.rotation_euler),'lens':lens}},indent=2))
def render():bpy.ops.render.render(write_still=True);return None
bpy.app.timers.register(render,first_interval=.2)
print(json.dumps(report))
