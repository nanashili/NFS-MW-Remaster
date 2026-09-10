"""Reference-led 3D refinement; run from SafeHouse-before-reference-refinement.blend.
Fine tread, cloth and chipped paint are native Cycles material bakes, not image cards.
"""
import bpy,bmesh,json,math
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
from collections import Counter
ROOT=Path('/Users/tihan-nico/NFS MW Remaster');ART=ROOT/'Art/FrontendRooms/SafeHouse';TEX=ART/'ReferenceTextures';TEX.mkdir(exist_ok=True)
s=bpy.context.scene
assert s['room']=='SafeHouse' and not s.get('referencePropsRefined',False)
audit=json.loads((ART/'prop-audit-source.json').read_text())
coll=bpy.data.collections.new('03 | Reference proportions and surfaces');s.collection.children.link(coll)
counts=Counter();created=[]
helpers=(ROOT/'Tools/FrontendRooms/rebuild_safehouse_props.py').read_text()
exec(helpers[helpers.index('def material('):helpers.index('# Capture full wheel islands')])
fabric.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.13,.14,.15,1)
rubber.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.018,.019,.020,1)
rubber.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.64
seam=material('upholstery seam',(.085,.09,.10),0,.88)
green=material('cylinder green label',(.025,.14,.067),0,.65)

# Recover actual placements from the preceding round tire geometry.
tires=[]
for o in list(s.objects):
    if o.get('propCategory')!='tire':continue
    pts=np.array([tuple(o.matrix_world@v.co) for v in o.data.vertices]);center=pts.mean(axis=0)
    values,axes=np.linalg.eigh(np.cov((pts-center).T));axis=axes[:,0]
    if axis[2]<0:axis=-axis
    axial=(pts-center)@axis;R=float(np.linalg.norm((pts-center)-axial[:,None]*axis,axis=1).max())
    tires.append({'center':center.tolist(),'axis':axis.tolist(),'R':R,'width':float(np.ptp(axial)),'upright':abs(axis[2])<.75})
for o in list(s.objects):
    if o.get('propCategory') in {'tire','tire tread','wheel rim','drum','sofa','gas cylinder'}:bpy.data.objects.remove(o,do_unlink=True)

def uv_lathe(name,profile,mat,center,segments=64,axis=(0,0,1),category='round prop'):
    o=lathe(name,profile,mat,center,segments,axis,category)
    uv=o.data.uv_layers.new(name='UVMap');zlo=min(z for r,z in profile);zhi=max(z for r,z in profile)
    for f in o.data.polygons:
        row=f.index//segments;col=f.index%segments
        for loop in f.loop_indices:
            vi=o.data.loops[loop].vertex_index;rr,cc=divmod(vi,segments)
            u=cc/segments
            if col==segments-1 and cc==0:u=1
            uv.data[loop].uv=(u,(profile[rr][1]-zlo)/(zhi-zlo))
    return o

# Node helpers for portable material bakes.
def shader(name):
    m=material(name,(.1,.1,.1));n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF')
    uv=n.new('ShaderNodeTexCoord');sep=n.new('ShaderNodeSeparateXYZ');l.new(uv.outputs['UV'],sep.inputs[0])
    def op(kind,a,b=0):
        x=n.new('ShaderNodeMath');x.operation=kind
        for i,v in enumerate([a,b]):
            if isinstance(v,(int,float)):x.inputs[i].default_value=v
            else:l.new(v,x.inputs[i])
        return x.outputs[0]
    def mix(f,a,b):
        x=n.new('ShaderNodeMixRGB');x.blend_type='MIX';l.new(f,x.inputs[0])
        for i,v in enumerate([a,b],1):
            if isinstance(v,tuple):x.inputs[i].default_value=(*v,1)
            else:l.new(v,x.inputs[i])
        return x.outputs[0]
    def noise(scale,detail=2):
        x=n.new('ShaderNodeTexNoise');x.inputs['Scale'].default_value=scale;x.inputs['Detail'].default_value=detail;l.new(uv.outputs['UV'],x.inputs['Vector']);return x.outputs['Fac']
    return m,n,l,p,sep.outputs['X'],sep.outputs['Y'],op,mix,noise

