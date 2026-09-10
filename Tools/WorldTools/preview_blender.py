"""Run in Blender MCP after blender_map.py to save inspectable views and pack textures."""
import bpy
from mathutils import Vector,Quaternion
ROOT='/Users/tihan-nico/NFS MW Remaster/Art/RockportBuildings/'
scene=bpy.context.scene
for obj in scene.objects:obj.select_set(False)
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        s=area.spaces.active;s.overlay.show_overlays=False;s.region_3d.view_perspective='ORTHO';s.region_3d.view_rotation=Quaternion((1,0,0,0));s.region_3d.view_distance=6800;s.region_3d.view_location=Vector((1937,1775,150));s.shading.color_type='MATERIAL'
camdata=bpy.data.cameras.new('Map overview');cam=bpy.data.objects.new('Map overview',camdata);scene.collection.objects.link(cam);cam.location=(1937,1775,8000);camdata.type='ORTHO';camdata.ortho_scale=6500;camdata.clip_end=20000;scene.camera=cam
scene.world=bpy.data.worlds.new('Map overview world');scene.world.color=(.035,.035,.035)
scene.render.engine='BLENDER_WORKBENCH';shade=scene.display.shading;shade.light='STUDIO';shade.color_type='SINGLE';shade.single_color=(.65,.7,.78);shade.show_shadows=False;shade.show_cavity=True;shade.cavity_type='WORLD';shade.background_type='WORLD'
scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0
scene.render.resolution_x=1600;scene.render.resolution_y=1600;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=ROOT+'overview.png'
bpy.ops.render.render(write_still=True)
cam.location=(400,-950,1000);target=Vector((1135,-43,120));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();camdata.ortho_scale=1500;scene.render.filepath=ROOT+'downtown.png';scene.render.resolution_x=1800;scene.render.resolution_y=1100;shade.color_type='TEXTURE';shade.light='FLAT'
bpy.ops.render.render(write_still=True)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'RockportBuildings.blend')
print('Saved final overview, downtown inspection, and packed Blender file.')
