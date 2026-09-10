"""Blender material preparation; normal detail is baked, never runtime displacement."""
import bpy
import json
from pathlib import Path

ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
ART=ROOT/'Art/FrontendCourtyard'
TEXTURES=ART/'Updated/Textures'
scene=bpy.context.scene
assert scene.name=='MW05 | Unity warehouse updated'
spec={
    'pipe':('QRACE_PIPE1',.72,.43,.12,.008),
    'siding':('QRACE_SIDING1',.28,.75,.20,.025),
    'floor':('QRACE_FLOOR1',0,.85,.20,.065),
    'tank':('QRACE_WATERTOWER',.72,.46,.12,.008),
    'concrete':('QRACE_CONCRETE1',0,.83,.16,.035),
}

def bake_and_apply():
    original_scene=bpy.context.window.scene
    bake=bpy.data.scenes.new('TEMP | normal detail bake')
    bpy.context.window.scene=bake
    bake.render.engine='CYCLES'
    bake.cycles.device='GPU'
    bake.cycles.samples=1
    bpy.ops.mesh.primitive_plane_add(size=4)
    plane=bpy.context.object
    material=bpy.data.materials.new('TEMP | detail bake')
    material.use_nodes=True
    plane.data.materials.append(material)
    nodes=material.node_tree.nodes;links=material.node_tree.links
    bsdf=nodes.get('Principled BSDF')
    source=nodes.new('ShaderNodeTexImage')
    bump=nodes.new('ShaderNodeBump')
    links.new(source.outputs['Color'],bump.inputs['Height'])
    links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
    target=nodes.new('ShaderNodeTexImage')
    maps={}
    for key,(source_name,metal,rough,strength,distance) in spec.items():
        albedo=bpy.data.images.load(str(TEXTURES/(key+'_BaseColor_2K.png')),check_existing=True)
        albedo.colorspace_settings.name='sRGB'
        source.image=albedo
        bump.inputs['Strength'].default_value=strength
        bump.inputs['Distance'].default_value=distance
        output=bpy.data.images.new(key+'_Normal_2K',width=albedo.size[0],height=albedo.size[1],alpha=False)
        output.colorspace_settings.name='Non-Color'
        target.image=output
        nodes.active=target
        bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT',margin=0,use_clear=True)
        output.filepath_raw=str(TEXTURES/(key+'_Normal_2K.png'))
        output.file_format='PNG';output.save()
        maps[key]=(albedo,output)
    bpy.context.window.scene=original_scene
    bpy.data.scenes.remove(bake)
    # All other legacy materials stay small; fix their physically implausible emission.
    updated=[]
    for m in list(bpy.data.materials):
        if not m.use_nodes or m.name.startswith('TEMP'):continue
        bsdf=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
        if bsdf:
            bsdf.inputs['Emission Strength'].default_value=0
        match=next(((k,v) for k,v in spec.items() if m.get('sourceTextureName')==v[0]),None)
        if not match:continue
        key,(_,metal,rough,_,_)=match
        albedo,normal=maps[key]
        m.node_tree.nodes.clear()
        nodes=m.node_tree.nodes;links=m.node_tree.links
        bsdf=nodes.new('ShaderNodeBsdfPrincipled')
        bsdf.inputs['Metallic'].default_value=metal
        bsdf.inputs['Roughness'].default_value=rough
        tex=nodes.new('ShaderNodeTexImage');tex.image=albedo
        ntex=nodes.new('ShaderNodeTexImage');ntex.image=normal
        normal_node=nodes.new('ShaderNodeNormalMap')
        links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
        links.new(ntex.outputs['Color'],normal_node.inputs['Color'])
        links.new(normal_node.outputs['Normal'],bsdf.inputs['Normal'])
        output=nodes.new('ShaderNodeOutputMaterial')
        links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
        m['texture_upgrade']='AI-assisted source-layout reconstruction; final 2K power-of-two texture. Detail normal baked from albedo, not a measured scan.'
        m.diffuse_color=(.5,.5,.5,1)
        updated.append(m.name)
    bpy.data.orphans_purge(do_recursive=True)
    bpy.ops.file.pack_all()
    scene.render.filepath=str(ART/'Updated/warehouse-updated-preview.png')
    scene.render.resolution_percentage=75
    scene.cycles.samples=24
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/'warehouse-unity-updated.blend'))
    (ART/'Updated/material-report.json').write_text(json.dumps({'updated_materials':updated,'new_texture_sets':list(spec),'normal_method':'Cycles tangent normal bake from low-strength albedo-derived bump; no displacement'},indent=2))
    bpy.ops.render.render(write_still=True)
    return None

bpy.app.timers.register(bake_and_apply,first_interval=.5)
print('Five 2K normal bakes, material update and preview queued')
