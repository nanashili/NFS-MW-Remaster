"""Decode building geometry and original placements into one portable glTF scene."""
import collections, hashlib, itertools, json, math, mmap, struct, sys
from pathlib import Path
from inventory_game import ROOT,GAME,OUT,chunks
from geometry import read_solid
from classification import object_category,material_decision
sys.path.insert(0,str(ROOT/'Tools/FrontendAssets'))
from texture_archive import TextureRecord,decode_texture
from texture_decode import png_bytes

def main(layer='buildings'):
    if layer=='buildings':
        asset_name='RockportBuildings';category_for=object_category;decide=lambda g,m,t:material_decision(object_category(g['name']),m['name'],t)
    elif layer=='trees':
        from tree_classification import category_for, decision
        asset_name='RockportTrees';decide=lambda g,m,t:decision(g['name'],m['name'],t)
    else:
        from ground_classification import category_for, decision
        assert layer in ('roads','terrain')
        asset_name='RockportRoads' if layer=='roads' else 'RockportTerrainSource'
        decide=lambda g,m,t:decision(layer,g['name'],m['name'],t)
    report_out=OUT if layer=='buildings' else ROOT/('Art/RockportTrees/Source' if layer=='trees' else 'Art/RockportGround/Source')
    report_out.mkdir(parents=True,exist_ok=True)
    inv=json.loads((OUT/'game-inventory.json').read_text())
    geo=json.loads((OUT/'geometry-inventory.json').read_text())['solids']
    tex=json.loads((OUT/'textures.json').read_text())['textures']
    lookup={g['hash']:g for g in geo}
    output=ROOT/'Assets/NfsMw/Content/World/Models'/asset_name;output.mkdir(parents=True,exist_ok=True)
    texture_out=output/'Textures';texture_out.mkdir(exist_ok=True)
    blob=bytearray();doc={'asset':{'version':'2.0','generator':'Rockport original PC building decoding'},'scene':0,'scenes':[{'name':'Rockport Buildings','nodes':[]}],'nodes':[],'meshes':[],'materials':[],'textures':[],'images':[],'samplers':[{'magFilter':9729,'minFilter':9987,'wrapS':10497,'wrapT':10497}],'accessors':[],'bufferViews':[],'buffers':[]}
    report={'sourceArchives':inv['archives'],'axisMapping':{'Blender':'(game X, game Y, game Z)','glTF':'(game X, game Z, -game Y)','Unity':'(-game X, game Z, -game Y)'},'rotationPolicy':'Bake packed rotation, scale and mirror into vertices; preserve original translation. No recentering, normalization or rescaling.','selection':[],'instances':[],'missingSolids':[],'lodFallbacks':[],'invalidPlacements':[],'textures':{},'counts':{}}
    material_map={};geometry_cache={};variants={};seen=set();decisions={};excluded=collections.Counter();bound_errors=[]
    def view(raw):
        while len(blob)%4:blob.append(0)
        n=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(raw)});blob.extend(raw);return n
    def accessor(values,kind,component,fmt,bounds=False):
        n=len(doc['accessors']); flat=list(itertools.chain.from_iterable(values)) if kind!='SCALAR' else values
        a={'bufferView':view(struct.pack('<'+str(len(flat))+fmt,*flat)),'componentType':component,'count':len(values),'type':kind}
        if bounds:a.update(min=[min(v[k] for v in values) for k in range(3)],max=[max(v[k] for v in values) for k in range(3)])
        doc['accessors'].append(a);return n
    def material(m):
        key=m['diffuse']
        if key in material_map:return material_map[key]
        meta=tex.get(key)
        if not meta:
            n=len(doc['materials']);doc['materials'].append({'name':'Unresolved_'+key+'_'+m['name'],'pbrMetallicRoughness':{'baseColorFactor':[0.55,0.55,0.55,1],'metallicFactor':0,'roughnessFactor':0.85},'doubleSided':True})
            material_map[key]=n;report['textures'][key]={'status':'unresolved source texture','material':m['name']};return n
        path=texture_out/(key+'.png')
        if not path.exists():
            payload=(ROOT/'Art/RockportBuildings/Textures'/(key+'.pixels')).read_bytes()
            image=decode_texture(TextureRecord(dict(meta),payload));path.write_bytes(png_bytes(image))
        i=len(doc['images']);doc['images'].append({'uri':'Textures/'+path.name,'name':meta['name']});doc['textures'].append({'sampler':0,'source':i})
        v={'name':meta['name']+'_'+key,'pbrMetallicRoughness':{'baseColorTexture':{'index':i},'metallicFactor':0,'roughnessFactor':0.85},'doubleSided':True}
        # Source alphaUsage 1 denotes punch-through; diffuse alpha is otherwise often specular.
        if meta['alphaUsage']==1 or (layer=='trees' and meta['alphaUsage']==2):v.update(alphaMode='MASK',alphaCutoff=0.5)
        n=len(doc['materials']);doc['materials'].append(v);material_map[key]=n
        report['textures'][key]={'name':meta['name'],'source':meta['source'],'pngSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sourceImageSha256':meta['sourceImageSha256'],'alphaUsage':meta['alphaUsage']}
        return n
    source=GAME/'TRACKS/STREAML2RA.BUN'
    with source.open('rb') as f,mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ) as data:
        digest=hashlib.sha256(data).hexdigest();assert digest==inv['archives'][1]['sha256']
        for section in inv['scenery']:
            for instance in section['instances']:
                info=section['infos'][instance['infoIndex']];key=info['solidKeys'][0];g=lookup.get(key)
                if not g and category_for(info['name'])!='excluded-object':
                    for lod,candidate in enumerate(info['solidKeys'][1:],1):
                        if candidate in lookup:
                            key=candidate;g=lookup[key];report['lodFallbacks'].append({'name':info['name'],'lodSlot':lod,'solid':key,'offset':instance['sourceOffset']});break
                if not g:
                    if category_for(info['name'])!='excluded-object':report['missingSolids'].append({'name':info['name'],'keys':info['solidKeys'],'section':section['sectionNumber']})
                    continue
                if key not in decisions:
                    category=category_for(g['name']);keep=[];sel={'hash':key,'name':g['name'],'category':category,'materials':[]}
                    for mi,m in enumerate(g['materials']):
                        t=tex.get(m['diffuse'],{}).get('name','');yes,reason=decide(g,m,t)
                        sel['materials'].append({'index':mi,'name':m['name'],'texture':t,'keep':yes,'reason':reason,'triangles':m['triangleCount']})
                        if yes and m['triangleCount']:keep.append(mi)
                    decisions[key]=keep;report['selection'].append(sel)
                keep=decisions[key]
                if not keep:excluded[category_for(g['name'])]+=1;continue
                r=instance['rotationRows'];p=instance['position'];identity=(key,*r,*p)
                if identity in seen:excluded['duplicate placement']+=1;continue
                seen.add(identity)
                if key not in geometry_cache:geometry_cache[key]=read_solid(data,g['offset'],struct.unpack_from('<I',data,g['offset']+4)[0])
                solid=geometry_cache[key]
                if p==[0.0,0.0,0.0] and max(abs(v) for v in instance['bounds'])>100 and max(abs(v) for v in solid['boundsMin']+solid['boundsMax'])<10:
                    report['invalidPlacements'].append({'name':g['name'],'offset':instance['sourceOffset'],'reason':'Source has an identity transform and local geometry but distant world culling bounds; no placement invented.'});continue
                variant=(key,*r)
                if variant not in variants:
                    # r stores matrix columns. Use inverse transpose for the source normals.
                    a,b,c=r[0],r[3],r[6];d,e,f=r[1],r[4],r[7];h,i,j=r[2],r[5],r[8]
                    det=a*(e*j-f*i)-b*(d*j-f*h)+c*(d*i-e*h)
                    assert abs(det)>1e-8,(g['name'],'singular transform')
                    cof=(e*j-f*i,f*h-d*j,d*i-e*h,c*i-b*j,a*j-c*h,b*h-a*i,b*f-c*e,c*d-a*f,a*e-b*d)
                    primitives=[]
                    for mi in keep:
                        m=solid['materials'][mi];buf=solid['buffers'][m['stream']];idx=m['indices'];unique=sorted(set(idx));remap={old:new for new,old in enumerate(unique)}
                        positions=[];normals=[];uv=[]
                        for vi in unique:
                            v=buf['positions'][vi];w=[sum(r[k*3+q]*v[k] for k in range(3)) for q in range(3)];positions.append((w[0],w[2],-w[1]))
                            n=buf['normals'][vi];nn=[sum(cof[q*3+k]*n[k] for k in range(3))/det for q in range(3)];length=math.sqrt(sum(v*v for v in nn)) or 1;normals.append((nn[0]/length,nn[2]/length,-nn[1]/length));uv.append(buf['uv'][vi])
                        indices=[remap[v] for v in idx]
                        if det<0:
                            for ii in range(0,len(indices),3):indices[ii+1],indices[ii+2]=indices[ii+2],indices[ii+1]
                        primitives.append({'attributes':{'POSITION':accessor(positions,'VEC3',5126,'f',True),'NORMAL':accessor(normals,'VEC3',5126,'f'),'TEXCOORD_0':accessor(uv,'VEC2',5126,'f')},'indices':accessor(indices,'SCALAR',5125,'I'),'material':material(m)})
                    variants[variant]=len(doc['meshes']);doc['meshes'].append({'name':g['name']+'_'+key,'primitives':primitives})
                node={'name':g['name']+'__'+str(instance['sourceOffset']),'mesh':variants[variant],'translation':[p[0],p[2],-p[1]],'extras':{'sourceSolidHash':key,'sourceSection':section['sectionNumber'],'sourceInstanceOffset':instance['sourceOffset']}}
                doc['scenes'][0]['nodes'].append(len(doc['nodes']));doc['nodes'].append(node)
                report['instances'].append(dict(node['extras'],name=g['name'],gamePosition=p,packedRotation=r,excludeFlags=instance['excludeFlags'],mesh=node['mesh']))
                # Compare transformed actual source vertices (before surface filtering) with authored culling bounds.
                points=[]
                for buf in solid['buffers']:
                    points.extend([[p[q]+sum(r[k*3+q]*v[k] for k in range(3)) for q in range(3)] for v in buf['positions']])
                actual=[min(v[q] for v in points) for q in range(3)]+[max(v[q] for v in points) for q in range(3)]
                error=max(abs(x-y) for x,y in zip(actual,instance['bounds']));bound_errors.append((error,g['name'],instance['sourceOffset']))
            if len(doc['nodes']) and section['sectionNumber']%100==0:print('section',section['sectionNumber'],'instances',len(doc['nodes']),'materials',len(material_map),flush=True)
        assert hashlib.sha256(data).hexdigest()==digest
    doc['asset']['generator']='Rockport original PC '+layer+' decoding';doc['scenes'][0]['name']=asset_name
    doc['buffers']=[{'byteLength':len(blob),'uri':asset_name+'.bin'}]
    (output/(asset_name+'.bin')).write_bytes(blob);(output/(asset_name+'.gltf')).write_text(json.dumps(doc,separators=(',',':')))
    report['counts']={'instances':len(doc['nodes']),'meshVariants':len(doc['meshes']),'uniqueSourceSolids':len(geometry_cache),'materials':len(material_map),'triangles':sum(len(geometry_cache[k]['materials'][mi]['indices'])//3 for k in geometry_cache for mi in decisions[k]),'excludedInstances':dict(excluded),'sourceUnchanged':True}
    report['placementBoundsValidation']={'maxErrorMetres':max(e[0] for e in bound_errors),'withinFiveCentimetres':sum(e[0]<0.05 for e in bound_errors),'total':len(bound_errors),'largestErrors':sorted(bound_errors,reverse=True)[:20]}
    retained_offsets={i['sourceInstanceOffset'] for i in report['instances']}
    report['retainedLodFallbacks']=[i for i in report['lodFallbacks'] if i['offset'] in retained_offsets]
    (report_out/('decoding-report.json' if layer=='buildings' else layer+'-decoding.json')).write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report['counts']));print(json.dumps(report['placementBoundsValidation']));print('missing',report['missingSolids'])
if __name__=='__main__':main(sys.argv[1] if len(sys.argv)>1 else 'buildings')
