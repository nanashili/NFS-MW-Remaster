"""Warehouse shells, visible corrugated profiles, facade structure, and windows."""
current_col=arch

def corrugated(name,x1,x2,y,z1,z2,mat,axis='X'):
    verts=[]; faces=[]
    count=max(2,int((x2-x1)/.065))
    for i in range(count+1):
        u=x1+(x2-x1)*i/count
        depth=(.027 if i%4 in (1,2) else -.027)
        for z in (z1,z2):
            verts.append((u,y+depth,z) if axis=='X' else (y+depth,u,z))
    for i in range(count):faces.append((i*2,i*2+2,i*2+3,i*2+1))
    return mesh(name,verts,faces,mat)

# Three stepped masses form the enclosed yard. Facing surfaces have real ribs.
cube('West mill | main masonry shell',(-11,16,5.9),(22,8,11.8),brick,.06)
cube('East mill | raised shell',(13.6,19,6.65),(23.2,8,13.3),brick,.06)
cube('West return | side wing',(-21,4.5,6),(5,23,12),brick,.05)

for i in range(11):
    x=-22+i*2
    corrugated('West facade | ribbed sheet %02d'%i,x,x+1.98,11.94,4.0,11.8,random.choice([rust,rust,rust2]))
for i in range(12):
    x=2+i*2
    corrugated('East facade | ribbed sheet %02d'%i,x,x+1.98,14.94,1.1,13.3,random.choice([rust,rust2]))
for i in range(11):
    corrugated('West return | ribbed sheet %02d'%i,-7+i*2,-5.02+i*2,-18.46,1.1,12,random.choice([rust,rust2]),axis='Y')

for z in [1.1,4,7.8,11.6]:
    cube('West facade | horizontal steel girt',(-11,11.85,z),(22.3,.2,.19),darksteel,.025)
for z in [1.1,4.7,8.8,13.1]:
    cube('East facade | horizontal steel girt',(14,14.83,z),(24.2,.22,.18),darksteel,.025)
for x in [-21.8,-17.8,-13.8,-9.8,-5.8,-1.8]:
    cube('West facade | weathered column',(x,11.69,5.9),(.22,.33,11.8),darksteel,.025)
    cube('West facade | concrete footing',(x,11.5,.6),(.55,.55,1.2),concrete,.04)
for x in [2.1,6.1,10.1,14.1,18.1,22.1,25.7]:
    cube('East facade | upright',(x,14.63,6.65),(.22,.42,13.3),darksteel,.025)

# Shallow pitched roof and overhanging eaves.
mesh('West mill | pitched roof',[(-22.5,11.1,11.85),(.5,11.1,11.85),(.5,16,13.25),(-22.5,16,13.25),(-22.5,20.5,11.85),(.5,20.5,11.85)],[(0,1,2,3),(3,2,5,4)],darksteel)
cube('West mill | rolled eave',(-11,11.1,11.82),(23.2,.18,.25),steel,.04)
cube('East mill | roof coping',(13.7,14.6,13.42),(24,.65,.25),steel,.04)
cube('East mill | raised flat roof',(13.6,19,13.4),(23.8,8.8,.18),darksteel,.03)
cube('West return | eave',(-18.3,3.6,12.1),(.4,24.4,.22),steel,.03)

def window(name,x,y,z,w,h,nx,ny):
    cube(name+' | deep recess',(x,y+.08,z),(w+.28,.17,h+.26),black,.025)
    cube(name+' | dusty glazing',(x,y-.02,z),(w,.055,h),glass)
    for i in range(nx+1):
        cube(name+' | vertical mullion',(x-w/2+i*w/nx,y-.08,z),(.048,.09,h+.08),darksteel)
    for j in range(ny+1):
        cube(name+' | horizontal mullion',(x,y-.085,z-h/2+j*h/ny),(w+.09,.1,.043),darksteel)
    cube(name+' | sill',(x,y-.17,z-h/2-.08),(w+.4,.4,.13),concrete,.02)
    # Sporadic patched / unlit panes break the perfect grid.
    for k in range(max(1,nx*ny//8)):
        ix=random.randrange(nx); iz=random.randrange(ny)
        cube(name+' | replacement pane',(x-w/2+(ix+.5)*w/nx,y-.055,z-h/2+(iz+.5)*h/ny),(w/nx-.06,.018,h/ny-.055),random.choice([rust2,darksteel,paint]))

window('West upper | large factory window',-15,11.71,9.05,5,3.5,9,6)
window('West upper | strip glazing',-6.15,11.7,9.25,4.4,1.65,9,3)
window('West lower | service window',-15.7,11.66,2.35,3.4,2.1,6,4)
window('West lower | small workshop',-3.3,11.65,2.9,2.35,1.85,5,4)
window('East upper | factory strip',18.3,14.65,10.8,6.8,1.9,13,4)

# Recessed roller door, guides, slatted face, loading pad and canopy.
cube('Loading bay | dark reveal',(-9.15,11.64,1.95),(5.1,.16,3.9),black)
for j in range(26):
    cube('Loading bay | shutter slat',(-9.15,11.48,.2+j*.139),(4.8,.09,.124),rust2,.011)
for x in [-11.7,-6.6]:cube('Loading bay | door guide',(x,11.39,2),(.16,.25,4),darksteel,.025)
cube('Loading bay | threshold',(-9.15,11.18,.1),(5.4,1.1,.2),concrete,.03)
o=cube('Loading bay | corrugated canopy',(-9.15,10.95,4.2),(6.1,2.2,.14),rust,.025); o.rotation_euler.x=.12
for x in [-11.7,-6.6]:beam('Loading bay | canopy bracket',(x,11.7,3.4),(x,10.05,4.08),.065,darksteel)
cube('Staff entry | recess',(-.5,11.68,1.35),(1.15,.16,2.7),black)
cube('Staff entry | peeling metal door',(-.5,11.52,1.33),(1,.08,2.55),rust2,.02)
cube('Staff entry | door handle',(-.86,11.39,1.32),(.035,.1,.19),steel,.012)
text_obj('Loading bay | stencilled number','03',(-11.18,11.38,2.66),.6,paint)
cube('Yard | concrete back curb',(6.5,14.1,.2),(36,.65,.4),concrete,.06)

# Some individual exposed bricks and lintels, intentionally irregular.
for row in range(7):
    for col in range(16):
        x=-21.5+col*.5+(row%2)*.25
        if -17.6<x<-13.8:continue
        cube('West base | masonry bond',(x,11.89,.19+row*.23),(.465,.055,.195),random.choice([brick,concrete,brick]))

print('ARCHITECTURE READY',len(arch.objects))
