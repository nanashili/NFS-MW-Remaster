"""Studio preview of the active remastered car scene. Run through Blender MCP."""
import bpy
from mathutils import Vector
from pathlib import Path

def studio_preview(car, size=1400, output_path=None):
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=32
    scene.cycles.use_denoising=True
    scene.render.resolution_x=size
    scene.render.resolution_y=round(size*.65)
    scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new(car+' Studio World')
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.15,.18,.23,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
    ground=bpy.data.materials.new('Studio floor');ground.diffuse_color=(.08,.10,.12,1)
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015))
    bpy.context.object.data.materials.append(ground)
    target=Vector((0,0,.7))
    for name,pos,power,size in [('Key',(-3,-4,6),1800,5),('Rim',(3,2,5),2200,4),('Fill',(-4,3,3),1300,3)]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
        o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=pos
        o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Preview camera');cam=bpy.data.objects.new('Preview camera',data)
    scene.collection.objects.link(cam);cam.location=(6,-7,3.8)
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();data.lens=48
    scene.camera=cam
    scene.render.filepath=output_path or str(Path('/Users/tihan-nico/NFS MW Remaster/Art/Cars/Remastered')/car/'preview.png')
    bpy.ops.render.render(write_still=True)
