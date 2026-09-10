"""Representative Blender studio check of six enhanced aftermarket wheels."""
import bpy,json,math,re
from pathlib import Path
from mathutils import Vector
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');art=ROOT/'Art/Cars/Modifications'
with bpy.data.libraries.load(str(art/'WHEELS/WHEELS.blend')) as (src,dst):dst.scenes=[src.scenes[0]]
scene=dst.scenes[0];bpy.context.window.scene=scene
for o in scene.objects:o.hide_render=True;o.hide_set(True)
report=json.loads((art/'WHEELS/report.json').read_text())
choices=['5ZIGEN_STYLE01_17_25','BBS_STYLE01_17_25','ENKEI_STYLE01_17_25','OZ_STYLE01_17_25','RACINGHART_STYLE01_17_25','VOLK_STYLE01_17_25']
for i,part in enumerate(choices):
 e=next(e for e in report['entries'] if e['id']==part)
 root=next(o for o in scene.objects if o.get('part_id')==part);root.location=((i//3)*1.3,(i%3-1)*1.1,.34)
 lod=next(o for o in root.children if o.get('lod_index')==0 or re.search(r'_LOD0(?:\.\d+)*$',o.name))
 for o in [root,lod]+list(lod.children_recursive):o.hide_render=False;o.hide_set(False)
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1400;scene.render.resolution_y=950;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Wheels studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.18,.2,.23,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,0));floor=bpy.context.object
mat=bpy.data.materials.new('Floor');mat.diffuse_color=(.06,.075,.09,1);floor.data.materials.append(mat)
target=Vector((.65,0,.35))
for name,pos,power,size in [('Key',(-3,-4,6),1400,5),('Edge',(3,2,5),1600,4),('Fill',(-4,3,3),1200,3)]:
 data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
 obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=pos
 obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Preview');cam=bpy.data.objects.new('Preview',data);scene.collection.objects.link(cam)
cam.location=(-4.8,-4.5,3.1);cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();data.lens=54
scene.camera=cam;scene.render.filepath=str(art/'wheels-preview.png')
bpy.ops.render.render(write_still=True)
