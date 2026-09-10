"""Inventory material-connected source islands after welding split shading vertices."""
import bpy,bmesh,json
from pathlib import Path
from collections import defaultdict
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');s=bpy.context.scene
assert s['room']=='SafeHouse'
report=[]
for o in s.objects:
    if o.type!='MESH' or 'BACKDROP' not in o.name:continue
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0001)
    for index,mat in enumerate(o.data.materials):
        faces={f for f in bm.faces if f.material_index==index and f.calc_area()>1e-8}
        group=0
        while faces:
            seed=faces.pop();todo=[seed];component={seed}
            while todo:
                f=todo.pop()
                for v in f.verts:
                    for n in v.link_faces:
                        if n in faces:faces.remove(n);component.add(n);todo.append(n)
            verts={v for f in component for v in f.verts};pts=[o.matrix_world@v.co for v in verts]
            lo=[min(v[a] for v in pts) for a in range(3)];hi=[max(v[a] for v in pts) for a in range(3)]
            layer=bm.loops.layers.uv.active;uv=[loop[layer].uv.copy() for f in component for loop in f.loops] if layer else []
            report.append({'object':o.name,'material':mat.get('sourceTextureName',mat.name),'group':group,'faces':len(component),'area':sum(f.calc_area() for f in component),'min':lo,'max':hi,'center':[(lo[a]+hi[a])/2 for a in range(3)],'size':[hi[a]-lo[a] for a in range(3)],'uvMin':[min(v[a] for v in uv) for a in range(2)] if uv else [],'uvMax':[max(v[a] for v in uv) for a in range(2)] if uv else []})
            group+=1
    bm.free()
(ROOT/'Art/FrontendRooms/SafeHouse/prop-audit-source.json').write_text(json.dumps(report,indent=2))
print('Islands',len(report))
