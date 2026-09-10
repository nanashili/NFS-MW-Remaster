"""Ground, atmospheric look, practical lights and reference-inspired menu camera."""
current_col=ground_col
asphalt=weather('Yard | cracked tar and dirt',(.025,.03,.018),(.21,.2,.115),.7,0)
n,l=asphalt.node_tree.nodes,asphalt.node_tree.links
bs=n.get('Principled BSDF')
tc=n.new('ShaderNodeTexCoord')
noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=1.7;noise.inputs['Detail'].default_value=6
l.new(tc.outputs['Object'],noise.inputs['Vector'])
warp=n.new('ShaderNodeVectorMath');warp.operation='SCALE';warp.inputs[3].default_value=.27;l.new(noise.outputs['Color'],warp.inputs[0])
add=n.new('ShaderNodeVectorMath');add.operation='ADD';l.new(tc.outputs['Object'],add.inputs[0]);l.new(warp.outputs[0],add.inputs[1])
vor=n.new('ShaderNodeTexVoronoi');vor.feature='DISTANCE_TO_EDGE';vor.inputs['Scale'].default_value=.48;l.new(add.outputs[0],vor.inputs['Vector'])
crack=n.new('ShaderNodeValToRGB');crack.color_ramp.elements[0].position=.004;crack.color_ramp.elements[0].color=(.015,.018,.009,1);crack.color_ramp.elements[1].position=.022;crack.color_ramp.elements[1].color=(.7,.7,.6,1);l.new(vor.outputs['Distance'],crack.inputs[0])
old=bs.inputs['Base Color'].links[0].from_socket
mix=n.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.8;l.new(old,mix.inputs[1]);l.new(crack.outputs[0],mix.inputs[2]);l.new(mix.outputs[0],bs.inputs['Base Color'])
fine=n.new('ShaderNodeTexNoise');fine.inputs['Scale'].default_value=85;fine.inputs['Detail'].default_value=3;l.new(tc.outputs['Object'],fine.inputs['Vector'])
bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.65;bump.inputs['Distance'].default_value=.035;l.new(fine.outputs[0],bump.inputs['Height'])
cb=n.new('ShaderNodeBump');cb.inputs['Strength'].default_value=.7;cb.inputs['Distance'].default_value=.035;l.new(crack.outputs[0],cb.inputs['Height']);l.new(bump.outputs[0],cb.inputs['Normal']);l.new(cb.outputs[0],bs.inputs['Normal'])
rough=n.new('ShaderNodeMapRange');rough.inputs['From Min'].default_value=.2;rough.inputs['From Max'].default_value=.8;rough.inputs['To Min'].default_value=.5;rough.inputs['To Max'].default_value=.95;l.new(noise.outputs[0],rough.inputs[0]);l.new(rough.outputs[0],bs.inputs['Roughness'])
cube('Courtyard | continuous cracked asphalt',(0,0,-.16),(90,90,.3),asphalt)

puddle=weather('Ground | oily standing water',(.018,.024,.016),(.067,.076,.041),4,.3)
puddle.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.14
for x,y,sx,sy in [(-12,4,2.6,.8),(7,5,2.2,.6),(-3,8,1.8,.7),(15,9,2.7,.9),(-17,-1,2.3,.6),(3,-5,1.8,.45)]:
    verts=[(x,y,.005)]
    for i in range(32):
        t=i*math.tau/32;r=random.uniform(.85,1.15);verts.append((x+math.cos(t)*sx*r,y+math.sin(t)*sy*r,.005))
    mesh('Yard | irregular damp patch',verts,[(0,i+1,(i+1)%32+1) for i in range(32)],puddle)
current_col=detail
for i in range(55):
    x=random.uniform(-18,24);y=random.uniform(8,14)
    if 0<x<16:y=random.uniform(12.5,14)
    o=cube('Yard edge | scattered rubble',(x,y,.045),(random.uniform(.04,.18),random.uniform(.06,.21),random.uniform(.03,.08)),concrete,.01);o.rotation_euler.z=random.uniform(0,6.28)
for x in [-16,-14,-1,3,17,19,24]:
    for i in range(7):
        xx=x+random.uniform(-.4,.4);yy=13.9+random.uniform(-.35,.2)
        path('Yard edge | dry weed',[(xx,yy,0),(xx+random.uniform(-.12,.12),yy,.15),(xx+random.uniform(-.2,.2),yy+random.uniform(-.1,.1),random.uniform(.22,.48))],.007,rust2)

current_col=atmo
def light(name,kind,loc,energy,color,size=1,target=None):
    d=bpy.data.lights.new(name,kind);d.energy=energy;d.color=color
    if kind=='AREA':d.shape='DISK';d.size=size
    if kind=='POINT':d.shadow_soft_size=size
    o=bpy.data.objects.new(name,d);current_col.objects.link(o);o.location=loc
    if target:o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
    return o