tread,n,l,p,u,v,op,mix,noise=shader('reference road tread')
im=bpy.data.images.load(str(TEX/'user_tire_tread_Normal.png'),check_existing=True);im.colorspace_settings.name='Non-Color'
tex=n.new('ShaderNodeTexImage');tex.image=im;normal=n.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.75;l.new(tex.outputs[0],normal.inputs['Color']);l.new(normal.outputs[0],p.inputs['Normal'])
p.inputs['Base Color'].default_value=(.018,.019,.020,1);p.inputs['Roughness'].default_value=.67

paint_materials=[]
for name,is_drum in [('weathered black red drum',True),('weathered oxide red cylinder',False)]:
    m,n,l,p,u,v,op,mix,noise=shader(name)
    grain=noise(47,3);patch=noise(11,4)
    chips=op('GREATER_THAN',op('ADD',op('MULTIPLY',grain,.65),op('MULTIPLY',patch,.35)),.59)
    edge=op('MAXIMUM',op('LESS_THAN',v,.024),op('GREATER_THAN',v,.975))
    if is_drum:
        band=op('MULTIPLY',op('GREATER_THAN',v,.335),op('LESS_THAN',v,.68))
        base=mix(band,(.014,.018,.021),(.13,.018,.015))
        for position in [.335,.68]:edge=op('MAXIMUM',edge,op('LESS_THAN',op('ABSOLUTE',op('SUBTRACT',v,position)),.012))
    else:base=(.12,.025,.02)
    wear=op('MAXIMUM',chips,op('MULTIPLY',edge,op('GREATER_THAN',grain,.44)))
    faded=mix(op('MULTIPLY',patch,.65),base,(.032,.03,.025))
    col=mix(wear,faded,(.12,.115,.095));l.new(col,p.inputs['Base Color'])
    l.new(op('ADD',.48,op('MULTIPLY',grain,.28)),p.inputs['Roughness']);p.inputs['Metallic'].default_value=.58
    b=n.new('ShaderNodeBump');b.inputs['Distance'].default_value=.0009;l.new(grain,b.inputs['Height']);l.new(b.outputs[0],p.inputs['Normal']);paint_materials.append(m)
drum_mat,cylinder_mat=paint_materials
lid_mat,n,l,p,u,v,op,mix,noise=shader('worn drum lid steel')
grain=noise(35,3);l.new(mix(grain,(.02,.024,.025),(.12,.115,.095)),p.inputs['Base Color']);p.inputs['Roughness'].default_value=.65;p.inputs['Metallic'].default_value=.65

cloth,n,l,p,u,v,op,mix,noise=shader('grey woven upholstery')
weave=op('MULTIPLY',op('SINE',op('MULTIPLY',u,math.tau*230)),op('SINE',op('MULTIPLY',v,math.tau*230)))
grain=noise(145,2);colour=mix(grain,(.09,.10,.115),(.18,.19,.205));l.new(colour,p.inputs['Base Color'])
b=n.new('ShaderNodeBump');b.inputs['Distance'].default_value=.0004;l.new(weave,b.inputs['Height']);l.new(b.outputs[0],p.inputs['Normal']);p.inputs['Roughness'].default_value=.9

