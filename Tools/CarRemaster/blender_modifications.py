"""Build modular source parts in Blender; one shared-texture GLB per archive."""
from pathlib import Path
import json, math, re, hashlib, traceback, contextlib, io, struct
ROOT=Path('/Users/tihan-nico/NFS MW Remaster')
exec(compile((ROOT/'Tools/CarRemaster/blender_cars.py').read_text(),'blender_cars.py','exec'))
MOD=OUT/'Modifications'
UNITY_SOURCES=ROOT/'Assets/NfsMw/Content/Customization/Sources'
UNITY_PARTS=ROOT/'Assets/NfsMw/Content/Customization/Parts'
CATEGORY_FOLDERS={'Brake':'Brake','Body kit':'BodyKit','Damage state':'DamageState','Decal mesh':'DecalMesh',
                  'Glass':'Glass','Hood':'Hood','Interior':'Interior','Light':'Light','Mirror':'Mirror',
                  'Other source part':'OtherSourcePart','Plate':'Plate','Rim':'Rim','Roof scoop':'RoofScoop',
                  'Spoiler':'Spoiler','Tyre':'Tyre','Wheel and tyre':'WheelAndTyre'}
_original_source_material=source_material

def source_material(state,solid,slot):
    lower=slot.lower()
    if lower.startswith('disc') or lower=='plain_aluminum':
        key=('authored metal',slot)
        if key not in state['materials']:
            state['materials'][key]=material(state['car']+'_'+slot,(.32,.35,.38,1),.90,.34)
        return state['materials'][key]
    return _original_source_material(state,solid,slot)

def category(archive,name):
    if archive=='WHEELS' or '_TIRE' in name:return 'Wheel and tyre'
    if archive.startswith('SPOILER'):return 'Spoiler'
    if archive=='ROOF':return 'Roof scoop'
    if 'DECAL' in name:return 'Decal mesh'
    if 'DAMAGE' in name:return 'Damage state'
    if 'HOOD' in name:return 'Hood'
    if re.search(r'(^|_)BRAKE(_|$)',name):return 'Brake'
    if 'WINDOW' in name or 'GLASS' in name:return 'Glass'
    if 'LIGHT' in name:return 'Light'
    if 'MIRROR' in name:return 'Mirror'
    if 'INTERIOR' in name or 'DRIVER' in name:return 'Interior'
    if 'LICENSE' in name:return 'Plate'
    if re.search(r'_KIT\d+_BODY$',name):return 'Body kit'
    return 'Other source part'

def tyre(state,parent,r,w,inner,segments):
    mat=state['hardware']['rubber']
    shoulder=max(inner+.008,r-.035)
    profile=[(-.49*w,inner),(-.515*w,(inner+shoulder)/2),(-.50*w,shoulder),(-.4*w,r-.004),(-.33*w,r)]
    for u in [-.23,0,.23]:profile.extend([(u*w-.004,r),(u*w-.002,r-.005),(u*w+.002,r-.005),(u*w+.004,r)])
    profile.extend([(.33*w,r),(.4*w,r-.004),(.5*w,shoulder),(.515*w,(inner+shoulder)/2),(.49*w,inner)])
    lathe('Grooved performance tyre',profile,mat,parent,segments,True)
    if segments>=48:
        for sign in [-1,1]:
            lathe('Sidewall bead',[(sign*.507*w,shoulder-.004),(sign*.51*w,shoulder-.002),(sign*.507*w,shoulder)],mat,parent,segments)

def consolidate(parent):
    parts=[o for o in parent.children if o.type=='MESH']
    if len(parts)<2:return
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join()

