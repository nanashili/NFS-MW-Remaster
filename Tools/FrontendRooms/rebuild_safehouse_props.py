"""Replace MW05 prop cards and coarse shells with bounded, explicit 3D meshes.

Run once from SafeHouse-before-prop-rebuild.blend through Blender MCP.
All dimensions and placement come from the recovered scene; architectural meshes remain.
"""
import bpy,bmesh,json,math
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
from collections import Counter
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');ART=ROOT/'Art/FrontendRooms/SafeHouse';s=bpy.context.scene
assert s['room']=='SafeHouse' and not s.get('solidPropsRebuilt',False)
audit=json.loads((ART/'prop-audit-source.json').read_text())
coll=bpy.data.collections.new('02 | Real 3D safe-house props');s.collection.children.link(coll)
counts=Counter();created=[]
def material(name,color,metal=0,rough=.5,emit=0):
    m=bpy.data.materials.new('SafeHouse prop | '+name);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    if emit:p.inputs['Emission Color'].default_value=(*color,1);p.inputs['Emission Strength'].default_value=emit
    return m
rubber=material('rubber',(.012,.016,.019),0,.78)
steel=material('brushed steel',(.30,.34,.37),.85,.28)
dark=material('dark steel',(.035,.047,.052),.75,.36)
red=material('red enamel',(.27,.025,.014),.3,.33)
blue=material('blue worn enamel',(.025,.075,.10),.35,.48)
ivory=material('warm white enamel',(.55,.56,.51),.15,.38)
brass=material('brass fittings',(.38,.23,.055),.8,.31)
wood=material('wood',(.20,.105,.043),0,.72)
card=material('cardboard',(.30,.21,.12),0,.88)
paper=material('paper',(.63,.62,.53),0,.85)
fabric=material('olive upholstery',(.09,.10,.067),0,.93)
glass=material('screen glass',(.009,.018,.021),.05,.15)
brick=material('bricks',(.22,.07,.035),0,.92)
light=material('work light',(.70,.84,1),0,.3,2)
def link(o,category):
    for c in list(o.users_collection):c.objects.unlink(o)
    coll.objects.link(o);o['propCategory']=category;created.append(o);return o
