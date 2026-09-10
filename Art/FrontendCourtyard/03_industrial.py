"""Twin silos, bent process pipes, ladders, brackets, ducts, and yard props."""
current_col=metal

def silo(name,x,y,r,zbase,ztop):
    cylinder(name+' | rolled tank',(x,y,(zbase+ztop)/2),r,ztop-zbase,steel,64)
    for z in [zbase+.06,zbase+1.3,zbase+2.6,ztop-.1]:
        if z<ztop:torus(name+' | welded seam',(x,y,z),r+.014,.026,darksteel)
    cylinder(name+' | top rim',(x,y,ztop),r+.08,.15,steel,64)
    bpy.ops.mesh.primitive_cone_add(vertices=64,radius1=r+.04,radius2=r*.68,depth=.32,location=(x,y,ztop+.2))
    o=bpy.context.object;o.name=name+' | rain cap';o.data.materials.append(steel);move(o)
    bpy.ops.mesh.primitive_cone_add(vertices=48,radius1=.2,radius2=r,depth=1.15,location=(x,y,zbase-.575))
    o=bpy.context.object;o.name=name+' | hopper';o.data.materials.append(steel);move(o)
    cylinder(name+' | outlet',(x,y,zbase-1.3),.16,.4,darksteel)
    for dx in [-r*.72,r*.72]:
        for dy in [-r*.72,r*.72]:
            cube(name+' | steel leg',(x+dx,y+dy,zbase/2),(.14,.14,zbase),darksteel,.015)
            cube(name+' | footplate',(x+dx,y+dy,.16),(.48,.48,.16),steel,.025)
            cube(name+' | concrete pedestal',(x+dx,y+dy,.06),(.65,.65,.12),concrete,.03)
    for dy in [-r*.72,r*.72]:
        beam(name+' | diagonal brace',(x-r*.72,y+dy,.45),(x+r*.72,y+dy,zbase-.1),.055,darksteel)
        beam(name+' | diagonal brace',(x+r*.72,y+dy,.45),(x-r*.72,y+dy,zbase-.1),.055,darksteel)
    # Vertical ladder, rungs and safety hoops on the visible side.
    lx=x-r-.19
    for dy in [-.3,.3]:beam(name+' | ladder rail',(lx,y+dy,.5),(lx,y+dy,ztop+.4),.032,darksteel)
    for j in range(int((ztop-.3)/.3)):
        beam(name+' | ladder rung',(lx,y-.32,.6+j*.3),(lx,y+.32,.6+j*.3),.024,steel)
    for z in [4,5.4,6.8,8.2,9.6]:
        if z<ztop:
            pts=[(lx-.37*math.sin(t),y+.4*math.cos(t),z) for t in [i*math.pi/12 for i in range(13)]]
            path(name+' | ladder cage hoop',pts,.024,darksteel)
    cube(name+' | data plate',(x,y-r-.032,zbase+1),(.42,.03,.25),darksteel,.01)

silo('Silo A | main tall zinc hopper',5.6,11.05,1.65,3.7,10.15)
silo('Silo B | smaller hopper',13.1,12.25,1.22,3.2,7.7)

def pipe(name,points,r):
    path(name+' | continuous run',points,r,steel)
    for a,b in zip(points[:-1],points[1:]):
        a,b=Vector(a),Vector(b)
        if (b-a).length>2:
            direction=(b-a).normalized()
            for t in [.15,.6,.9]:
                p=a.lerp(b,t)
                o=cylinder(name+' | flange',p,r*1.14,.1,darksteel,32)
                o.rotation_euler=direction.to_track_quat('Z','Y').to_euler()

pipe('Cross-yard duct | high diagonal',[(-18.4,11.2,8),(-16.8,11.0,8),(-4.3,10.9,6.6),(-3.4,10.8,6.65),(4.7,13.6,10.7),(5.7,14.2,10.9),(8.5,15,10.9)],.36)
pipe('West process duct | lower run',[(-18.3,11.2,6.65),(-17.2,11.05,6.55),(-4.2,10.6,5.5),(-3.6,10.5,5.5)],.24)
pipe('Silo B | elbow feed',[(13.1,12.25,7.6),(13.1,12.25,8.55),(12.9,12.25,8.85),(8.8,12.2,8.85)],.36)
pipe('East process duct | low return',[(25,13.9,5.9),(17.7,13.9,5.9),(17.05,13.75,5.7),(16.8,12.3,5.7),(16.3,11.9,5.7),(10,11.9,5.7),(9.5,11.95,6),(9.5,14.8,6)],.43)

