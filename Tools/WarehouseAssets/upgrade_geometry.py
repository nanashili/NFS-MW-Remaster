"""Run inside Blender against the decoded PlatformCrib scene."""
import bpy
import bmesh
import math
import json
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT = Path('/Users/tihan-nico/NFS MW Remaster')
ART = ROOT / 'Art/FrontendCourtyard'
scene = bpy.context.scene
assert scene.name == 'MW05 | Decoded PlatformCrib'
bpy.ops.wm.save_as_mainfile(filepath=str(ART / 'warehouse-unity-updated.blend'))
scene.name = 'MW05 | Unity warehouse updated'
report = {'removed': {}, 'pipes': [], 'tanks': []}

def components(bm):
    unseen = {v for v in bm.verts if v.link_faces}
    result = []
    while unseen:
        stack = [unseen.pop()]
        group = []
        while stack:
            v = stack.pop()
            group.append(v)
            for edge in v.link_edges:
                n = edge.other_vert(v)
                if n in unseen:
                    unseen.remove(n)
                    stack.append(n)
        result.append(group)
    return result

def cleanup(bm):
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')

def smooth_angles(mesh, degrees=48):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.normal_update()
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        e.smooth = e.is_manifold and e.calc_face_angle() < math.radians(degrees)
    bm.to_mesh(mesh)
    bm.free()

def circle_fit(points):
    points = np.array(points)
    mean = points.mean(axis=0)
    _, _, axes = np.linalg.svd(points-mean, full_matrices=False)
    p = (points-mean) @ axes[:2].T
    xy = np.linalg.lstsq(np.column_stack((2*p[:,0], 2*p[:,1], np.ones(len(p)))), (p*p).sum(axis=1), rcond=None)[0]
    center = mean + xy[0]*axes[0] + xy[1]*axes[1]
    radius = float(np.sqrt(xy[2]+xy[0]**2+xy[1]**2))
    return Vector(center), radius

def ordered(points):
    # Original long pipe routes each form one unbranched path, beginning at min X.
    remaining = list(points)
    first = min(remaining, key=lambda p:p.x)
    remaining.remove(first)
    result = [first]
    while remaining:
        p = min(remaining, key=lambda p:(p-result[-1]).length)
        remaining.remove(p)
        result.append(p)
    return result

def tube(name, centers, radius, material, collection):
    sides = 24
    verts, faces, tex = [], [], []
    distances = [0.0]
    for a,b in zip(centers, centers[1:]):
        distances.append(distances[-1]+(b-a).length)
    for i,c in enumerate(centers):
        tangent = (centers[min(i+1,len(centers)-1)]-centers[max(i-1,0)]).normalized()
        u = tangent.cross(Vector((0,0,1))).normalized()
        v = u.cross(tangent).normalized()
        for j in range(sides):
            angle = 2*math.pi*j/sides
            verts.append(c + radius*(math.cos(angle)*u+math.sin(angle)*v))
    for i in range(len(centers)-1):
        for j in range(sides):
            k = (j+1)%sides
            faces.append((i*sides+j, i*sides+k, (i+1)*sides+k, (i+1)*sides+j))
            tex.append(((distances[i]/4,j/sides), (distances[i]/4,(j+1)/sides), (distances[i+1]/4,(j+1)/sides), (distances[i+1]/4,j/sides)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(material)
    layer=mesh.uv_layers.new(name='UVMap')
    for p, coords in zip(mesh.polygons, tex):
        for li, uv in zip(p.loop_indices,coords):
            layer.data[li].uv=uv
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh);bm.free()
    for p in mesh.polygons:p.use_smooth=True
    obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj)
    obj['source']='Retopology of original PlatformCrib pipe route; 24 radial segments'
    return obj

# The original game uses mirrored geometry underneath the ground for its reflection.
sky=bpy.data.objects.get('DEFAULT-PlatformCrib-BACKDROP_CURRGEN')
assert sky
report['removed']['sky_triangles']=len(sky.data.polygons)
bpy.data.objects.remove(sky,do_unlink=True)
crib=bpy.data.objects['DEFAULT-PlatformCrib-BACKDROP_CRIB']
bm=bmesh.new();bm.from_mesh(crib.data)
# Keep the ground and above-ground dressing. Delete only reflection geometry,
# including reflected walls crossing the -0.05 threshold; preserve ground decals.
reflection=[f for f in bm.faces if min(v.co.z for v in f.verts)<-.09 and crib.data.materials[f.material_index].name not in {'FLOOR1','SKIDS','SHADOWMAP'}]
report['removed']['reflection_faces']=len(reflection)
bmesh.ops.delete(bm,geom=reflection,context='FACES');cleanup(bm)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
bm.to_mesh(crib.data);bm.free()
crib.name='Warehouse | ground and backdrop'

body=bpy.data.objects['DEFAULT-PlatformCrib-BACKDROP_CAST_SHADOW_MAP']
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
bm.to_mesh(body.data);bm.free()
body.name='Warehouse | original architecture'

