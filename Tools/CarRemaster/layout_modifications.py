"""Present editable libraries on a grid; Unity GLBs keep their assembly origins."""
import bpy,json,math,traceback
from pathlib import Path
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');OUT=ROOT/'Art/Cars';MOD=OUT/'Modifications'
UNITY_SOURCES=ROOT/'Assets/NfsMw/Content/Customization/Sources'
UNITY_PARTS=ROOT/'Assets/NfsMw/Content/Customization/Parts'
CATEGORY_FOLDERS={'Brake':'Brake','Body kit':'BodyKit','Damage state':'DamageState','Decal mesh':'DecalMesh',
                  'Glass':'Glass','Hood':'Hood','Interior':'Interior','Light':'Light','Mirror':'Mirror',
                  'Other source part':'OtherSourcePart','Plate':'Plate','Rim':'Rim','Roof scoop':'RoofScoop',
                  'Spoiler':'Spoiler','Tyre':'Tyre','Wheel and tyre':'WheelAndTyre'}

def schedule_layout():
    queue=[p.parent.name for p in sorted(MOD.glob('*/report.json'))]
    progress={'remaining':queue.copy(),'completed':[],'errors':[]}
    def tick():
        if not queue:return None
        archive=queue.pop(0);previous=bpy.context.scene;scene=None
        try:
            source=json.loads((OUT/'Prepared'/archive/'source.json').read_text())
            path=MOD/archive/(archive+'.blend')
            with bpy.data.libraries.load(str(path)) as (src,dst):dst.scenes=[src.scenes[0]]
            scene=dst.scenes[0];bpy.context.window.scene=scene
            roots=sorted((o for o in scene.objects if 'part_id' in o),key=lambda o:o['part_id'])
            spans=[max(hi-lo for lo,hi in zip(s['boundsMin'],s['boundsMax'])) for s in source['solids'].values() if all(hi>=lo for lo,hi in zip(s['boundsMin'],s['boundsMax']))]
            spacing=max(spans,default=1)+.5
            columns=math.ceil(math.sqrt(len(roots)))
            for i,root in enumerate(roots):
                root['assembly_position']=[0.0,0.0,0.0]
                root['library_grid_position']=[(i%columns)*spacing,(i//columns)*spacing,0.0]
                root.location=root['library_grid_position'];root.name=root['part_id']
            scene['library_layout']='Display grid only. Restore each part root location to its assembly_position property before assembly export.'
            report=json.loads((MOD/archive/'report.json').read_text())
            for entry in report['entries']:
                if entry['category']=='Brake' and 'BRAKELIGHT' in entry['id']:
                    entry['category']='Glass' if 'GLASS' in entry['id'] else 'Light'
                entry['prefab']=str((UNITY_PARTS/CATEGORY_FOLDERS[entry['category']]/archive/(entry['id']+'.prefab')).relative_to(ROOT))
            report['blenderLayout']='Grid; each root stores assembly_position and library_grid_position'
            report['sourceVisualAttributes']=source.get('visual');report['sourceChassis']=source.get('chassis')
            report['asset']=str((UNITY_SOURCES/archive/'Geometry/Parts.glb').relative_to(ROOT))
            report['sourceCatalog']=str((UNITY_SOURCES/archive/'Metadata/catalog.json').relative_to(ROOT))
            report['layout']='content-first-v2'
            unity_catalog=UNITY_SOURCES/archive/'Metadata/catalog.json';unity_catalog.parent.mkdir(parents=True,exist_ok=True)
            for target in [MOD/archive/'report.json',unity_catalog]:
                target.write_text(json.dumps(report,indent=2)+'\n')
            bpy.data.libraries.write(str(path),{scene},path_remap='RELATIVE_ALL',compress=True)
            progress['completed'].append(archive)
        except Exception:progress['errors'].append({'archive':archive,'error':traceback.format_exc()})
        finally:
            bpy.context.window.scene=previous
            if scene:bpy.data.batch_remove(ids=tuple(scene.objects)+(scene,))
            for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
                unused=tuple(x for x in collection if x.users==0)
                if unused:bpy.data.batch_remove(ids=unused)
        progress['remaining']=queue.copy();(MOD/'layout-progress.json').write_text(json.dumps(progress,indent=2)+'\n')
        return .1 if queue else None
    bpy.app.timers.register(tick,first_interval=.5);print('Scheduled',len(queue),'library layouts')