# Rectangular galvanized ventilation trunk and faceted transition above hopper.
cube('East ventilation | tall rectangular trunk',(16.2,14.1,10.45),(2.05,1.1,4.1),steel,.025)
mesh('East ventilation | diamond stiffening',[(15.175,13.53,8.4),(17.225,13.53,8.4),(17.225,13.53,12.5),(15.175,13.53,12.5),(16.2,13.36,10.45)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],steel)
for z in [8.4,10.45,12.5]:cube('East ventilation | strap',(16.2,13.51,z),(2.13,.08,.085),darksteel)
cube('West downspout',(-17.8,11.36,5.7),(.12,.17,11.4),darksteel,.02)
for z in [1.5,4,7,10]:cube('West downspout | bracket',(-17.8,11.3,z),(.28,.26,.06),steel)

# Platform behind the silos, with open steel railings.
cube('Service catwalk | deck',(9.5,14.25,3),(10,.95,.13),darksteel,.025)
for x in [4.5,6.5,8.5,10.5,12.5,14.5]:
    beam('Catwalk | guard post',(x,13.78,3),(x,13.78,4),.035,steel)
for z in [3.5,4]:beam('Catwalk | handrail',(4.5,13.78,z),(14.5,13.78,z),.03,steel)

current_col=detail
for x,y in [(-16.1,10.65),(-15.3,10.7),(-14.5,10.8),(20.8,13.3),(21.6,13.1)]:
    cylinder('Steel drum | weathered body',(x,y,.53),.32,1.02,random.choice([paint,rust2,rust]),32)
    for z in [.1,.31,.78,1.03]:torus('Steel drum | rolled band',(x,y,z),.323,.024,darksteel)
    cylinder('Steel drum | lid',(x,y,1.047),.316,.024,darksteel)
    cylinder('Steel drum | bung',(x+.12,y,1.066),.042,.035,steel,16)

def crate(x,y,z,size=1):
    cube('Shipping crate | body',(x,y,z+size/2),(size,size,size),wood,.02)
    for dx in [-.42,.42]:
        cube('Shipping crate | upright',(x+dx*size,y-size/2-.02,z+size/2),(.095*size,.075,size),wood,.01)
    for zz in [.08,.92]:
        cube('Shipping crate | rail',(x,y-size/2-.055,z+zz*size),(size,.08,.095*size),wood,.01)
    for dx in [-.25,0,.25]:cube('Shipping crate | board seam',(x+dx*size,y-size/2-.006,z+size/2),(.008,.01,size-.14),darksteel)
crate(-4.8,11.0,0,1.15);crate(-5.5,9.8,0,.9);crate(-4.8,11,1.15,.75)
crate(22,13,0,1.35);crate(23.4,13.2,0,1.1)

# Bollards, exposed conduit and an overhead slack service cable.
for x,y in [(-12.1,10.2),(-6.3,10.2),(18.4,11.7),(20.3,11.7)]:
    cylinder('Yard bollard',(x,y,.6),.075,1.2,darksteel,16)
    cylinder('Yard bollard | pale band',(x,y,.9),.077,.12,paint,16)
for z in [4.7,4.87]:path('West facade | surface conduit',[(-18,11.39,z),(-12,11.39,z),(-11.8,11.39,z+.2),(-2,11.39,z+.2)],.027,darksteel)
path('Suspended utility cable',[(-18.3,4,10.5),(-12,6,9.1),(-6,8,8.3),(0,10,8.1),(6,12,8.9),(16,14,11.1)],.019,black)

# Distressed wall tags made from thin paint strokes, separate from cladding.
tagwhite=weather('Graffiti | chalk silver',(.18,.18,.12),(.6,.59,.4),12)
tagred=weather('Graffiti | faded vermilion',(.09,.027,.015),(.32,.09,.035),10)
taggreen=weather('Graffiti | faded acid green',(.04,.075,.02),(.2,.3,.055),10)
for x,y,z,sz,col,word in [(-12.9,11.38,4.8,.7,taggreen,'SHIFT'),(-3.4,11.39,5.05,.52,tagred,'ROCKPORT'),(20.9,14.39,1.12,.87,tagwhite,'NO LIMIT'),(-13.2,11.33,1.55,.45,tagred,'05')]:
    o=text_obj('Wall tag | '+word,word,(x,y,z),sz,col);o.data.shear=.27
    for k in range(9):
        px=x+random.uniform(-1.4,1.4)*sz
        path('Wall tag | paint drip',[(px,y-.012,z+.05),(px+random.uniform(-.015,.015),y-.012,z-random.uniform(.06,.45))],.007,col)

print('INDUSTRIAL DETAILS READY',len(metal.objects),len(detail.objects))
