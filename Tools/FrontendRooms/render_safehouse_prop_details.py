"""Native close-up renders; leave the saved authoring camera intact."""
import bpy
from pathlib import Path
from mathutils import Vector
s=bpy.context.scene;ART=Path('/Users/tihan-nico/NFS MW Remaster/Art/FrontendRooms/SafeHouse')
camera=s.camera;matrix=camera.matrix_world.copy();lens=camera.data.lens
def render():
    s.render.resolution_percentage=100;s.cycles.samples=64
    for name,pos,target,l in [('tires-detail',(6,-.1,1.8),(10,-2,.65),44),('props-reverse-view',(4,-4,1.8),(-3,9,1.8),23),('barrels-detail',(7.5,7.6,1.6),(10.5,5.7,.6),43),('sofas-detail',(-3.5,3.6,1.8),(-7.5,9,.65),38),('cylinders-detail',(3.9,4.1,1.5),(1.15,8.18,.85),43)]:
        camera.location=pos;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.lens=l;s.render.filepath=str(ART/(name+'.png'));bpy.ops.render.render(write_still=True)
    camera.matrix_world=matrix;camera.data.lens=lens;s.cycles.samples=80;s.render.filepath=str(ART/'remake-preview.png');return None
bpy.app.timers.register(render,first_interval=.2)