def build_parts(archive):
    source=json.loads((OUT/'Prepared'/archive/'source.json').read_text())
    scene=bpy.data.scenes.new(archive+' Modification Library');bpy.context.window.scene=scene
    scene.unit_settings.system='METRIC'
    state={'car':archive,'source':source,'materials':{},'paint':PALETTE[int(hash_name(archive),16)%len(PALETTE)]}
    state['hardware']={'rubber':material(archive+'_PerformanceRubber',(.018,.021,.025,1),0,.78)}
    groups={}
    for solid in source['solids']:groups.setdefault(solid[:-2],[]).append(solid)
    entries=[];placeholders=[]
    for base,names in sorted(groups.items()):
        names=sorted(names);top=names[0];cat=category(archive,base)
        top_path=OUT/'Models'/archive/'GEOMETRY/models'/('DEFAULT-GEOMETRY.BIN-'+top+'.obj')
        if not any(line.startswith('f ') for line in top_path.open()):
            placeholders.append({'id':base,'sourceSolids':names,'reason':'Original source contains no faces','sourceMarkers':source['solids'][top]['markers']})
            continue
        wheel=cat=='Wheel and tyre'
        variants=['Assembly','Tyre only'] if wheel else ['Source part']
        rim_slots=[k for k in source['materials'] if k.startswith(top+'/RIM_')]
        if wheel and rim_slots:variants.append('Rim only')
        wheel_dimensions=None
        for variant in variants:
            index=len(entries);node='PART_'+str(index).zfill(4)
            root=empty(node);root.rotation_euler.z=-math.pi/2
            root['part_id']=base+('' if variant in ('Assembly','Source part') else ('_TYRE_ONLY' if variant=='Tyre only' else '_RIM_ONLY'))
            root['source_archive']=archive
            radii=wheel_dimensions
            lod_entries=[]
            for lod in range(3):
                group=empty(node+'_LOD'+str(lod),root)
                group['lod_index']=lod
                solid=names[min(lod,len(names)-1)]
                geometry_lod=0 if lod==0 else (2 if lod==2 and len(names)<3 else 1)
                if wheel:
                    bounds=source['solids'][top];lo=bounds['boundsMin'];hi=bounds['boundsMax']
                    r=max(hi[0]-lo[0],hi[2]-lo[2])/2;w=hi[1]-lo[1];cy=(hi[1]+lo[1])/2
                    if not (r>0 and w>0):raise ValueError('Invalid wheel bounds '+top)
                    obj=None
                    if variant!='Tyre only':
                        filter_rim=variant=='Rim only' or (lod==0 and bool(rim_slots))
                        obj=read_obj(state,solid,geometry_lod,group,slots=(lambda n:n.startswith('RIM_')) if filter_rim else None)
                        if obj is None and variant=='Rim only':obj=read_obj(state,top,2 if lod==2 else 1,group,slots=lambda n:n.startswith('RIM_'))
                        if obj:obj.location.y=-cy
                    if radii is None:
                        rimr=max((math.hypot(v.co.x,v.co.z) for v in obj.data.vertices),default=.72*r) if obj and rim_slots else .72*r
                        radii={'outerRadius':r,'rimRadius':min(rimr,.92*r),'width':w,'sourceCenterY':cy}
                        wheel_dimensions=radii
                    if variant=='Tyre only' or (lod==0 and rim_slots and variant=='Assembly'):
                        tyre(state,group,r,w,radii['rimRadius'],[96,48,24][lod])
                        consolidate(group)
                    # Painted traffic rims retain their recovered wheel face in assemblies.
                else:
                    obj=read_obj(state,solid,geometry_lod,group)
                    if obj and lod==0 and cat in ('Roof scoop','Spoiler','Brake','Plate') and not obj.modifiers:
                        bevel=obj.modifiers.new('Edge highlights','BEVEL');bevel.width=.001;bevel.segments=2;bevel.limit_method='ANGLE'
                bpy.context.view_layer.update()
                deps=bpy.context.evaluated_depsgraph_get()
                meshes=[o for o in group.children_recursive if o.type=='MESH']
                tris=sum(sum(len(p.vertices)-2 for p in o.evaluated_get(deps).data.polygons) for o in meshes)
                if tris==0:raise ValueError('Empty part '+root['part_id']+' LOD'+str(lod))
                lod_entries.append({'node':group.name,'sourceSolid':solid,'triangles':tris})
            kit=re.search(r'_KIT(\d+)_',base)
            entries.append({'id':root['part_id'],'node':root.name,'category':('Tyre' if variant=='Tyre only' else 'Rim') if variant in ('Tyre only','Rim only') else cat,
                            'variant':variant,'archive':archive,'kit':kit.group(1) if kit else '',
                            'compatibility':archive if archive not in ('WHEELS','BRAKES','ROOF','PLATES') and not archive.startswith('SPOILER') else 'Shared library; vehicle fit not verified',
                            'sourceSolids':names,'sourceMarkers':source['solids'][top]['markers'],'wheelDimensions':radii,'lods':lod_entries})
    dest=UNITY_SOURCES/archive/'Geometry';dest.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    with contextlib.redirect_stdout(io.StringIO()):
        bpy.ops.export_scene.gltf(filepath=str(dest/'Parts.glb'),export_format='GLB',use_selection=True,use_active_scene=True,export_apply=True,
                                  export_extras=True,export_cameras=False,export_lights=False,export_yup=True)
    # Polygon estimates can differ after export; serialized indices are authoritative.
    raw=(dest/'Parts.glb').read_bytes();length=struct.unpack_from('<I',raw,12)[0]
    gltf=json.loads(raw[20:20+length]);node_indices={n['name']:i for i,n in enumerate(gltf['nodes'])}
    def exported_triangles(index):
        node=gltf['nodes'][index]
        own=sum(gltf['accessors'][p['indices']]['count']//3 for p in gltf['meshes'][node['mesh']]['primitives']) if 'mesh' in node else 0
        return own+sum(exported_triangles(i) for i in node.get('children',[]))
    for entry in entries:
        for lod in entry['lods']:
            lod['blenderTriangles']=lod['triangles']
            lod['triangles']=exported_triangles(node_indices[lod['node']])
            if lod['triangles']<=0:raise ValueError('Empty exported LOD '+lod['node'])
    for entry in entries:
        folder=CATEGORY_FOLDERS[entry['category']]
        entry['prefab']=str((UNITY_PARTS/folder/archive/(entry['id']+'.prefab')).relative_to(ROOT))
    art=MOD/archive;art.mkdir(parents=True,exist_ok=True)
    for m in state['materials'].values():
        for n in m.node_tree.nodes:
            if n.type=='TEX_IMAGE' and n.image and not n.image.packed_file:n.image.pack()
    for entry in entries:
        for lod in entry['lods'][1:]:
            group=bpy.data.objects[lod['node']]
            for o in [group]+list(group.children_recursive):o.hide_set(True);o.hide_render=True
    bpy.data.libraries.write(str(art/(archive+'.blend')),{scene},path_remap='RELATIVE_ALL',compress=True)
    geometry_asset=dest/'Parts.glb'
    unity_catalog=UNITY_SOURCES/archive/'Metadata/catalog.json'
    report={'archive':archive,'sourceSha256':source['sourceSha256'],'sourceSolidCount':len(source['solids']),
            'sourceVisualAttributes':source.get('visual'),'sourceChassis':source.get('chassis'),
            'asset':str(geometry_asset.relative_to(ROOT)),'sourceCatalog':str(unity_catalog.relative_to(ROOT)),
            'layout':'content-first-v2','entries':entries,'placeholders':placeholders}
    unity_catalog.parent.mkdir(parents=True,exist_ok=True)
    unity_catalog.write_text(json.dumps(report,indent=2)+'\n')
    (art/'report.json').write_text(json.dumps(report,indent=2)+'\n')
    return scene,report

def schedule_parts(only=None):
    queue=[p.parent.name for p in sorted((OUT/'Prepared').glob('*/source.json')) if not only or p.parent.name in only]
    queue=[a for a in queue if not (MOD/a/'report.json').exists()]
    progress={'remaining':queue.copy(),'completed':[],'errors':[]}
    def tick():
        if not queue:return None
        archive=queue.pop(0);old=bpy.context.scene;scene=None
        try:
            scene,report=build_parts(archive)
            progress['completed'].append({'archive':archive,'parts':len(report['entries'])})
        except Exception:progress['errors'].append({'archive':archive,'error':traceback.format_exc()})
        finally:
            bpy.context.window.scene=old
            if scene:
                bpy.data.batch_remove(ids=tuple(scene.objects)+(scene,))
            # Only delete unused data blocks, never objects or user scenes.
            for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
                unused=tuple(block for block in collection if block.users==0)
                if unused:bpy.data.batch_remove(ids=unused)
        progress['remaining']=queue.copy();MOD.mkdir(parents=True,exist_ok=True)
        (MOD/'progress.json').write_text(json.dumps(progress,indent=2)+'\n')
        return .1 if queue else None
    bpy.app.timers.register(tick,first_interval=.5)
    print('Scheduled',len(queue),'modification archives')
