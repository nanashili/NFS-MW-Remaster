"""Bake portable image maps once in Blender; artistic colour comes from imagegen."""
import bpy,json
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');TEX=ROOT/'Art/FrontendRooms/SharedTextures'
scene=bpy.context.scene
def run():
    s=bpy.data.scenes.new('TEMP | Room material bake');bpy.context.window.scene=s
    s.render.engine='CYCLES';s.cycles.device='GPU';s.cycles.samples=1
    bpy.ops.mesh.primitive_plane_add(size=3);o=bpy.context.object
    m=bpy.data.materials.new('TEMP | bake');m.use_nodes=True;o.data.materials.append(m)
    n=m.node_tree.nodes;l=m.node_tree.links;n.clear()
    out=n.new('ShaderNodeOutputMaterial');p=n.new('ShaderNodeBsdfPrincipled');src=n.new('ShaderNodeTexImage');target=n.new('ShaderNodeTexImage')
    bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.18;bump.inputs['Distance'].default_value=.012
    l.new(src.outputs['Color'],bump.inputs['Height']);l.new(bump.outputs['Normal'],p.inputs['Normal']);l.new(p.outputs[0],out.inputs['Surface'])
    for key,size in [('concrete',(2048,2048)),('safehouse_wall',(2048,1024))]:
        src.image=bpy.data.images.load(str(TEX/(key+'_BaseColor_2K.png')),check_existing=True)
        im=bpy.data.images.new(key+'_Normal_2K',width=size[0],height=size[1],alpha=False);im.colorspace_settings.name='Non-Color'
        target.image=im;n.active=target;bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT',margin=0)
        im.filepath_raw=str(TEX/(key+'_Normal_2K.png'));im.file_format='PNG';im.save()
    uv=n.new('ShaderNodeTexCoord');noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=5;noise.inputs['Detail'].default_value=3;l.new(uv.outputs['UV'],noise.inputs['Vector'])
    ramp=n.new('ShaderNodeValToRGB');l.new(noise.outputs['Fac'],ramp.inputs['Fac'])
    c=n.new('ShaderNodeCombineColor');c.mode='RGB';c.inputs['Red'].default_value=1;c.inputs['Blue'].default_value=0;l.new(ramp.outputs[0],c.inputs['Green'])
    em=n.new('ShaderNodeEmission');l.new(c.outputs[0],em.inputs[0]);l.new(em.outputs[0],out.inputs['Surface'])
    params={'SafeHouse':(.5,.74),'Showroom':(.35,.62),'Performance':(.36,.63),'Visual':(.23,.47),'wall':(.65,.85)}
    for key,(lo,hi) in params.items():
        for e,v in zip(ramp.color_ramp.elements,[lo,hi]):e.color=(v,v,v,1)
        im=bpy.data.images.new(key+'_ORM_1K',width=1024,height=1024,alpha=False);im.colorspace_settings.name='Non-Color';target.image=im;n.active=target
        bpy.ops.object.bake(type='EMIT',margin=0);im.filepath_raw=str(TEX/(key+'_ORM_1K.png'));im.file_format='PNG';im.save()
    bpy.context.window.scene=scene;bpy.data.scenes.remove(s)
    print('ROOM_MAPS_COMPLETE');return None
bpy.app.timers.register(run,first_interval=.2)