# Isolate original pipe topology to recover its centerline independently of beams.
bm=bmesh.new();bm.from_mesh(body.data)
pipe_slot=next(i for i,m in enumerate(body.data.materials) if m.name=='PIPE1.001')
bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.material_index!=pipe_slot],context='FACES')
cleanup(bm);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0001)
delete_coords=set()
for group in components(bm):
    if len(group) not in {18,21,54,66}:continue
    points=[v.co.copy() for v in group]
    radii=[];centers=[]
    if len(group)==18:
        # Three oblique cross-sections. Their X extents are disjoint.
        points.sort(key=lambda p:p.x)
        for i in range(3):
            center,r=circle_fit([list(p) for p in points[i*6:(i+1)*6]])
            centers.append(center);radii.append(r)
    else:
        # Original horizontal/sloping ring tops and bottoms share XY.
        used=set()
        diameter=max(abs(a.z-b.z) for a in points for b in points if (a.xy-b.xy).length<.06)
        for i,a in enumerate(points):
            if i in used:continue
            matches=[(j,b) for j,b in enumerate(points) if j!=i and j not in used and (a.xy-b.xy).length<.06 and abs(a.z-b.z)>diameter*.94]
            if matches:
                j,b=max(matches,key=lambda p:abs(a.z-p[1].z))
                centers.append((a+b)/2);radii.append(abs(a.z-b.z)/2);used.update((i,j))
    expected={18:3,21:3,54:9,66:11}[len(group)]
    assert len(centers)==expected,(len(group),len(centers))
    centers=ordered(centers)
    radius=sum(radii)/len(radii)
    new=tube('Warehouse | rounded pipe %02d'%(len(report['pipes'])+1),centers,radius,body.data.materials[pipe_slot],body.users_collection[0])
    report['pipes'].append({'source_vertices':len(group),'rings':len(centers),'radius':radius,'triangles':len(new.data.polygons)*2,'centerline':[list(c) for c in centers]})
    delete_coords.update(tuple(round(x,4) for x in v.co) for v in group)
bm.free()
bm=bmesh.new();bm.from_mesh(body.data)
remove=[f for f in bm.faces if f.material_index==pipe_slot and all(tuple(round(x,4) for x in v.co) in delete_coords for v in f.verts)]
bmesh.ops.delete(bm,geom=remove,context='FACES');cleanup(bm)
bm.to_mesh(body.data);bm.free()

# Subdivide only the cylindrical tank walls and project onto the source ellipse.
# Roofs, support beams and the main tank's arched duct retain their original shapes.
bm=bmesh.new();bm.from_mesh(body.data)
tank_slot=next(i for i,m in enumerate(body.data.materials) if m.name=='WATERTOWER.001')
for cx,cy,rx,ry,z0,z1 in [(-.92185,-7.7636,1.26495,1.1998,2.851,6.699),(-5.3494,-7.7636,.9487,.8998,2.395,4.225),(5.3137,-14.465,1.26495,1.1998,2.851,6.699)]:
    sides=[f for f in bm.faces if f.material_index==tank_slot and all(z0-.002<=v.co.z<=z1+.002 and abs(v.co.x-cx)<rx+.025 and abs(v.co.y-cy)<ry+.025 for v in f.verts) and max(v.co.z for v in f.verts)-min(v.co.z for v in f.verts)>1]
    assert sides, 'No tank side faces'
    # Keep a vertex group through subdivision to avoid changing nearby supports.
    layer=bm.verts.layers.float.get('round_tank') or bm.verts.layers.float.new('round_tank')
    for v in bm.verts:v[layer]=0
    for f in sides:
        for v in f.verts:v[layer]=1
    edges=list({e for f in sides for e in f.edges})
    bmesh.ops.subdivide_edges(bm,edges=edges,cuts=2,use_grid_fill=True)
    n=0
    for v in bm.verts:
        if v[layer]>.99:
            x=(v.co.x-cx)/rx;y=(v.co.y-cy)/ry;r=math.hypot(x,y)
            if r>.3:
                v.co.x=cx+rx*x/r;v.co.y=cy+ry*y/r;n+=1
    report['tanks'].append({'center':[cx,cy],'rounded_vertices':n})
bm.to_mesh(body.data);bm.free()
for obj in (body,crib):
    # AssetDumper's OBJ triangle winding is reversed: ground pointed downward,
    # tank walls inward, and the rear courtyard wall away from the courtyard.
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.reverse_faces(bm,faces=list(bm.faces))
    bm.to_mesh(obj.data);bm.free()
    obj['source_winding_corrected']=True
    smooth_angles(obj.data)

scene.render.film_transparent=True
scene['unity_note']='No sky, vehicle, reflected underground warehouse, camera or light in mesh export. Preview lights remain Blender-only.'
scene['geometry_upgrade']='Original architecture retained. Four original pipe routes retopologized to 24 sides. Three original tank walls radially refined. Angle-limited smooth normals.'
(ART/'Updated').mkdir(exist_ok=True)
(ART/'Updated/geometry-report.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'warehouse-unity-updated.blend'))
print(json.dumps({'pipes':len(report['pipes']),'tanks':len(report['tanks']),'removed':report['removed'],'mesh_faces':sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH')}))
