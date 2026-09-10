import bpy
from mathutils import Vector
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');s=bpy.context.scene
pipe=next(m for m in bpy.data.materials if m.get('sourceTextureName')=='QRACE_PIPE1')
for name,a,b,r in [
    ('Main inlet',(.174,-7.989,7.323),(-.96,-7.989,7.323),.3953),
    ('Branch inlet',(-2.017,-7.989,7.323),(-.90,-7.989,7.323),.3928),
]:
    a=Vector(a);b=Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=32,radius=r,depth=(b-a).length,location=(a+b)/2)
    o=bpy.context.object;o.name='Tank 1 | '+name;o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    o.data.materials.append(pipe)
    for p in o.data.polygons:p.use_smooth=len(p.vertices)==4
    for c in list(o.users_collection):c.objects.unlink(o)
    bpy.data.collections['02 | Remake fittings'].objects.link(o)
s.render.resolution_percentage=100;s.cycles.samples=96
s.render.filepath=str(ROOT/'Art/FrontendCourtyard/Remake/warehouse-remake-preview.png')
bpy.ops.object.select_all(action='DESELECT');bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Art/FrontendCourtyard/warehouse-modern-remake.blend'))
print('Tank pipe connections completed; final resolution configured')
