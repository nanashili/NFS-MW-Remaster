import bpy,bmesh,json
from mathutils import Vector
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');s=bpy.context.scene
assert s.name=='MW05 | Modern warehouse remake'
body=bpy.data.objects['Warehouse | original architecture'];bm=bmesh.new();bm.from_mesh(body.data)
old=[f for f in bm.faces if all(-6.31<v.co.x<-5.33 and -8.68<v.co.y<-6.85 and 4.22<v.co.z<6.28 for v in f.verts)]
bmesh.ops.delete(bm,geom=old,context='FACES');bm.to_mesh(body.data);bm.free()
bpy.ops.mesh.primitive_cube_add(size=1,location=(-5.8235,-7.7635,5.2445))
o=bpy.context.object;o.name='Tank 2 | folded steel duct';o.dimensions=(.949,1.799,2.039)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
for c in list(o.users_collection):c.objects.unlink(o)
bpy.data.collections['02 | Remake fittings'].objects.link(o)
o.data.materials.append(next(m for m in bpy.data.materials if m.get('sourceTextureName')=='QRACE_PIPE1'))
mod=o.modifiers.new('Rounded folded edges','BEVEL');mod.width=.065;mod.segments=3;bpy.ops.object.modifier_apply(modifier=mod.name)
mod=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');bpy.ops.object.modifier_apply(modifier=mod.name)
bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Strength'].default_value=.065
bpy.data.lights['Preview | warm courtyard key'].energy=3800
bpy.data.lights['Preview | cool open sky'].energy=3500
bpy.data.lights['Preview | roof rim'].energy=5200
s.view_settings.exposure=-.25
s.render.resolution_percentage=75;s.cycles.samples=48
s.render.filepath=str(ROOT/'Art/FrontendCourtyard/Remake/warehouse-remake-preview.png')
bpy.ops.object.select_all(action='DESELECT');bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Art/FrontendCourtyard/warehouse-modern-remake.blend'))
def render():bpy.ops.render.render(write_still=True);return None
bpy.app.timers.register(render,first_interval=.5)
print('Refined duct and lighting saved; preview queued')
