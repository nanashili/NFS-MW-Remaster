"""Schedule one independent car per Blender timer tick; resumable by per-car report."""
import bpy
import json
import traceback
import contextlib
import io
from pathlib import Path

ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
exec(compile((ROOT/'Tools/CarRemaster/blender_cars.py').read_text(),'blender_cars.py','exec'))
queue=[]
for p in sorted((ROOT/'Art/Cars/Prepared').glob('*/source.json')):
    report=ROOT/'Art/Cars/Remastered'/p.parent.name/'report.json'
    source=json.loads(p.read_text())
    if not source['assetFolder'].startswith('Assets/NfsMw/Content/Vehicles/Street/'):
        continue  # Shared accessories and special-object libraries use their own builder.
    has_offset=any(any(abs(source['solids'][n]['pivot'][i])>.0001 for i in (12,13,14)) for n in selected_solids(source,0))
    previous=json.loads(report.read_text()) if report.exists() else {}
    # Rebuild only missing or changed stock assemblies; source archive exports are immutable inputs.
    changed=(not previous or previous.get('stockParts')!=selected_solids(source,0)
             or (needs_wheel_face(source) and not previous.get('authoredWheelFace'))
             or previous.get('extraRearTireOffset',0)!=source['visual'].get('extraRearTireOffset',0)
             or (has_offset and not previous.get('sourcePivotPositionsApplied')))
    if changed:
        queue.append(p.parent.name)
batch_result={'remaining':queue.copy(),'completed':[],'errors':[]}

def next_car():
    if not queue:return None
    car=queue.pop(0)
    old_scene=bpy.context.scene
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            state,scene,root=build_car(car,replace_generated=True)
        batch_result['completed'].append(car)
        # Finished scenes live in individual .blend files; release their in-memory geometry.
        bpy.context.window.scene=old_scene
        for o in list(scene.objects):bpy.data.objects.remove(o,do_unlink=True)
        bpy.data.scenes.remove(scene)
    except Exception:
        batch_result['errors'].append({'car':car,'error':traceback.format_exc()})
    batch_result['remaining']=queue.copy()
    (ROOT/'Art/Cars/blender-batch-progress.json').write_text(json.dumps(batch_result,indent=2)+'\n')
    return .1 if queue else None

bpy.app.timers.register(next_car,first_interval=.5)
print('Scheduled',len(queue),'cars')
