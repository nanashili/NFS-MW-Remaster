"""Bake portable game material maps in Blender; no procedural runtime shader."""
import bpy, json
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
DEST=ROOT/'Art/FrontendCourtyard/Remake'
TEX=DEST/'Textures'
scene=bpy.context.scene
assert scene.name=='MW05 | Modern warehouse remake'

def run():
    bake=bpy.data.scenes.new('TEMP | Remake material bake')
    bpy.context.window.scene=bake
    bake.render.engine='CYCLES';bake.cycles.device='GPU';bake.cycles.samples=1
    bpy.ops.mesh.primitive_plane_add(size=4)
    plane=bpy.context.object
    m=bpy.data.materials.new('TEMP | Remake maps');m.use_nodes=True
    plane.data.materials.append(m)
    n=m.node_tree.nodes;l=m.node_tree.links;n.clear()
    output=n.new('ShaderNodeOutputMaterial');p=n.new('ShaderNodeBsdfPrincipled')
    source=n.new('ShaderNodeTexImage');target=n.new('ShaderNodeTexImage')
    bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.22
    bump.inputs['Distance'].default_value=.022
    l.new(source.outputs['Color'],bump.inputs['Height']);l.new(bump.outputs['Normal'],p.inputs['Normal'])
    normals={}
    for key in ['asphalt','cladding']:
        source.image=bpy.data.images.load(str(TEX/(key+'_BaseColor_2K.png')),check_existing=True)
        im=bpy.data.images.new(key+'_Normal_2K',width=2048,height=2048,alpha=False)
        im.colorspace_settings.name='Non-Color';target.image=im;n.active=target
        l.new(p.outputs['BSDF'],output.inputs['Surface'])
        bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT',margin=0,use_clear=True)
        im.filepath_raw=str(TEX/(key+'_Normal_2K.png'));im.file_format='PNG';im.save()
        normals[key]=im
    uv=n.new('ShaderNodeTexCoord');noise=n.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value=4;noise.inputs['Detail'].default_value=3
    l.new(uv.outputs['UV'],noise.inputs['Vector'])
    ramp=n.new('ShaderNodeValToRGB');l.new(noise.outputs['Fac'],ramp.inputs['Fac'])
    combine=n.new('ShaderNodeCombineColor');combine.mode='RGB';combine.inputs['Red'].default_value=1
    l.new(ramp.outputs['Color'],combine.inputs['Green'])
    emission=n.new('ShaderNodeEmission');l.new(combine.outputs['Color'],emission.inputs['Color'])
    l.new(emission.outputs[0],output.inputs['Surface'])
    orm={}
    params={'asphalt':(.32,.78,0),'cladding':(.42,.72,.18),'tank':(.37,.58,.82),'pipe':(.35,.56,.78)}
    for key,(low,high,metal) in params.items():
        ramp.color_ramp.elements[0].position=.34;ramp.color_ramp.elements[0].color=(low,low,low,1)
        ramp.color_ramp.elements[1].position=.66;ramp.color_ramp.elements[1].color=(high,high,high,1)
        combine.inputs['Blue'].default_value=metal
        im=bpy.data.images.new(key+'_ORM_1K',width=1024,height=1024,alpha=False)
        im.colorspace_settings.name='Non-Color';target.image=im;n.active=target
        bpy.ops.object.bake(type='EMIT',margin=0,use_clear=True)
        im.filepath_raw=str(TEX/(key+'_ORM_1K.png'));im.file_format='PNG';im.save();orm[key]=im
    bpy.context.window.scene=scene;bpy.data.scenes.remove(bake)
    sources={'QRACE_FLOOR1':'asphalt','QRACE_SIDING1':'cladding','QRACE_WATERTOWER':'tank','QRACE_PIPE1':'pipe'}
    for mat in list(bpy.data.materials):
        key=sources.get(mat.get('sourceTextureName'))
        if not key:continue
        nodes=mat.node_tree.nodes;links=mat.node_tree.links
        normal=next((x.image for x in nodes if x.type=='TEX_IMAGE' and x.image and 'Normal' in x.image.name),None)
        nodes.clear();bsdf=nodes.new('ShaderNodeBsdfPrincipled');out=nodes.new('ShaderNodeOutputMaterial')
        links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
        if key in normals:
            albedo=nodes.new('ShaderNodeTexImage');albedo.image=bpy.data.images.load(str(TEX/(key+'_BaseColor_2K.png')),check_existing=True)
            links.new(albedo.outputs['Color'],bsdf.inputs['Base Color']);normal=normals[key]
        else:
            bsdf.inputs['Base Color'].default_value=(.20,.23,.24,1) if key=='tank' else (.11,.14,.15,1)
        if normal:
            nt=nodes.new('ShaderNodeTexImage');nt.image=normal;nm=nodes.new('ShaderNodeNormalMap')
            nm.inputs['Strength'].default_value=.22 if key in ('tank','pipe') else .8
            links.new(nt.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bsdf.inputs['Normal'])
        rt=nodes.new('ShaderNodeTexImage');rt.image=orm[key];sep=nodes.new('ShaderNodeSeparateColor');sep.mode='RGB'
        links.new(rt.outputs['Color'],sep.inputs['Color']);links.new(sep.outputs['Green'],bsdf.inputs['Roughness']);links.new(sep.outputs['Blue'],bsdf.inputs['Metallic'])
        mat['remake_material']='Portable baked 2K colour/normal and 1K packed roughness-metallic; no runtime procedures'
    for o in scene.objects:
        if o.type!='MESH':continue
        uv=o.data.uv_layers.active
        if not uv:continue
        for face in o.data.polygons:
            key=o.data.materials[face.material_index].get('sourceTextureName')
            if key not in ('QRACE_FLOOR1','QRACE_SIDING1'):continue
            for li in face.loop_indices:
                v=o.matrix_world@o.data.vertices[o.data.loops[li].vertex_index].co
                if key=='QRACE_FLOOR1':uv.data[li].uv=(v.x/4,v.y/4)
                elif abs(face.normal.x)>.7:uv.data[li].uv=(v.y/2.4,v.z/3)
                elif abs(face.normal.y)>.7:uv.data[li].uv=(v.x/2.4,v.z/3)
                else:uv.data[li].uv=(v.x/2.4,v.y/3)
    bpy.data.orphans_purge(do_recursive=True);bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Art/FrontendCourtyard/warehouse-modern-remake.blend'))
    (DEST/'material-report.json').write_text(json.dumps({'materials':list(sources),'colourNormals':2048,'roughnessMetallic':1024,'method':'AI generated asphalt/cladding; native Cycles inferred normal and authored roughness-metallic bakes'},indent=2))
    print('REMAKE_MATERIALS_COMPLETE')
    return None
bpy.app.timers.register(run,first_interval=.5)
print('Queued remake material bakes')
