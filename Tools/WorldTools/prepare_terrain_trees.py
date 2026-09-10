"""Create reusable native Terrain tree prototypes and original-placement manifests."""
import copy,itertools,json,math,mmap,struct
from pathlib import Path
from inventory_game import ROOT,GAME,OUT
from geometry import read_solid
DIR=ROOT/'Assets/NfsMw/Content/World/Models/RockportTrees';DEST=ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Trees';REPORT=ROOT/'Art/RockportTrees/Source'
def unity(v):return (-v[0],v[2],-v[1])
def mul(r,v):return [sum(r[k*3+q]*v[k] for k in range(3)) for q in range(3)]
def f32(x):return struct.unpack('<f',struct.pack('<f',x))[0]
def main():
    DEST.mkdir(parents=True,exist_ok=True)
    source=json.loads((REPORT/'trees-decoding.json').read_text());old=json.loads((DIR/'RockportTrees.gltf').read_text());lookup={g['hash']:g for g in json.loads((OUT/'geometry-inventory.json').read_text())['solids']}
    # Keep vegetation prototype identifiers stable when adding breakable tree props.
    source['instances'].sort(key=lambda ins:not ins['name'].startswith('XT_'))
    terrain=json.loads((ROOT/'Assets/NfsMw/Content/World/Maps/Rockport/Heightmaps/metadata.json').read_text());tiles={(t['xIndex'],t['zIndex']):t for t in terrain['tiles']}
    doc={k:copy.deepcopy(old[k]) for k in ('asset','materials','textures','images','samplers')};doc.update(scene=0,scenes=[{'name':'Rockport Native Tree Prototypes','nodes':[]}],nodes=[],meshes=[],accessors=[],bufferViews=[],buffers=[])
    blob=bytearray();cache={};prototypes={};placements=[];specs=[];maxerr=0;materialids={m['name'].split('_')[-1]:i for i,m in enumerate(old['materials'])}
    def accessor(values,kind,fmt,component,bounds=False):
        while len(blob)%4:blob.append(0)
        flat=list(itertools.chain.from_iterable(values)) if kind!='SCALAR' else values;raw=struct.pack('<'+str(len(flat))+fmt,*flat);view=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(raw)});blob.extend(raw)
        a={'bufferView':view,'componentType':component,'count':len(values),'type':kind}
        if bounds:a.update(min=[min(v[k] for v in values) for k in range(3)],max=[max(v[k] for v in values) for k in range(3)])
        doc['accessors'].append(a);return len(doc['accessors'])-1
    with (GAME/'TRACKS/STREAML2RA.BUN').open('rb') as handle,mmap.mmap(handle.fileno(),0,access=mmap.ACCESS_READ) as data:
        for ins in source['instances']:
            key=ins['sourceSolidHash'];g=lookup[key];r=ins['packedRotation'];world=unity(ins['gamePosition'])
            width=(math.hypot(r[0],r[1])+math.hypot(r[3],r[4]))/2;height=r[8];yaw=math.atan2(r[1],r[0]);c=math.cos(yaw)*width;s=math.sin(yaw)*width;approx=[c,s,0,-s,c,0,0,0,height]
            err=max(math.sqrt(sum(v*v for v in mul([a-b for a,b in zip(r,approx)],p))) for p in itertools.product(*zip(g['boundsMin'],g['boundsMax'])))
            shared=err<=.01 and width>0 and height>0
            variant=(key,'upright') if shared else (key,*r)
            if not shared:width=height=1;yaw=0;err=0
            angle=f32((-yaw)%(math.pi*2));width=f32(width);height=f32(height)
            if variant not in prototypes:
                if key not in cache:cache[key]=read_solid(data,g['offset'],struct.unpack_from('<I',data,g['offset']+4)[0])
                solid=cache[key];matrix=[1,0,0,0,1,0,0,0,1] if shared else r
                allpos=[unity(mul(matrix,v)) for b in solid['buffers'] for v in b['positions']];lo=[min(p[k] for p in allpos) for k in range(3)];hi=[max(p[k] for p in allpos) for k in range(3)];anchor=[(lo[0]+hi[0])/2,lo[1],(lo[2]+hi[2])/2]
                a,b,c=matrix[0],matrix[3],matrix[6];d,e,f=matrix[1],matrix[4],matrix[7];h,i,j=matrix[2],matrix[5],matrix[8];det=a*(e*j-f*i)-b*(d*j-f*h)+c*(d*i-e*h);cof=(e*j-f*i,f*h-d*j,d*i-e*h,c*i-b*j,a*j-c*h,b*h-a*i,b*f-c*e,c*d-a*f,a*e-b*d)
                prims=[]
                for material in solid['materials']:
                    if not material['triangleCount']:continue
                    buf=solid['buffers'][material['stream']];unique=sorted(set(material['indices']));remap={old:new for new,old in enumerate(unique)};positions=[];normals=[];uv=[]
                    for vi in unique:
                        v=unity(mul(matrix,buf['positions'][vi]));p=[f32(v[k]-anchor[k]) for k in range(3)];positions.append((-p[0],p[1],p[2]));n=buf['normals'][vi];n=unity([sum(cof[q*3+k]*n[k] for k in range(3))/det for q in range(3)]);length=math.sqrt(sum(q*q for q in n)) or 1;normals.append((-n[0]/length,n[1]/length,n[2]/length));uv.append(buf['uv'][vi])
                    indices=[remap[v] for v in material['indices']]
                    if det<0:
                        for n in range(0,len(indices),3):indices[n+1],indices[n+2]=indices[n+2],indices[n+1]
                    prims.append({'attributes':{'POSITION':accessor(positions,'VEC3','f',5126,True),'NORMAL':accessor(normals,'VEC3','f',5126),'TEXCOORD_0':accessor(uv,'VEC2','f',5126)},'indices':accessor(indices,'SCALAR','I',5125),'material':materialids[material['diffuse']]})
                index=len(specs);name=f'Tree_{index:04d}_'+g['name'];spec={'index':index,'name':name,'sourceSolidHash':key,'transformMode':'native yaw and scale' if shared else 'baked original affine transform','anchor':anchor,'boundsMin':[lo[k]-anchor[k] for k in range(3)],'boundsMax':[hi[k]-anchor[k] for k in range(3)]};specs.append(spec);prototypes[variant]=spec
                doc['meshes'].append({'name':name,'primitives':prims});doc['nodes'].append({'name':name,'mesh':index});doc['scenes'][0]['nodes'].append(index)
            proto=prototypes[variant];ax,ay,az=proto['anchor'];cs=math.cos(angle);sn=math.sin(angle);offset=[width*(cs*ax+sn*az),height*ay,width*(-sn*ax+cs*az)];position=[f32(world[k]+offset[k]) for k in range(3)];tilekey=(math.floor((position[0]-terrain['originX'])/1024),math.floor((position[2]-terrain['originZ'])/1024));tile=tiles.get(tilekey)
            if tile is None:raise ValueError(('No terrain tile',ins['name'],position))
            normalized=[(position[0]-tile['originX'])/1024,(position[1]-terrain['minHeight'])/terrain['heightRange'],(position[2]-tile['originZ'])/1024]
            if any(v<0 or v>1 for v in normalized):raise ValueError(('Outside terrain instance range',ins['name'],position,normalized))
            placements.append({'sourceOffset':ins['sourceInstanceOffset'],'name':ins['name'],'prototype':proto['index'],'tile':tile['name'],'worldPosition':position,'rotation':angle,'widthScale':width,'heightScale':height,'sourceTransformErrorBoundMetres':err});maxerr=max(maxerr,err)
    doc['buffers']=[{'byteLength':len(blob),'uri':'NativeTreePrototypes.bin'}];doc['asset']['generator']='Rockport original trees for editable Unity Terrain';(DIR/'NativeTreePrototypes.bin').write_bytes(blob);(DIR/'NativeTreePrototypes.gltf').write_text(json.dumps(doc,separators=(',',':')))
    (DEST/'prototypes.tsv').write_text(''.join(p['name']+'\n' for p in specs));(DEST/'placements.tsv').write_text(''.join('\t'.join(map(str,[p['tile'],p['prototype'],*p['worldPosition'],p['rotation'],p['widthScale'],p['heightScale'],p['sourceOffset']]))+'\n' for p in placements))
    report={'instances':placements,'prototypes':specs,'counts':{'treeAndGroupPlacements':len(placements),'prototypes':len(specs),'sourceSolidTypesIncludingAuthoredGroups':len(cache),'sourceTransformMaxApproximationMetres':maxerr},'policy':'Original tree and grouped tree geometry. Preserve source world elevation with SetTreeInstances snapToHeightmap=false. Upright transforms share prototypes only when source bounds prove at most 1 cm difference; other affine transforms are baked. Prototype pivots are recentered without changing rendered world positions. No random trees or ground filling.'}
    (REPORT/'terrain-trees.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report['counts'],indent=2))
if __name__=='__main__':main()