def box(name,loc,size,mat,bevel=.01,rotation=0,category=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.rotation_euler.z=rotation;o.data.materials.append(mat)
    if bevel:
        mod=o.modifiers.new('Physical edge radius','BEVEL');mod.width=min(bevel,min(size)*.22);mod.segments=3 if category=='sofa' else 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return link(o,category or name)
def mesh(name,verts,faces,mat,category,smooth=False):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.materials.append(mat);me.update()
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    for p in me.polygons:p.use_smooth=smooth
    o=bpy.data.objects.new(name,me);coll.objects.link(o);o['propCategory']=category;created.append(o);return o
def lathe(name,profile,mat,loc=(0,0,0),segments=40,axis=(0,0,1),category='round prop'):
    # Profile is a closed contour of (radius, axial position). An inner contour creates a real hole.
    verts=[];faces=[];q=Vector(axis).normalized().to_track_quat('Z','Y');center=Vector(loc)
    for r,z in profile:
        for i in range(segments):
            a=i*math.tau/segments;verts.append(tuple(center+q@Vector((r*math.cos(a),r*math.sin(a),z))))
    for j in range(len(profile)):
        for i in range(segments):
            ni=(i+1)%segments;nj=(j+1)%len(profile);faces.append((j*segments+i,j*segments+ni,nj*segments+ni,nj*segments+i))
    return mesh(name,verts,faces,mat,category,True)
def cylinder(name,loc,r,depth,mat,axis=(0,0,1),segments=32,category='fitting'):
    # Solid caps and small physical chamfers, including a nonzero central cap radius.
    z=depth/2;b=min(.012,r*.12,depth*.12)
    return lathe(name,[(0,-z),(r-b,-z),(r,-z+b),(r,z-b),(r-b,z),(0,z)],mat,loc,segments,axis,category)
def tube(name,points,r,mat,category,segments=8):
    points=[Vector(p) for p in points];verts=[];faces=[]
    for i,p in enumerate(points):
        tangent=(points[min(i+1,len(points)-1)]-points[max(i-1,0)]).normalized()
        ref=Vector((0,0,1)) if abs(tangent.z)<.95 else Vector((1,0,0));u=tangent.cross(ref).normalized();v=tangent.cross(u).normalized()
        for j in range(segments):
            a=j*math.tau/segments;verts.append(tuple(p+r*(math.cos(a)*u+math.sin(a)*v)))
    for i in range(len(points)-1):
        for j in range(segments):faces.append((i*segments+j,i*segments+(j+1)%segments,(i+1)*segments+(j+1)%segments,(i+1)*segments+j))
    faces.extend([tuple(range(segments-1,-1,-1)),tuple((len(points)-1)*segments+j for j in range(segments))])
    return mesh(name,verts,faces,mat,category,True)
def source(key):return [r for r in audit if r['material']==key]
def transform_parts(start,center,angle):
    rot=Matrix.Rotation(angle,4,'Z');tr=Matrix.Translation(Vector(center))
    for o in created[start:]:o.matrix_world=tr@rot@o.matrix_world
def bounds(key):
    rows=source(key);lo=[min(r['min'][a] for r in rows) for a in range(3)];hi=[max(r['max'][a] for r in rows) for a in range(3)];return lo,hi

# Capture full wheel islands (including their source axle direction) before deleting the old atlas mesh.
wheel_islands=[]
for o in list(s.objects):
    if o.type!='MESH' or 'BACKDROP' not in o.name:continue
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0001)
    faces={f for f in bm.faces if o.data.materials[f.material_index].get('sourceTextureName')=='CRIB1_WHEEL_TREAD'}
    while faces:
        seed=faces.pop();todo=[seed];found={seed}
        while todo:
            f=todo.pop()
            for v in f.verts:
                for n in v.link_faces:
                    if n in faces:faces.remove(n);found.add(n);todo.append(n)
        pts=np.array([tuple(o.matrix_world@v.co) for v in {v for f in found for v in f.verts}]);center=(pts.min(axis=0)+pts.max(axis=0))/2
        values,vectors=np.linalg.eigh(np.cov((pts-center).T));axis=vectors[:,0]
        if axis[2]<0:axis=-axis
        axial=(pts-center)@axis;radial=(pts-center)-axial[:,None]*axis
        radius=float(np.max(np.linalg.norm(radial,axis=1)));width=float(axial.max()-axial.min())
        wheel_islands.append((center.tolist(),axis.tolist(),radius,min(width,.30),float(np.ptp(pts[:,2]))>.5))
    bm.free()

