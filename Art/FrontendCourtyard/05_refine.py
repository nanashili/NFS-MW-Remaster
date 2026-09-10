"""Refinements after inspecting the first rendered camera image."""
from mathutils import Matrix
current_col=arch
for y,z,w,h in [(4.4,9.05,5.6,3.8),(11.3,9.1,3.1,2.7),(3.2,2.5,3.8,2.3)]:
    before=set(arch.objects)
    window('West return | factory glazing',0,0,z,w,h,max(5,int(w/.57)),max(4,int(h/.56)))
    transform=Matrix.Translation(Vector((-18.37,y,0))) @ Matrix.Rotation(math.pi/2,4,'Z')
    for o in set(arch.objects)-before:o.matrix_world=transform @ o.matrix_world

# Desaturate the fresh-looking orange rust and add irregular dark runoff strips.
for mat,lo,hi in [(rust,(.016,.019,.011),(.135,.09,.039)),(rust2,(.013,.02,.014),(.09,.108,.049))]:
    ramp=next(n for n in mat.node_tree.nodes if n.type=='VALTORGB')
    ramp.color_ramp.elements[0].color=(*lo,1);ramp.color_ramp.elements[1].color=(*hi,1)
    mat.diffuse_color=(*hi,1)
for node in steel.node_tree.nodes:
    if node.type=='VECT_MATH' and node.operation=='MULTIPLY':node.inputs[1].default_value=(.55,.55,4)

soot=weather('Runoff | accumulated rain grime',(.008,.012,.008),(.04,.049,.025),5,0,(3,3,.12))
for x in [-21.3,-18.2,-13.6,-12.4,-5.6,-1.6]:
    h=random.uniform(.7,2.4)
    mesh('West facade | irregular runoff stain',[(x,11.905,4.1),(x+.24,11.905,4.1),(x+.18,11.905,4.1+h*.9),(x+.06,11.905,4.1+h)],[(0,1,2,3)],soot)
for x in [3,7,10.3,14.5,18,22.6,24.4]:
    h=random.uniform(.8,2.2)
    mesh('East base | irregular damp tide',[(x,14.9,1.1),(x+1.1,14.9,1.1),(x+.86,14.9,h),(x+.24,14.9,h*.9)],[(0,1,2,3)],soot)

# Patchy fissures instead of continuous regular Voronoi cells.
n,l=asphalt.node_tree.nodes,asphalt.node_tree.links
crack_ramp=next(node for node in n if node.type=='VALTORGB' and abs(node.color_ramp.elements[0].position-.004)<.0001)
oldlinks=list(crack_ramp.outputs[0].links)
coords=next(node for node in n if node.type=='TEX_COORD')
masknoise=n.new('ShaderNodeTexNoise');masknoise.inputs['Scale'].default_value=.39;masknoise.inputs['Detail'].default_value=4;l.new(coords.outputs['Object'],masknoise.inputs[0])
mask=n.new('ShaderNodeMapRange');mask.clamp=True;mask.inputs['From Min'].default_value=.38;mask.inputs['From Max'].default_value=.65;l.new(masknoise.outputs[0],mask.inputs[0])
patched=n.new('ShaderNodeMixRGB');patched.inputs[1].default_value=(.7,.7,.6,1);l.new(mask.outputs[0],patched.inputs[0]);l.new(crack_ramp.outputs[0],patched.inputs[2])
for link in oldlinks:l.new(patched.outputs[0],link.to_socket)
scene.objects['Courtyard | cool haze backlight'].data.energy=500
scene.objects['Sun | low warm break in cloud'].data.energy=2
scene.objects['Sky | broad soft courtyard fill'].data.energy=1700
scene.view_settings.exposure=.15
cam=scene.camera
cam.location=(10,-21,2.25)
cam.rotation_euler=(Vector((-1,10,4.15))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.lens=28
scene.cycles.samples=64
scene.render.resolution_percentage=100

# Keep the active Blender viewport clear and frame the actual render camera.
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.overlay.show_overlays=False
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.region_3d.view_camera_zoom=10
print('REFINEMENTS READY',len(scene.objects))