for i,t in enumerate(tires):
    c=Vector(t['center']);axis=Vector(t['axis']);R=t['R'];h=t['width']*.5
    # Nearly flat tread, compact shoulders and restrained, almost planar sidewalls.
    profile=[(.755*R,-.91*h),(.77*R,-h),(.87*R,-1.012*h),(.96*R,-.95*h),(.992*R,-.80*h)]
    for z in [-.69,-.472,-.23,0,.23,.472,.69]:
        if z in [-.472,0,.472]:
            half=.029 if z==0 else .016
            profile.extend([(R,(z-half-.006)*h),(R-.0045,(z-half)*h),(R-.0045,(z+half)*h),(R,(z+half+.006)*h)])
        else:profile.append((R,z*h))
    profile.extend([(.992*R,.80*h),(.96*R,.95*h),(.87*R,1.012*h),(.77*R,h),(.755*R,.91*h),(.75*R,.71*h),(.75*R,-.71*h)])
    o=uv_lathe('Reference tire %02d | road carcass'%i,profile,rubber,c,64,axis,'tire');o.data.materials.append(tread)
    # Supplied image: horizontal axis spans tire width; vertical axis repeats along rolling direction.
    for item in o.data.uv_layers.active.data:
        angular,axial=item.uv;item.uv=((axial-.104743)/.790514,angular*26)
    for f in o.data.polygons:
        row=f.index//64
        if 4<=row<len(profile)-8:f.material_index=1
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    sharp=o.modifiers.new('Keep tread channels crisp','EDGE_SPLIT');sharp.split_angle=.60;sharp.use_edge_angle=True;bpy.ops.object.modifier_apply(modifier=sharp.name)
    o['tireCenter']=list(c);o['tireAxis']=list(axis);o['radius']=R;counts['tires']+=1
    # Bare lower tires remain hollow; visible top wheels and standing wheels receive alloys.
    above=any(not other['upright'] and math.hypot(other['center'][0]-c.x,other['center'][1]-c.y)<.3 and other['center'][2]>c.z+.06 for other in tires)
    if t['upright'] or not above:
        q=axis.to_track_quat('Z','Y');rim=.757*R
        uv_lathe('Reference wheel | rolled alloy barrel',[(rim*.965,-h*.91),(rim,-h*.91),(rim,h*.91),(rim*.965,h*.91)],steel,c,64,axis,'wheel rim')
        for side in [-1,1] if t['upright'] else [1]:
            z=side*h*.94;front=c+axis*z
            uv_lathe('Reference wheel | polished bead lip',[(rim*.93,-.006),(rim*1.018,-.006),(rim*1.022,.002),(rim*1.005,.009),(rim*.95,.009)],steel,front,64,axis,'wheel rim')
            cylinder('Reference wheel | center cap',front-axis*(side*.043),R*.14,.026,steel,axis,24,'wheel rim')
            for k in range(10):
                a=k*math.tau/10;verts=[]
                # Sculpted tapered spokes curve and deepen toward the central hub.
                for depth in [-.012,.012]:
                    for rad,ang,zz in [(R*.135,a-.11,z-side*.052),(R*.39,a-.10,z-side*.027),(rim*.96,a+.015,z),(rim*.96,a+.068,z),(R*.39,a+.055,z-side*.027),(R*.135,a+.13,z-side*.052)]:
                        verts.append(tuple(c+q@Vector((rad*math.cos(ang),rad*math.sin(ang),zz+depth))))
                spoke=mesh('Reference wheel | swept alloy spoke',verts,[(0,5,4,3,2,1),(6,7,8,9,10,11)]+[(j,(j+1)%6,(j+1)%6+6,j+6) for j in range(6)],steel,'wheel rim',False)
            for k in range(5):
                a=k*math.tau/5;cylinder('Reference wheel | lug bolt',front+q@Vector((R*.19*math.cos(a),R*.19*math.sin(a),-side*.03)),.011,.016,dark,axis,8,'wheel rim')
        counts['alloy wheels']+=1