# Remove full obsolete prop surfaces, including coplanar fronts and support shells sharing another atlas.
remove_keys={'CRIB1_WHEEL_TREAD','CRIB1_BARREL','CRIB1_TANK','CRIB1_OBJECTS01_TEXTURE','CRIB1_COUCH','CRIB1_TV','CRIB1_TVNOISE','CRIB1_FRIDGE','CRIB1_GASPUMP','CRIB1_TOOLBOX','CRIB1_BENCH_TEXTURE','CRIB1_CABLE','SFX_CRIB1_FAN','CRIB1_CARDBOARD'}
removed=Counter()
for o in list(s.objects):
    if o.type!='MESH' or 'BACKDROP' not in o.name:continue
    bm=bmesh.new();bm.from_mesh(o.data)
    gone=[f for f in bm.faces if o.data.materials[f.material_index].get('sourceTextureName') in remove_keys]
    for f in gone:removed[o.data.materials[f.material_index].get('sourceTextureName')]+=1
    if gone:bmesh.ops.delete(bm,geom=gone,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bm.to_mesh(o.data);bm.free()

# Tires have an open bore, rounded shoulders, modeled circumferential tread channel and thick sidewalls.
for i,(center,axis,R,width,upright) in enumerate(wheel_islands):
    if not upright:
        stack=[r for r in wheel_islands if not r[4] and math.hypot(r[0][0]-center[0],r[0][1]-center[1])<.32]
        shift=max(0,.003-min(r[0][2]-max(.20,r[3])/2 for r in stack))
        center=list(center);center[2]+=shift
    R=min(max(R,.34),.44);w=max(.20,width);h=w/2
    profile=[(.60*R,-.70*h),(.72*R,-h),(.91*R,-h),(.98*R,-.78*h),(R,-.58*h),(R,-.12*h),(.975*R,-.09*h),(.975*R,.09*h),(R,.12*h),(R,.58*h),(.98*R,.78*h),(.91*R,h),(.72*R,h),(.60*R,.70*h)]
    o=lathe('Tire %02d | open rubber carcass'%i,profile,rubber,center,48,axis,'tire')
    minz=min(v.co.z for v in o.data.vertices)
    if minz<.003:
        dz=.003-minz
        for v in o.data.vertices:v.co.z+=dz
        center[2]+=dz
    counts['tires']+=1
    # Shallow staggered tread blocks are actual solids. Their gaps expose the recessed carcass.
    q=Vector(axis).to_track_quat('Z','Y');c=Vector(center)
    for row,z in enumerate([-.37*h,.37*h]):
        for j in range(36):
            theta=(j+row*.45)*math.tau/36;half=math.tau/36*.42;verts=[]
            for rr in [R*.997,R+.004]:
                for zz,aa in [(-h*.22,-half),(h*.22,-half+.025),(h*.22,half),(-h*.22,half-.025)]:
                    a=theta+aa;verts.append(tuple(c+q@Vector((rr*math.cos(a),rr*math.sin(a),z+zz))))
            mesh('Tire %02d | molded tread block'%i,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],rubber,'tire tread')
    if upright:
        q=Vector(axis).to_track_quat('Z','Y');c=Vector(center);a=Vector(axis)
        lathe('Wheel %02d | hollow alloy barrel'%i,[(.58*R,-h*.83),(.62*R,-h*.83),(.62*R,h*.83),(.58*R,h*.83)],steel,center,40,axis,'wheel rim')
        for side in [-1,1]:
            z=side*h*.80;cface=c+a*z
            cylinder('Wheel | hub',cface,R*.14,.04,steel,axis,20,'wheel rim')
            for k in range(5):
                theta=k*math.tau/5;direction=q@Vector((math.cos(theta),math.sin(theta),0))
                # Machined spokes have depth and leave five genuine open windows.
                mid=cface+direction*R*.34
                spoke=box('Wheel | forged spoke',mid,(R*.45,R*.083,.035),steel,.007,category='wheel rim')
                spoke.rotation_mode='QUATERNION';spoke.rotation_quaternion=q@Matrix.Rotation(theta,3,'Z').to_quaternion()
                cylinder('Wheel | lug',cface+direction*R*.105+a*.025,.012,.02,dark,axis,8,'wheel rim')
        counts['alloy wheels']+=1

def drum(center,R,H,paint,striped=False):
    x,y,z=center;bottom=z-H/2;start=len(created)
    profile=[(0,0),(R*.95,0),(R,.018),(R,.08*H),(R*1.025,.085*H),(R*1.025,.105*H),(R,.115*H),(R,.36*H),(R*1.025,.37*H),(R*1.025,.39*H),(R,.40*H),(R,.65*H),(R*1.025,.66*H),(R*1.025,.68*H),(R,.69*H),(R,.96*H),(R*.98,H),(0,H)]
    body=lathe('Drum | rolled steel body',profile,paint,(x,y,bottom),40,category='drum')
    if striped:
        body.data.materials.append(ivory)
        for f in body.data.polygons:
            if bottom+H*.36<f.center.z<bottom+H*.65:f.material_index=1
    # Lid, rolled top seam and two bung plugs are separate solids.
    lathe('Drum | rolled top lip',[(R*.91,H-.018),(R*1.01,H-.018),(R*1.01,H+.01),(R*.91,H+.01)],dark,(x,y,bottom),40,category='drum')
    cylinder('Drum | lid',(x,y,bottom+H-.015),R*.92,.016,paint,segments=32,category='drum')
    for dx,rr in [(.48*R,.043),(-.48*R,.023)]:cylinder('Drum | bung',(x+dx,y,bottom+H+.01),rr,.018,steel,segments=12,category='drum')
    counts['drums']+=1
for row in source('CRIB1_BARREL'):
    drum(row['center'],sum(row['size'][:2])/4,row['size'][2],blue)
for row in source('CRIB1_OBJECTS01_TEXTURE'):
    x,y,z=row['center'];sx,sy,sz=row['size'];u0,v0=row['uvMin'];u1,v1=row['uvMax']
    if row['faces']==22 and sz>.9:drum(row['center'],.34,sz,red,True)

