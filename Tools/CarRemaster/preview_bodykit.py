"""Assemble one inspection-only A3 body kit/hood combination in Blender."""
import bpy,json,re
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');OUT=ROOT/'Art/Cars'
with bpy.data.libraries.load(str(OUT/'Remastered/A3/A3.blend')) as (src,dst):dst.scenes=[src.scenes[0]]
scene=dst.scenes[0];bpy.context.window.scene=scene
root=next(o for o in scene.objects if o.get('source_model')=='A3')
for o in scene.objects:
 if o.get('source_solid') in ('A3_KIT00_BODY_A','A3_KIT00_HOOD_A'):o.hide_render=True;o.hide_set(True)
with bpy.data.libraries.load(str(OUT/'Modifications/A3/A3.blend')) as (src,dst):dst.scenes=[src.scenes[0]]
parts_scene=dst.scenes[0]
for part_id in ('A3_KIT05_BODY','A3_STYLE03_HOOD'):
 part=next(o for o in parts_scene.objects if o.get('part_id')==part_id)
 part.location=root.location
 lod=next(o for o in part.children if o.get('lod_index')==0 or re.search(r'_LOD0(?:\.\d+)*$',o.name))
 for o in [part,lod]+list(lod.children_recursive):
  scene.collection.objects.link(o);o.hide_render=False;o.hide_set(False)
exec(compile((ROOT/'Tools/CarRemaster/preview.py').read_text(),'preview.py','exec'))
studio_preview('A3 Kit 05 and Style 03 Hood',output_path=str(OUT/'Modifications/bodykit-preview.png'))
