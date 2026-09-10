"""Run in Blender via MCP. Creates an isolated environment scene without vehicles."""
import bpy, math, random, os
from mathutils import Vector
random.seed(2005)
OUT = '/Users/tihan-nico/NFS MW Remaster/Art/FrontendCourtyard'
scene = bpy.context.scene
# Preserve other Blender scenes; this authored scene is environment-only.
source_scene = scene
scene = bpy.data.scenes.new('MW05 | Warehouse Courtyard')
bpy.context.window.scene = scene

def collection(name):
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    return c

arch = collection('02 | Warehouse architecture')
metal = collection('03 | Silos and pipework')
detail = collection('04 | Yard dressing')
ground_col = collection('05 | Ground')
atmo = collection('06 | Atmosphere and lighting')
cams = collection('07 | Cameras')
current_col = arch

def move(o, col=None):
    for c in list(o.users_collection):
        c.objects.unlink(o)
    (col or current_col).objects.link(o)
    return o


def simple(name, color, rough=.7, metallic=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color,1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color,1)
    bs.inputs['Roughness'].default_value = rough
    bs.inputs['Metallic'].default_value = metallic
    return m

def weather(name, dark, light, scale=3, metallic=0, stretch=(1,1,1)):
    m = simple(name, light, .79, metallic)
    n, l = m.node_tree.nodes, m.node_tree.links
    bs = n.get('Principled BSDF')
    tc=n.new('ShaderNodeTexCoord')
    vm=n.new('ShaderNodeVectorMath'); vm.operation='MULTIPLY'; vm.inputs[1].default_value=stretch
    l.new(tc.outputs['Object'],vm.inputs[0])
    noise=n.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=scale; noise.inputs['Detail'].default_value=5; noise.inputs['Roughness'].default_value=.75
    l.new(vm.outputs[0],noise.inputs['Vector'])
    ramp=n.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position=.23; ramp.color_ramp.elements[0].color=(*dark,1); ramp.color_ramp.elements[1].position=.77; ramp.color_ramp.elements[1].color=(*light,1)
    l.new(noise.outputs['Fac'],ramp.inputs[0]); l.new(ramp.outputs[0],bs.inputs['Base Color'])
    bump=n.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.48; bump.inputs['Distance'].default_value=.045
    l.new(noise.outputs['Fac'],bump.inputs['Height']); l.new(bump.outputs[0],bs.inputs['Normal'])
    return m

rust = weather('Cladding | layered umber corrosion',(.025,.026,.014),(.24,.12,.045),3,.65,(1.6,1.6,.12))
rust2 = weather('Cladding | oxidised olive',(.023,.03,.021),(.145,.155,.079),3,.6,(1.5,1.5,.12))
steel = weather('Steel | tarnished zinc',(.09,.12,.105),(.43,.48,.39),4,.82,(1,1,.18))
darksteel = weather('Steel | blackened structural',(.013,.017,.014),(.075,.09,.069),3,.72)
concrete = weather('Concrete | damp exposed aggregate',(.045,.048,.035),(.24,.23,.16),2)
brick = weather('Brick | soot and faded mortar',(.055,.04,.026),(.18,.115,.065),5)
black = simple('Rubber / dark apertures',(.009,.013,.012),.92)
wood = weather('Timber | old shipping crates',(.065,.047,.021),(.24,.18,.075),3,0,(.15,2,2))
paint = weather('Paint | dirty ivory',(.17,.18,.13),(.49,.48,.31),5)
glass = weather('Windows | dusty amber glazing',(.095,.115,.08),(.39,.42,.28),3,.25)
bs=glass.node_tree.nodes.get('Principled BSDF'); bs.inputs['Emission Color'].default_value=(.27,.3,.16,1); bs.inputs['Emission Strength'].default_value=.3

def cube(name, loc, size, mat, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o=bpy.context.object; o.name=name; o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if mat: o.data.materials.append(mat)
    if bevel:
        mod=o.modifiers.new('Light-catching edges','BEVEL'); mod.width=bevel; mod.segments=2
    return move(o)

def mesh(name, verts, faces, mat):
    me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
    o=bpy.data.objects.new(name,me); current_col.objects.link(o)
    if mat: me.materials.append(mat)
    return o

def cylinder(name,loc,radius,depth,mat,vertices=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=loc)
    o=bpy.context.object; o.name=name; o.data.materials.append(mat)
    for p in o.data.polygons: p.use_smooth=len(p.vertices)==4
    return move(o)

def beam(name,a,b,r,mat):
    a,b=Vector(a),Vector(b)
    o=cylinder(name,(a+b)/2,r,(b-a).length,mat,16)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return o

def path(name,points,r,mat):
    cu=bpy.data.curves.new(name,'CURVE'); cu.dimensions='3D'; cu.resolution_u=1; cu.bevel_depth=r; cu.bevel_resolution=2
    sp=cu.splines.new('POLY'); sp.points.add(len(points)-1)
    for p,co in zip(sp.points,points):p.co=(*co,1)
    o=bpy.data.objects.new(name,cu); current_col.objects.link(o); cu.materials.append(mat)
    return o

def torus(name,loc,major,minor,mat,rot=None):
    bpy.ops.mesh.primitive_torus_add(major_segments=40,minor_segments=8,location=loc,major_radius=major,minor_radius=minor)
    o=bpy.context.object; o.name=name; o.data.materials.append(mat)
    if rot:o.rotation_euler=rot
    return move(o)

def text_obj(name,body,loc,size,mat):
    cu=bpy.data.curves.new(name,'FONT'); cu.body=body; cu.size=size; cu.extrude=.001; cu.align_x='CENTER'
    o=bpy.data.objects.new(name,cu); current_col.objects.link(o); o.location=loc; o.rotation_euler=(math.pi/2,0,0); cu.materials.append(mat)
    return o

print('SETUP READY; environment only')