# Pressure bottles and portable extinguishers: rounded shoulders, necks, valves, gauges, handles and hoses.
def bottle(center,R,H,paint,category):
    x,y,z=center;b=z-H/2
    profile=[(0,0),(.75*R,0),(R,.05*H),(R,.77*H),(.94*R,.84*H),(.7*R,.90*H),(.35*R,.94*H),(.35*R,H),(0,H)]
    lathe(category+' | pressure vessel',profile,paint,(x,y,b),32,category=category)
    cylinder(category+' | valve',(x,y,b+H+.025),R*.25,.065,brass,segments=16,category=category)
    if category=='gas cylinder':
        cylinder('Cylinder | valve wheel',(x,y,b+H+.09),R*.46,.025,red,segments=16,category=category)
        cylinder('Cylinder | gauge',(x,y-R*.5,b+H+.02),.035,.028,ivory,(0,-1,0),16,category)
    else:
        box('Extinguisher | squeeze lever',(x,y,b+H+.085),(.14,.025,.022),dark,.004,category=category)
        box('Extinguisher | instruction plate',(x,y-R-.002,b+H*.51),(R*1.15,.009,H*.27),ivory,.002,category=category)
    tube(category+' | hose',[(x+R*.25,y,b+H),(x+R*1.4,y,b+H*.92),(x+R*1.7,y,b+H*.58),(x+R*1.55,y,b+H*.20)],R*.095,rubber,category)
    counts[category]+=1
for x,y in [(.93,8.21),(1.47,8.12)]:bottle((x,y,.78),.17,1.53,dark,'gas cylinder')
for row in source('CRIB1_OBJECTS01_TEXTURE'):
    if row['faces']==38:bottle(row['center'],.12,row['size'][2]*.80,red,'extinguisher')

# The eleven alpha cards become sagging physical cables with circular cross-sections.
for row in source('CRIB1_CABLE'):
    lo,hi=row['min'],row['max'];major=0 if row['size'][0]>row['size'][1] else 1;other=1-major
    pts=[]
    for i in range(25):
        t=i/24;p=list(row['center']);p[major]=lo[major]+t*(hi[major]-lo[major]);p[2]=hi[2]-(hi[2]-lo[2])*4*t*(1-t);pts.append(p)
    tube('Cable | sagging round insulation',pts,.018,rubber,'cable',8);counts['cables']+=1

# Card fan textures are replaced by deep housings, blades, hubs and guard rods.
for row in source('SFX_CRIB1_FAN'):
    c=Vector(row['center']);axis=Vector((1,0,0)) if row['size'][0]<.1 else Vector((0,-1,0));q=axis.to_track_quat('Z','Y');R=.56
    lathe('Fan | cylindrical housing',[(R,-.10),(R+.055,-.10),(R+.055,.10),(R,.10)],dark,c,40,axis,'fan')
    cylinder('Fan | motor',c,.12,.20,steel,axis,24,'fan')
    for k in range(5):
        a=k*math.tau/5
        blade=box('Fan | pitched blade',c+q@Vector((.31*math.cos(a),.31*math.sin(a),0)),(.45,.17,.035),steel,.016,category='fan');blade.rotation_mode='QUATERNION';blade.rotation_quaternion=q@Matrix.Rotation(a,3,'Z').to_quaternion()
    for rr in [.25,.43,.58]:lathe('Fan | guard ring',[(rr-.007,.12),(rr+.007,.12),(rr+.007,.134),(rr-.007,.134)],steel,c,32,axis,'fan')
    for k in range(8):
        a=k*math.tau/8;tube('Fan | guard spoke',[c+q@Vector((.12*math.cos(a),.12*math.sin(a),.13)),c+q@Vector((.58*math.cos(a),.58*math.sin(a),.13))],.006,steel,'fan',6)
    counts['fans']+=1

# Workshop furniture: drawer fronts, finger pulls, casters and a real open-legged workbench.
def cabinet(center,angle):
    start=len(created);box('Tool cabinet | carcass',(0,0,.35),(.85,.46,.56),dark,.02,category='tool cabinet')
    box('Tool cabinet | top',(0,0,.655),(.9,.5,.05),steel,.012,category='tool cabinet')
    for k in range(5):
        z=.135+k*.102;box('Tool cabinet | drawer',(0,-.24,z),(.79,.035,.09),red,.008,category='tool cabinet');box('Tool cabinet | pull',(0,-.27,z+.025),(.57,.03,.013),steel,.003,category='tool cabinet')
    for x in [-.32,.32]:
        for y in [-.15,.15]:cylinder('Tool cabinet | caster',(x,y,.055),.053,.036,rubber,(1,0,0),16,'tool cabinet')
    transform_parts(start,center,angle);counts['tool cabinets']+=1
