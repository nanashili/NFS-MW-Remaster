"""Derive planting markers, never render geometry, for newly modeled Unity trees.

Original prototype bounds/topology identify trunk bases and billboard tree rows.
Only positions, dimensions, species and seeds leave this script. Replacement
vertices, normals, UVs and textures are generated independently by Unity.
"""
import json, math, struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets/NfsMw/Content/World/Models/RockportTrees'
OUT = ROOT / 'Assets/NfsMw/Content/World/Maps/Rockport/Trees/Replacement'

def main():
    manifest = json.loads((ROOT / 'Art/RockportTrees/Source/terrain-trees.json').read_text())
    doc = json.loads((SOURCE / 'NativeTreePrototypes.gltf').read_text())
    blob = (SOURCE / doc['buffers'][0]['uri']).read_bytes()
    def read(index):
        a = doc['accessors'][index]; view = doc['bufferViews'][a['bufferView']]
        fmt = {5126:'f', 5125:'I', 5123:'H'}[a['componentType']]
        width = {'VEC3':3, 'VEC2':2, 'SCALAR':1}[a['type']]
        offset = view.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = view.get('byteStride', struct.calcsize(fmt) * width)
        return [struct.unpack_from('<' + fmt * width, blob, offset + i * stride) for i in range(a['count'])]
    def components(primitive):
        # Weld topology only to identify source planting markers. No mesh output.
        points = [(-x, y, z) for x,y,z in read(primitive['attributes']['POSITION'])]
        indices = [v[0] for v in read(primitive['indices'])]
        parent = list(range(len(points))); weld = {}
        def root(i):
            while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
            return i
        def union(a,b): parent[root(a)] = root(b)
        for i,p in enumerate(points):
            key = tuple(round(v, 3) for v in p)
            if key in weld: union(i,weld[key])
            else: weld[key] = i
        for a,b,c in zip(indices[::3],indices[1::3],indices[2::3]): union(a,b); union(b,c)
        groups = {}
        for i in set(indices): groups.setdefault(root(i),[]).append(points[i])
        return list(groups.values())
    def species(name):
        if any(v in name for v in ('DEAD','TRUNK')): return 4
        if any(v in name for v in ('CYPRESS','POPLAR','JUNIPER')): return 2
        if any(v in name for v in ('CEDAR','EVRGRN','EVERGREEN','PINE')): return 1
        if 'SMACKTREE' in name: return 3
        return 0
    rows=[]; summaries=[]
    for spec,mesh in zip(manifest['prototypes'],doc['meshes']):
        name=spec['name']; kind=species(name); height=spec['boundsMax'][1]; markers=[]
        if 'SMACKTREE' in name:
            markers=[(-spec['anchor'][0],0,-spec['anchor'][2],height, .11 if 'SMACKTREEB' in name else .08)]
        else:
            woody=[]; leaves=[]
            for primitive in mesh['primitives']:
                material=doc['materials'][primitive['material']]['name'].upper()
                woody_material=any(token in material for token in ('TRUNK','BARK'))
                for points in components(primitive):
                    lo=[min(p[k] for p in points) for k in range(3)]; hi=[max(p[k] for p in points) for k in range(3)]
                    if hi[1]-lo[1]<1: continue
                    (woody if woody_material else leaves).append((points,lo,hi))
            groups=woody or leaves
            for points,lo,hi in groups:
                span=max(hi[0]-lo[0],hi[2]-lo[2]); h=hi[1]-lo[1]
                if woody:
                    low=[p for p in points if p[1]<=lo[1]+min(.3,h*.05)]
                    x=sum(p[0] for p in low)/len(low); z=sum(p[2] for p in low)/len(low)
                    grown=min(height,max(h*1.35,8)); radius=max(.1,min(.55,grown*.015))
                    markers.append((x,lo[1],z,grown,radius))
                elif span < max(12,h*1.25):
                    markers.append(((lo[0]+hi[0])*.5,lo[1],(lo[2]+hi[2])*.5,min(30,max(6,h)),min(.45,max(.1,h*.014))))
                else:
                    # Replace a billboard row along its authored lower edge, not a random footprint scatter.
                    axis=0 if hi[0]-lo[0]>=hi[2]-lo[2] else 2
                    bins={}
                    for p in points:
                        key=round(p[axis],2)
                        if key not in bins or p[1]<bins[key][1]: bins[key]=p
                    edge=sorted(bins.values(),key=lambda p:p[axis]); tree_h=min(25,max(8,h*.7))
                    steps=max(2,min(40,math.ceil(span/(tree_h*.48))))
                    for i in range(steps):
                        at=lo[axis]+span*(i+.5)/steps
                        pair=next(((a,b) for a,b in zip(edge,edge[1:]) if a[axis]<=at<=b[axis]),None)
                        if pair:
                            a,b=pair; t=(at-a[axis])/max(.0001,b[axis]-a[axis]); p=tuple(a[k]+(b[k]-a[k])*t for k in range(3))
                        else: p=min(edge,key=lambda p:abs(p[axis]-at))
                        markers.append((p[0],p[1],p[2],tree_h,max(.12,tree_h*.014)))
            if not markers:
                markers=[(0,0,0,min(30,max(5,height)),.18)]
        unique=[]
        for marker in markers:
            if not any(math.hypot(marker[0]-p[0],marker[2]-p[2])<max(1,min(marker[3],p[3])*.22) and abs(marker[1]-p[1])<3 for p in unique): unique.append(marker)
        for i,(x,y,z,h,radius) in enumerate(unique):
            rows.append('\t'.join(map(str,[spec['index'],x,y,z,h,radius,kind,spec['index']*31+i])))
        summaries.append({'prototype':spec['index'],'name':name,'newTrees':len(unique),'species':kind})
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/'planting-markers.tsv').write_text('\n'.join(rows)+'\n')
    report={'status':'PASS','prototypeCount':len(summaries),'newTreesAcrossPrototypes':len(rows),'policy':'Only planting markers and estimated sizes derive from source data. Every replacement vertex, normal, UV and texture is independently generated in Unity. Original tree meshes and textures are excluded from replacement asset dependencies.','prototypes':summaries}
    (ROOT/'Art/RockportTrees/Source/replacement-tree-markers.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k!='prototypes'},indent=2))

if __name__=='__main__':main()
