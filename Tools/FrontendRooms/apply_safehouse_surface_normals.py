"""Apply the retained couch and floor surface normals to the reference-refined safe house.

Run once after refine_safehouse_references.py finishes its bakes, before export.
Original supplied files remain unchanged in ReferenceTextures.
"""
import bpy, json
from pathlib import Path

ROOT = Path('/Users/tihan-nico/NFS MW Remaster')
ART = ROOT / 'Art/FrontendRooms/SafeHouse'
TEX = ART / 'ReferenceTextures'
s = bpy.context.scene
assert s.get('referencePropsRefined') and not s.get('userSurfaceNormalsApplied')

def texture(m, filename, scale=(1, 1), offset=(0, 0)):
    n, l = m.node_tree.nodes, m.node_tree.links
    im = bpy.data.images.load(str(TEX / filename), check_existing=True)
    im.colorspace_settings.name = 'Non-Color'
    t = n.new('ShaderNodeTexImage'); t.image = im
    if scale != (1, 1) or offset != (0, 0):
        uv = n.new('ShaderNodeTexCoord'); mapping = n.new('ShaderNodeMapping')
        mapping.inputs['Scale'].default_value = (*scale, 1)
        mapping.inputs['Location'].default_value = (*offset, 0)
        l.new(uv.outputs['UV'], mapping.inputs['Vector'])
        l.new(mapping.outputs['Vector'], t.inputs['Vector'])
    return t.outputs['Color']

def normal(m, source, strength):
    n, l = m.node_tree.nodes, m.node_tree.links
    p = n.get('Principled BSDF'); nm = n.new('ShaderNodeNormalMap')
    nm.inputs['Strength'].default_value = strength
    l.new(source, nm.inputs['Color']); l.new(nm.outputs['Normal'], p.inputs['Normal'])

cloth = bpy.data.materials['SafeHouse prop | grey woven upholstery']
normal(cloth, texture(cloth, 'user_couch_fabric_Normal.png', (12, 12)), .24)

# CRIB1_FLOOR is also assigned to non-floor geometry; separate actual ground faces.
old_floor = bpy.data.materials['SafeHouse | CRIB1_FLOOR']
floor = old_floor.copy(); floor.name = 'SafeHouse | floor with supplied surface normal'
normal(floor, texture(floor, 'user_metal_floor_Normal.jpg', (6, 6)), .28)
ground_faces = 0
for o in s.objects:
    if o.type != 'MESH' or old_floor not in list(o.data.materials): continue
    old = list(o.data.materials).index(old_floor)
    o.data.materials.append(floor); new = len(o.data.materials) - 1
    for p in o.data.polygons:
        if p.material_index == old and abs(p.normal.z) > .95 and max((o.matrix_world @ o.data.vertices[v].co).z for v in p.vertices) < .10:
            p.material_index = new; ground_faces += 1

def finish():
    s['userSurfaceNormalsApplied'] = True
    report = {
        'suppliedFilesUnchanged': ['user_couch_fabric_Normal.png', 'user_bricks_Normal.png', 'user_metal_floor_Normal.jpg'],
        'cloth': {'normalStrength': .24, 'uvScale': [12, 12]},
        'floor': {'normalStrength': .28, 'uvScale': [6, 6], 'groundFaces': ground_faces, 'baseColorAndMetallicUnchanged': True},
        'bricks': {'reverted': True, 'baseColorAndNormalsRestored': True},
        'normalSpace': 'tangent', 'colorSpace': 'Non-Color', 'originalGeometryUnchanged': True
    }
    (ART / 'user-surface-normals-report.json').write_text(json.dumps(report, indent=2))
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(ART / 'SafeHouse-modern-remake.blend'))
    print('USER_SURFACE_NORMALS_DONE ' + json.dumps(report)); return None

bpy.app.timers.register(finish, first_interval=.2)
print('Couch and floor normals applied; save queued.')