cabinet((-6.11,-8.66,0),math.pi-.63);cabinet((-4.95,-9.35,0),math.pi-.34)
box('Workbench | thick top',(-2.78,-10.82,.78),(3,.60,.075),wood,.014,category='workbench')
box('Workbench | lower shelf',(-2.78,-10.82,.18),(2.86,.51,.045),wood,.01,category='workbench')
for x in [-4.18,-2.78,-1.38]:
    for y in [-11.04,-10.60]:box('Workbench | steel leg',(x,y,.39),(.05,.05,.74),dark,.007,category='workbench')
box('Bench vise | base',(-1.6,-10.8,.855),(.28,.19,.075),blue,.012,category='workbench')
for x in [-1.69,-1.51]:box('Bench vise | jaw',(x,-10.8,.94),(.055,.20,.12),steel,.007,category='workbench')
cylinder('Bench vise | screw',(-1.48,-10.8,.885),.017,.36,dark,(1,0,0),16,'workbench')
tube('Bench vise | handle',[(-1.3,-10.8,.78),(-1.3,-10.8,1.0)],.012,steel,'workbench')
for i,x in enumerate([-4.04,-3.2,-2.96,-2.7]):
    cylinder('Workbench | oil tin',(x,-10.99,.92),.077,.23,[red,ivory,blue,red][i],segments=24,category='workbench');cylinder('Workbench | tin cap',(x,-10.99,1.043),.045,.025,dark,segments=16,category='workbench')
for i,x in enumerate([-3.9,-3.45,-2.95,-2.45]):box('Workbench | storage box',(x,-10.8,.34),(.30,.34,.26),card,.006,category='workbench')
for i in range(3):
    box('Workbench | wrench handle',(-3.7+i*.19,-10.76,.831),(.035,.22,.013),steel,.004,rotation=.3,category='workbench')
    lathe('Workbench | ring spanner',[(.022,-.006),(.035,-.006),(.035,.006),(.022,.006)],steel,(-3.7+i*.19,-10.64,.831),16,category='workbench')
counts['workbenches']=1

# Rebuild the two sofas with rounded individual cushions and feet, keeping the recovered arrangement.
for center,angle in [((-7.85,9.76,0),-.15),((-5.63,7.91,0),-1.03)]:
    start=len(created);box('Sofa | upholstered base',(0,0,.28),(2.3,.94,.28),fabric,.065,category='sofa')
    for x in [-.54,.54]:
        box('Sofa | seat cushion',(x,-.09,.48),(1.03,.80,.22),fabric,.075,category='sofa');box('Sofa | back cushion',(x,.35,.82),(1.03,.25,.61),fabric,.065,category='sofa')
    for x in [-1.08,1.08]:
        box('Sofa | arm',(x,0,.61),(.20,.92,.48),fabric,.065,category='sofa')
        for y in [-.35,.35]:cylinder('Sofa | foot',(x,y,.085),.036,.17,dark,segments=12,category='sofa')
    transform_parts(start,center,angle);counts['sofas']+=1

# Appliances use solid shells, separated panels, pulls, controls and a deep CRT screen.
start=len(created)
box('Fridge | body',(0,0,1.0),(.82,.76,1.96),ivory,.035,category='fridge')
for z,h in [(.72,1.32),(1.70,.54)]:
    box('Fridge | door',(0,-.41,z),(.79,.08,h),ivory,.025,category='fridge');box('Fridge | handle',(.30,-.48,z),(.035,.07,h*.45),steel,.009,category='fridge')