bulb=simple('Practical | warm luminous glass',(.75,.69,.41),.3)
bs=bulb.node_tree.nodes.get('Principled BSDF');bs.inputs['Emission Color'].default_value=(1,.85,.5,1);bs.inputs['Emission Strength'].default_value=5
for x,y,z in [(-17.7,11.1,4.6),(-12,10,4.5),(-6.5,10,4.5),(-2.2,11.25,4.6),(19,14,5.2)]:
    path('Workshop lamp | gooseneck',[(x,y+.4,z+.2),(x,y+.12,z+.3),(x,y-.12,z+.2),(x,y-.12,z)],.035,darksteel)
    bpy.ops.mesh.primitive_cone_add(vertices=24,radius1=.23,radius2=.075,depth=.14,location=(x,y-.12,z-.03))
    o=bpy.context.object;o.name='Workshop lamp | enamel shade';o.data.materials.append(darksteel);move(o)
    cylinder('Workshop lamp | bulb',(x,y-.12,z-.105),.09,.035,bulb,16)
    light('Workshop lamp | warm pool','POINT',(x,y-.22,z-.2),65,(1,.82,.46),.16)

world=bpy.data.worlds.new('Rockport | storm-yellow overcast');scene.world=world;world.use_nodes=True
n,l=world.node_tree.nodes,world.node_tree.links;n.clear()
out=n.new('ShaderNodeOutputWorld');bg=n.new('ShaderNodeBackground');bg.inputs['Strength'].default_value=.7
tc=n.new('ShaderNodeTexCoord');mapping=n.new('ShaderNodeVectorMath');mapping.operation='MULTIPLY';mapping.inputs[1].default_value=(1.1,1.1,3.8);l.new(tc.outputs['Normal'],mapping.inputs[0])
noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=3;noise.inputs['Detail'].default_value=5;noise.inputs['Roughness'].default_value=.65;l.new(mapping.outputs[0],noise.inputs[0])
ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.28;ramp.color_ramp.elements[0].color=(.065,.075,.028,1);ramp.color_ramp.elements[1].position=.74;ramp.color_ramp.elements[1].color=(1,.83,.36,1)
e=ramp.color_ramp.elements.new(.51);e.color=(.28,.3,.12,1)
l.new(noise.outputs[0],ramp.inputs[0]);l.new(ramp.outputs[0],bg.inputs[0]);l.new(bg.outputs[0],out.inputs['Surface'])
sun=light('Sun | low warm break in cloud','SUN',(-12,-4,16),2.5,(1,.82,.46),target=(3,8,0));sun.data.angle=math.radians(14)
light('Sky | broad soft courtyard fill','AREA',(0,-4,14),2300,(.77,.83,.71),18,target=(0,8,3))
light('Silos | warm metallic rim','AREA',(3,5,13),1700,(1,.9,.65),8,target=(9,12,6))
light('Courtyard | cool haze backlight','AREA',(7,14,3),1400,(.73,.85,.81),10,target=(7,2,.6))

fog=bpy.data.materials.new('Atmosphere | rolling ground mist');fog.use_nodes=True
n,l=fog.node_tree.nodes,fog.node_tree.links;n.clear();out=n.new('ShaderNodeOutputMaterial');vol=n.new('ShaderNodeVolumePrincipled');vol.inputs['Color'].default_value=(.67,.7,.58,1);vol.inputs['Anisotropy'].default_value=.25
tc=n.new('ShaderNodeTexCoord');dist=n.new('ShaderNodeVectorMath');dist.operation='DISTANCE';dist.inputs[1].default_value=(.5,.5,.42);l.new(tc.outputs['Generated'],dist.inputs[0])
fade=n.new('ShaderNodeMapRange');fade.clamp=True;fade.inputs['From Min'].default_value=.18;fade.inputs['From Max'].default_value=.58;fade.inputs['To Min'].default_value=.24;fade.inputs['To Max'].default_value=0;l.new(dist.outputs['Value'],fade.inputs[0])
noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=5.5;noise.inputs['Detail'].default_value=3;l.new(tc.outputs['Generated'],noise.inputs[0])
mult=n.new('ShaderNodeMath');mult.operation='MULTIPLY';l.new(noise.outputs[0],mult.inputs[0]);l.new(fade.outputs[0],mult.inputs[1]);l.new(mult.outputs[0],vol.inputs['Density']);l.new(vol.outputs['Volume'],out.inputs['Volume'])
for x,y,sx in [(-12,8,15),(1,9,15),(13,10,17)]:cube('Ground mist | soft procedural volume',(x,y,.8),(sx,5,2),fog)

current_col=cams
bpy.ops.object.camera_add(location=(9,-24,2.5));cam=move(bpy.context.object);cam.name='MENU | reference-inspired low wide shot';cam.data.lens=29
cam.rotation_euler=(Vector((0,10,4.1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.clip_end=250;scene.camera=cam
bpy.ops.object.empty_add(location=(-1,4,0));anchor=move(bpy.context.object);anchor.name='MENU | clear presentation zone';anchor.empty_display_size=2
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=75
scene.render.image_settings.file_format='PNG';scene.render.filepath=OUT+'/courtyard-preview.png'
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.3
scene.render.film_transparent=False
scene['Purpose']='Warehouse-only main-menu environment reconstructed from user image. Approximate dimensions and camera; not a measured original-game reconstruction.'
scene['Vehicle']='None. Presentation zone intentionally empty per user request.'
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.type='MATERIAL'
print('LOOKDEV READY; objects',len(scene.objects))