def reference_drum(center,R,H):
    x,y,z=center;b=z-H/2
    profile=[(.96*R,0),(.97*R,0),(R,.014),(R,.032),(.99*R,.043),(.99*R,.32*H),(1.013*R,.333*H),(1.013*R,.345*H),(.99*R,.358*H),(.99*R,.665*H),(1.013*R,.678*H),(1.013*R,.690*H),(.99*R,.702*H),(.99*R,H-.02),(R,H-.012),(R,H),(.96*R,H)]
    uv_lathe('Reference drum | worn rolled steel',profile,drum_mat,(x,y,b),64,category='drum')
    uv_lathe('Reference drum | thin rolled top seam',[(R*.955,-.01),(R*1.01,-.01),(R*1.01,.006),(R*.955,.006)],lid_mat,(x,y,b+H),64,category='drum')
    lid=cylinder('Reference drum | recessed lid',(x,y,b+H-.018),R*.961,.014,lid_mat,segments=48,category='drum')
    uv=lid.data.uv_layers.new(name='UVMap')
    for loop in lid.data.loops:
        pt=lid.data.vertices[loop.vertex_index].co;uv.data[loop.index].uv=((pt.x-x)/(R*2)+.5,(pt.y-y)/(R*2)+.5)
    for dx,r in [(.50*R,.037),(-.48*R,.021)]:
        cylinder('Reference drum | bung rim',(x+dx,y,b+H-.001),r*1.16,.012,dark,segments=16,category='drum')
        cylinder('Reference drum | hex bung',(x+dx,y,b+H+.009),r,.014,steel,segments=6,category='drum')
    counts['drums']+=1
for row in source('CRIB1_BARREL'):reference_drum(row['center'],sum(row['size'][:2])/4,row['size'][2])
for row in source('CRIB1_OBJECTS01_TEXTURE'):
    if row['faces']==22 and row['size'][2]>.9:reference_drum(row['center'],.34,row['size'][2])

for x,y in [(.93,8.21),(1.47,8.12)]:
    R=.15;H=1.49;b=.01
    profile=[(0,0),(R*.93,0),(R,.016),(R,.05),(R,H*.89),(R*.96,H*.915),(R*.81,H*.94),(R*.52,H*.965),(R*.35,H*.976),(R*.35,H),(0,H)]
    uv_lathe('Reference gas cylinder | rounded shoulder',profile,cylinder_mat,(x,y,b),48,category='gas cylinder')
    uv_lathe('Reference gas cylinder | foot ring',[(R*.96,0),(R*1.015,0),(R*1.015,.038),(R*.96,.038)],cylinder_mat,(x,y,b),48,category='gas cylinder')
    cylinder('Reference cylinder | brass valve stem',(x,y,b+H+.045),.024,.09,brass,segments=16,category='gas cylinder')
    cylinder('Reference cylinder | valve outlet',(x+.031,y,b+H+.037),.014,.065,brass,(1,0,0),16,'gas cylinder')
    cylinder('Reference cylinder | handwheel',(x,y,b+H+.102),.046,.014,dark,segments=20,category='gas cylinder')
    for a in [0,math.pi/2]:box('Reference cylinder | handwheel grip',(x,y,b+H+.109),(.096,.014,.012),dark,.003,rotation=a,category='gas cylinder')
    box('Reference cylinder | green diamond',(x,y-R-.002,b+H*.69),(.095,.002,.095),green,.001,category='gas cylinder').rotation_euler.y=math.pi/4
    counts['gas cylinders']+=1