box('Fridge | kick vent',(0,-.405,.06),(.62,.045,.06),dark,.005,category='fridge')
transform_parts(start,(-10.84,9.88,0),.30);counts['fridges']=1
start=len(created)
box('CRT | casing',(0,0,.56),(.95,.56,.70),dark,.05,category='television')
box('CRT | front bezel',(0,-.31,.57),(.91,.075,.66),rubber,.04,category='television')
box('CRT | convex glass screen',(-.07,-.358,.59),(.68,.075,.49),glass,.055,category='television')
for z in [.4,.53]:cylinder('CRT | control knob',(.34,-.374,z),.037,.025,steel,(0,-1,0),16,'television')
for x in [-.38,.38]:box('CRT | stand leg',(x,0,.11),(.06,.36,.20),dark,.014,category='television')
transform_parts(start,(-11.33,6.87,0),-1.25);counts['televisions']=1
start=len(created)
box('Fuel pump | pedestal',(0,0,.45),(.57,.58,.85),red,.025,category='fuel pump')
box('Fuel pump | head',(0,0,1.25),(.63,.61,.77),ivory,.035,category='fuel pump')
box('Fuel pump | inset display',(0,-.322,1.41),(.43,.025,.21),glass,.01,category='fuel pump')
box('Fuel pump | control panel',(0,-.321,1.12),(.40,.025,.20),dark,.01,category='fuel pump')
for x in [-.12,0,.12]:cylinder('Fuel pump | button',(x,-.35,1.12),.025,.02,brass,(0,-1,0),12,'fuel pump')
tube('Fuel pump | hose',[(.30,.10,1.55),(.65,.10,1.30),(.67,.10,.30),(.54,-.14,.18),(.43,-.22,.85)],.03,rubber,'fuel pump',10)
box('Fuel pump | nozzle',(.40,-.22,.93),(.09,.11,.23),steel,.016,category='fuel pump')
transform_parts(start,(11.85,5.27,0),-1.57);counts['fuel pumps']=1

# Replace atlas crates and clutter with individually modeled slats, cans, bins, bricks and stacked paper.
def crate(c,size):
    x,y,z=c;sx,sy,sz=size;sz=max(sz,.18)
    for k in range(4):
        zz=z-sz/2+(k+.5)*sz/4
        for yy in [y-sy/2,y+sy/2]:box('Crate | slat',(x,yy,zz),(sx,.035,sz/4*.86),wood,.004,category='crate')
        for xx in [x-sx/2,x+sx/2]:box('Crate | end slat',(xx,y,zz),(.035,sy,sz/4*.86),wood,.004,category='crate')
    for xx in [x-sx/2+.035,x+sx/2-.035]:
        for yy in [y-sy/2+.035,y+sy/2-.035]:box('Crate | corner post',(xx,yy,z),(.055,.055,sz),wood,.004,category='crate')
    box('Crate | base',(x,y,z-sz/2),(.94*sx,.94*sy,.025),wood,.004,category='crate');counts['crates']+=1
for row in source('CRIB1_CARDBOARD'):
    c=row['center'];sx,sy,sz=row['size'];box('Carton | body',c,(sx*.9,sy*.9,sz),card,.006,category='carton')
    box('Carton | top flap',(c[0],c[1]-sy*.21,c[2]+sz/2+.008),(sx*.9,sy*.42,.014),card,.003,category='carton');box('Carton | top flap',(c[0],c[1]+sy*.21,c[2]+sz/2+.008),(sx*.9,sy*.42,.014),card,.003,category='carton');counts['cartons']+=1
for row in source('CRIB1_OBJECTS01_TEXTURE'):
    c=row['center'];x,y,z=c;sx,sy,sz=row['size'];faces=row['faces'];u0,v0=row['uvMin'];u1,v1=row['uvMax']
    if faces==22 and sz>.9 or faces==38:continue
    if faces==22:
        R=.17;H=.58
        lathe('Wastebasket | rolled rim',[(R-.012,H),(R+.012,H),(R+.012,H+.02),(R-.012,H+.02)],steel,(x,y,.015),32,category='wastebasket')
        cylinder('Wastebasket | base',(x,y,.03),R,.025,dark,segments=24,category='wastebasket')
        for i in range(16):
            a=i*math.tau/16;tube('Wastebasket | wire',[ (x+R*math.cos(a),y+R*math.sin(a),.03),(x+R*math.cos(a),y+R*math.sin(a),H)],.006,steel,'wastebasket',6)
        counts['wastebaskets']+=1
    elif u0>.49 and sz>1:
        for k in range(6):
            for j in range(2):box('Brick pile | individual brick',(x+(j-.5)*.23+(k%2)*.035,y,z-sz/2+(k+.5)*sz/6),(.22,.37,sz/6*.9),brick,.006,category='brick pile')
        counts['brick piles']+=1
    elif .30<u0<.36 and v0>-.35:
        # Source recycling-bin box is retained as a real open container with a separate rim.
        for xx in [-sx/2,sx/2]:box('Storage bin | side',(x+xx,y,z),(.035,sy,sz),blue,.009,category='storage bin')
        for yy in [-sy/2,sy/2]:box('Storage bin | side',(x,y+yy,z),(sx,.035,sz),blue,.009,category='storage bin')
        box('Storage bin | bottom',(x,y,z-sz/2),(sx,sy,.035),blue,.009,category='storage bin');counts['storage bins']+=1
    elif v0<-.65 and u0>.3:crate(c,(sx*.9,sy*.9,sz))
    elif v0<-.65:
        box('Beverage tray | base',(x,y,z-sz/2),(sx*.9,sy*.9,.04),card,.004,category='beverage tray')
        for i in range(3):
            for j in range(2):
                pos=(x+(i-1)*sx*.24,y+(j-.5)*sy*.43,z);cylinder('Beverage tray | can',pos,min(sx*.1,.055),sz*.85,ivory,segments=16,category='beverage tray')
        counts['beverage trays']+=1
    else:
        # Paper piles and lying newspapers are genuinely thin layered sheets, not upright impostors.
        num=3 if sz<.09 else 5
        for k in range(num):box('Paper | folded layer',(x+.004*(k%2),y,z-sz/2+(k+.5)*max(sz,.006)/num),(sx*.9,sy*.9,max(.002,min(.012,sz/num))),paper,0,rotation=.025*(k%2),category='paper stack')
        counts['paper piles']+=1

