"""Export each saved library as exactly one scene, restoring assembly origins."""
import bpy,json,struct,traceback,contextlib,io,re
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');MOD=ROOT/'Art/Cars/Modifications'
UNITY_SOURCES=ROOT/'Assets/NfsMw/Content/Customization/Sources'
UNITY_PARTS=ROOT/'Assets/NfsMw/Content/Customization/Parts'
CATEGORY_FOLDERS={'Brake':'Brake','Body kit':'BodyKit','Damage state':'DamageState','Decal mesh':'DecalMesh',
                  'Glass':'Glass','Hood':'Hood','Interior':'Interior','Light':'Light','Mirror':'Mirror',
                  'Other source part':'OtherSourcePart','Plate':'Plate','Rim':'Rim','Roof scoop':'RoofScoop',
                  'Spoiler':'Spoiler','Tyre':'Tyre','Wheel and tyre':'WheelAndTyre'}

def schedule_exports():
    queue=[p.parent.name for p in sorted(MOD.glob('*/report.json')) if not json.loads(p.read_text()).get('singleSceneExport')]
    progress={'remaining':queue.copy(),'completed':[],'errors':[]}
    def tick():
        if not queue:return None
        archive=queue.pop(0);previous=bpy.context.scene;scene=None
        try:
            path=MOD/archive/(archive+'.blend');report=json.loads((MOD/archive/'report.json').read_text())
            with bpy.data.libraries.load(str(path)) as (src,dst):dst.scenes=[src.scenes[0]]
            scene=dst.scenes[0];bpy.context.window.scene=scene
            roots={o['part_id']:o for o in scene.objects if 'part_id' in o}
            for entry in report['entries']:
                root=roots[entry['id']];root.location=root.get('assembly_position',(0,0,0));entry['node']=root.name
                for index,lod in enumerate(entry['lods']):
                    group=next(o for o in root.children if re.search('_LOD'+str(index)+r'(?:\.\d+)*$',o.name))
                    group['lod_index']=index
                    lod['node']=group.name
            for o in scene.objects:o.hide_set(False);o.hide_render=False;o.select_set(True)
            dest=UNITY_SOURCES/archive/'Geometry/Parts.glb';dest.parent.mkdir(parents=True,exist_ok=True)
            with contextlib.redirect_stdout(io.StringIO()):
                bpy.ops.export_scene.gltf(filepath=str(dest),export_format='GLB',use_selection=True,use_active_scene=True,
                    export_apply=True,export_extras=True,export_cameras=False,export_lights=False,export_yup=True)
            raw=dest.read_bytes();n=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+n])
            assert len(g['scenes'])==1 and len(g['scenes'][0]['nodes'])==len(report['entries'])
            nodes={n['name']:i for i,n in enumerate(g['nodes'])}
            def triangles(i):
                node=g['nodes'][i]
                own=sum(g['accessors'][p['indices']]['count']//3 for p in g['meshes'][node['mesh']]['primitives']) if 'mesh' in node else 0
                return own+sum(triangles(c) for c in node.get('children',[]))
            for entry in report['entries']:
                root=roots[entry['id']];root.location=root['library_grid_position']
                for i,lod in enumerate(entry['lods']):
                    lod['triangles']=triangles(nodes[lod['node']]);assert lod['triangles']>0
                    if i:
                        group=next(o for o in root.children if o.name==lod['node'])
                        for o in [group]+list(group.children_recursive):o.hide_set(True);o.hide_render=True
            report['singleSceneExport']=True
            report['asset']=str(dest.relative_to(ROOT))
            report['sourceCatalog']=str((UNITY_SOURCES/archive/'Metadata/catalog.json').relative_to(ROOT))
            report['layout']='content-first-v2'
            for entry in report['entries']:
                entry['prefab']=str((UNITY_PARTS/CATEGORY_FOLDERS[entry['category']]/archive/(entry['id']+'.prefab')).relative_to(ROOT))
            unity_catalog=UNITY_SOURCES/archive/'Metadata/catalog.json';unity_catalog.parent.mkdir(parents=True,exist_ok=True)
            for out in [MOD/archive/'report.json',unity_catalog]:out.write_text(json.dumps(report,indent=2)+'\n')
            bpy.data.libraries.write(str(path),{scene},path_remap='RELATIVE_ALL',compress=True)
            progress['completed'].append(archive)
        except Exception:progress['errors'].append({'archive':archive,'error':traceback.format_exc()})
        finally:
            bpy.context.window.scene=previous
            if scene:bpy.data.batch_remove(ids=tuple(scene.objects)+(scene,))
            for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
                unused=tuple(x for x in collection if x.users==0)
                if unused:bpy.data.batch_remove(ids=unused)
        progress['remaining']=queue.copy();(MOD/'reexport-progress.json').write_text(json.dumps(progress,indent=2)+'\n')
        return .1 if queue else None
    bpy.app.timers.register(tick,first_interval=.5);print('Scheduled',len(queue),'single-scene exports')