def cushion(name,loc,size,back=False):
    o=box(name,loc,size,cloth,.065,category='sofa')
    # Subdivide planar panels and gently puff their centers; tuck the back center into a button.
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=3,use_grid_fill=True);bm.to_mesh(o.data);bm.free()
    sx,sy,sz=size
    for vtx in o.data.vertices:
        x,y,z=vtx.co
        if back and y<-.3*sy:
            envelope=max(0,1-(x/(sx*.5))**2)*max(0,1-(z/(sz*.5))**2)
            vtx.co.y-=.025*envelope;vtx.co.y+=.048*math.exp(-((x/.07)**2+(z/.075)**2))
        elif not back and z>.3*sz:
            vtx.co.z+=.018*max(0,1-(x/(sx*.5))**2)*max(0,1-(y/(sy*.5))**2)
    for f in o.data.polygons:f.use_smooth=True
    # Physical seam piping follows the visible cushion perimeter.
    px,py,pz=loc;pts=[]
    if back:
        for a in range(33):
            t=a*math.tau/32;pts.append((px+math.copysign(abs(math.cos(t))**.22,t and math.cos(t) or 1)*(sx*.5-.018),py-sy*.5-.003,pz+math.copysign(abs(math.sin(t))**.22,math.sin(t))*(sz*.5-.018)))
        cylinder('Sofa | tuft button',(px,py-sy*.5+.016,pz),.014,.007,seam,(0,-1,0),12,'sofa')
        for dx,dz in [(-1,-1),(-1,1),(1,-1),(1,1)]:
            tube('Sofa | diagonal stitched tuck',[(px+dx*.025,py-sy*.5+.005,pz+dz*.025),(px+dx*sx*.23,py-sy*.5-.008,pz+dz*sz*.23),(px+dx*(sx*.5-.035),py-sy*.5+.01,pz+dz*(sz*.5-.035))],.0011,seam,'sofa',4)
    else:
        for a in range(33):
            t=a*math.tau/32;pts.append((px+math.copysign(abs(math.cos(t))**.22,math.cos(t))*(sx*.5-.013),py+math.copysign(abs(math.sin(t))**.22,math.sin(t))*(sy*.5-.013),pz+.012))
    tube('Sofa | sewn cushion piping',pts,.0018,seam,'sofa',4)
    return o

for index,(center,angle) in enumerate([((-7.85,9.76,0),-.15),((-5.63,7.91,0),-1.03)]):
    start=len(created);W=2.48 if index==0 else 2.15
    box('Sofa | fabric upholstered base',(0,0,.29),(W,.94,.24),cloth,.035,category='sofa')
    count=3 if index==0 else 2;cw=(W-.28)/count
    for j in range(count):
        x=-W/2+.14+cw*(j+.5)
        cushion('Sofa | tailored seat',(x,-.08,.475),(cw-.014,.79,.18))
        cushion('Sofa | tufted back pillow',(x,.35,.87),(cw-.012,.22,.63),True)
    for x in [-W/2+.085,W/2-.085]:
        box('Sofa | squared padded arm',(x,-.01,.635),(.17,.93,.62),cloth,.033,category='sofa')
        for y in [-.34,.34]:
            lathe('Sofa | tapered black foot',[(.032,0),(.045,.15),(0,.15),(0,0)],dark,(x,y,.015),16,category='sofa')
    if index==0:
        x=W/2-.14-cw*.5
        box('Sofa | chaise base',(x,-.88,.29),(cw+.01,1.0,.24),cloth,.035,category='sofa')
        cushion('Sofa | chaise extension',(x,-.93,.475),(cw-.015,.92,.18))
        for xx in [x-cw*.35,x+cw*.35]:lathe('Sofa | chaise foot',[(.032,0),(.045,.15),(0,.15),(0,0)],dark,(xx,-1.2,.015),16,category='sofa')
    if index==1:center=(center[0]+.42,center[1]-.32,center[2])
    for part in created[start:]:part['sofaIndex']=index
    transform_parts(start,center,angle);counts['sofas']+=1

# UVs for upholstered solids. All procedural shaders use UVs and are baked below.
for o in created:
    if o.type!='MESH' or o.data.uv_layers:continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.015);bpy.ops.object.mode_set(mode='OBJECT')