# Remove the four grille pictures from the vent atlas and insert recessed louvers.
for o in list(s.objects):
    if o.type!='MESH' or 'BACKDROP' not in o.name:continue
    bm=bmesh.new();bm.from_mesh(o.data);uv=bm.loops.layers.uv.active
    gone=[f for f in bm.faces if o.data.materials[f.material_index].get('sourceTextureName')=='CRIB1_VENT1' and min(loop[uv].uv.x for loop in f.loops)>=.499]
    if gone:bmesh.ops.delete(bm,geom=gone,context='FACES')
    bm.to_mesh(o.data);bm.free()
for row in source('CRIB1_VENT1'):
    if row['uvMin'][0]<.499:continue
    c=row['center'];sx,sy,sz=row['size'];W=max(sx,sy);H=sz;start=len(created)
    box('Vent | recessed back',(0,.035,0),(W,.035,H),dark,.009,category='vent grille')
    for x in [-W/2,W/2]:box('Vent | frame',(x,0,0),(.045,.12,H+.06),steel,.009,category='vent grille')
    for z in [-H/2,H/2]:box('Vent | frame',(0,0,z),(W+.06,.12,.045),steel,.009,category='vent grille')
    for k in range(7):
        o=box('Vent | louver',(0,-.025,-H/2+(k+.5)*H/7),(W-.07,.10,H/7*.75),steel,.005,category='vent grille');o.rotation_euler.x=.34
    angle=math.pi/2 if sx<.05 else (0 if c[1]>0 else math.pi)
    transform_parts(start,c,angle);counts['vent grilles']+=1
# Existing pizza boxes already have folded lids and thickness; existing rags are appropriate draped cloth surfaces.
s['solidPropsRebuilt']=True;s['triangleBudget']=125000
for i in range(len(wheel_islands)):
    pieces=[o for o in coll.objects if o.get('propCategory')=='tire tread' and o.name.startswith('Tire %02d |'%i)]
    bpy.ops.object.select_all(action='DESELECT')
    for o in pieces:o.select_set(True)
    bpy.context.view_layer.objects.active=pieces[0];bpy.ops.object.join();bpy.context.object.name='Tire %02d | complete modeled tread'%i
report={'counts':dict(counts),'removedSourceFaces':dict(removed),'createdMeshObjects':len(created),'retainedVolumetricProps':['folded pizza boxes','draped rags'],'retainedSurfaceGraphics':['graffiti','posters','banners','architectural material detail'],'methods':{'tires':'48 angular segments, hollow bore, modeled tread channel and 72 staggered solid tread blocks per tire, 3D open five-spoke rims on upright wheels','drums':'40 angular segments, rolled reinforcing ribs, lids, separate bung plugs','cables':'25 path samples and 8 radial segments; closed end caps','fans':'real housing, pitched blades, hub and guards'},'triangleBudget':125000}
(ART/'prop-rebuild-report.json').write_text(json.dumps(report,indent=2))
bpy.data.orphans_purge(do_recursive=True);bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ART/'SafeHouse-modern-remake.blend'))
s.render.resolution_percentage=60;s.cycles.samples=32;s.render.filepath=str(ART/'props-preview-draft.png')
def render():bpy.ops.render.render(write_still=True);return None
bpy.app.timers.register(render,first_interval=.2)
print(json.dumps(report))
