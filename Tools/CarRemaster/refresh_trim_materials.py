"""Repair grille/metal source slots without altering unrelated materials or geometry."""
from pathlib import Path
import bpy,json,traceback
ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
exec(compile((ROOT/'Tools/CarRemaster/blender_cars.py').read_text(),'blender_cars.py','exec'))
MOD=OUT/'Modifications'

def schedule_trim():
    queue=[]
    for path in sorted(MOD.glob('*/report.json')):
        report=json.loads(path.read_text());source=json.loads((OUT/'Prepared'/path.parent.name/'source.json').read_text())
        if not report.get('sourceTrimMaterialsApplied') and any('GRILL' in k or 'ALUMIN' in k for k in source['materials']):queue.append(path.parent.name)
    progress={'remaining':queue.copy(),'completed':[],'errors':[]}
    def tick():
        if not queue:return None
        archive=queue.pop(0);previous=bpy.context.scene;scene=None
        try:
            source=json.loads((OUT/'Prepared'/archive/'source.json').read_text())
            state={'car':archive,'source':source,'materials':{},'paint':PALETTE[0]}
            path=MOD/archive/(archive+'.blend')
            with bpy.data.libraries.load(str(path)) as (src,dst):dst.scenes=[src.scenes[0]]
            scene=dst.scenes[0];bpy.context.window.scene=scene
            slots={};changed=0
            for obj in scene.objects:
                solid=obj.get('source_solid')
                if obj.type!='MESH' or not solid:continue
                if solid not in slots:
                    names=[]
                    for line in (OUT/'Models'/archive/'GEOMETRY/models'/('DEFAULT-GEOMETRY.BIN-'+solid+'.obj')).read_text().splitlines():
                        if line.startswith('usemtl ') and line[7:] not in names:names.append(line[7:])
                    slots[solid]=names
                names=slots[solid]
                if not any('GRILL' in slot or 'ALUMIN' in slot for slot in names):continue
                assert len(obj.data.materials)>=len(names)
                for i,slot in enumerate(names):
                    if 'GRILL' in slot or 'ALUMIN' in slot:
                        obj.data.materials[i]=source_material(state,solid,slot);changed+=1
            for m in state['materials'].values():
                for node in m.node_tree.nodes:
                    if node.type=='TEX_IMAGE' and node.image and not node.image.packed_file:node.image.pack()
            bpy.data.libraries.write(str(path),{scene},path_remap='RELATIVE_ALL',compress=True)
            report=json.loads((MOD/archive/'report.json').read_text())
            report['sourceTrimMaterialsApplied']=True;report['correctedTrimSlots']=changed
            report['singleSceneExport']=False
            (MOD/archive/'report.json').write_text(json.dumps(report,indent=2)+'\n')
            progress['completed'].append({'archive':archive,'slots':changed})
        except Exception:progress['errors'].append({'archive':archive,'error':traceback.format_exc()})
        finally:
            bpy.context.window.scene=previous
            if scene:bpy.data.batch_remove(ids=tuple(scene.objects)+(scene,))
            for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
                unused=tuple(x for x in collection if x.users==0)
                if unused:bpy.data.batch_remove(ids=unused)
        progress['remaining']=queue.copy();(MOD/'trim-progress.json').write_text(json.dumps(progress,indent=2)+'\n')
        return .1 if queue else None
    bpy.app.timers.register(tick,first_interval=.5);print('Scheduled',len(queue),'grille/trim material corrections')