def bake_material(m,kind,res=2048):
    bpy.ops.object.select_all(action='DESELECT');bpy.ops.mesh.primitive_plane_add(size=2,location=(0,0,-100));plane=bpy.context.object;plane.data.materials.append(m)
    n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF');out=next(x for x in n if x.type=='OUTPUT_MATERIAL')
    image=bpy.data.images.new(m.name.split('|')[-1].strip().replace(' ','_')+'_'+kind,width=res,height=res,alpha=False)
    image.colorspace_settings.name='sRGB' if kind=='BaseColor' else 'Non-Color';tex=n.new('ShaderNodeTexImage');tex.image=image;n.active=tex
    if kind=='Normal':bpy.ops.object.bake(type='NORMAL')
    else:
        em=n.new('ShaderNodeEmission')
        if kind=='BaseColor':
            if p.inputs['Base Color'].is_linked:l.new(p.inputs['Base Color'].links[0].from_socket,em.inputs[0])
            else:em.inputs[0].default_value=p.inputs['Base Color'].default_value
        else:
            comb=n.new('ShaderNodeCombineColor');comb.inputs['Red'].default_value=1
            for channel,source in [('Green','Roughness'),('Blue','Metallic')]:
                if p.inputs[source].is_linked:l.new(p.inputs[source].links[0].from_socket,comb.inputs[channel])
                else:comb.inputs[channel].default_value=p.inputs[source].default_value
            l.new(comb.outputs[0],em.inputs[0])
        l.new(em.outputs[0],out.inputs['Surface']);bpy.ops.object.bake(type='EMIT');l.new(p.outputs[0],out.inputs['Surface']);n.remove(em)
        if kind=='ORM':n.remove(comb)
    image.filepath_raw=str(TEX/(image.name+'.png'));image.file_format='PNG';image.save();n.remove(tex);bpy.data.objects.remove(plane,do_unlink=True);return image

def finish_bakes():
    s.cycles.samples=16;s.render.bake.margin=12
    for m in [drum_mat,cylinder_mat,cloth,lid_mat]:
        maps={kind:bake_material(m,kind,2048 if kind!='ORM' else 1024) for kind in ['BaseColor','Normal','ORM']}
        n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF')
        for socket in ['Base Color','Normal','Roughness','Metallic']:
            for link in list(p.inputs[socket].links):l.remove(link)
        for kind,im in maps.items():
            tex=n.new('ShaderNodeTexImage');tex.image=im
            if kind=='Normal':nm=n.new('ShaderNodeNormalMap');l.new(tex.outputs[0],nm.inputs['Color']);l.new(nm.outputs[0],p.inputs['Normal'])
            elif kind=='BaseColor':l.new(tex.outputs[0],p.inputs['Base Color'])
            else:
                sep=n.new('ShaderNodeSeparateColor');l.new(tex.outputs[0],sep.inputs[0]);l.new(sep.outputs['Green'],p.inputs['Roughness']);l.new(sep.outputs['Blue'],p.inputs['Metallic'])
    s['referencePropsRefined']=True;s['triangleBudget']=220000;s.cycles.samples=64
    report={'counts':dict(counts),'referenceImages':['tire-upright.png','tire-flat.png','drum.png','sofa.png','cylinder.png'],'methods':{'tires':'64 radial segments, flat crown, three physically recessed circumferential grooves aligned to the supplied user_tire_tread_Normal.png; normal UV width across tread and 26 vertical repeats around circumference; ten sculpted spokes on visible wheels','drums':'64 radial segments, narrow rolled seams, recessed lid, hex bungs, chipped black paint and red band baked to PBR maps','cylinders':'rounded shoulders, foot ring, brass valve, handwheel, oxide red chipped paint and a green diamond marking','sofas':'gray woven fabric, puffed cushions, stitched piping, tufted backs, tapered feet and one chaise extension'},'nativeCyclesBakes':['drum paint','cylinder paint','cloth','lid'],'suppliedNormalPreserved':True,'noImageCards':True,'triangles':sum(len(p.vertices)-2 for o in s.objects if o.type=='MESH' for p in o.data.polygons)}
    (ART/'reference-refinement-report.json').write_text(json.dumps(report,indent=2));bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ART/'SafeHouse-modern-remake.blend'));print('REFERENCE_REFINEMENT_DONE '+json.dumps(report));return None
bpy.app.timers.register(finish_bakes,first_interval=.2)
print('Reference geometry complete; native material bakes queued.')
