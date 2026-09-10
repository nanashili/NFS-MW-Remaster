"""Apply the reviewed faithful artwork maps to the currently open Blender scene.

Run after the usual room/export pipeline. This updates packed images only;
the sips packaging script updates Unity image files at their existing paths.
"""
import bpy
import hashlib
import json
import re
from pathlib import Path

ROOT = Path('/Users/tihan-nico/NFS MW Remaster')
report = json.loads((ROOT / 'Art/FrontendRooms/Artwork2K/artwork-report.json').read_text())
artworks = {entry['hash']: entry for entry in report['artworks']}
materials = {slot.material for obj in bpy.context.scene.objects if obj.type == 'MESH' for slot in obj.material_slots if slot.material}
images = {node.image for mat in materials if mat.use_nodes for node in mat.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image}
changed = []
for image in images:
    match = re.search(r'0x[0-9A-Fa-f]{8}', image.filepath or image.name)
    if not match or match.group() not in artworks:
        continue
    entry = artworks[match.group()]
    color_space = image.colorspace_settings.name
    if image.packed_file:
        image.unpack(method='REMOVE')
    image.filepath = str(ROOT / entry['output'])
    image.reload()
    image.colorspace_settings.name = color_space
    assert list(image.size) == entry['size'], image.name
    image.pack()
    changed.append({'image': image.name, 'size': list(image.size), 'path': image.filepath})
if artworks.get('0x5521D734', {}).get('reconstruction') and bpy.context.scene.get('room') == 'SafeHouse':
    # The recovered board UVs are mirrored. Correct only this colour texture.
    material = bpy.data.materials['SafeHouse | CRIB1_POSTERBOARD']
    nodes, links = material.node_tree.nodes, material.node_tree.links
    uv = nodes.get('Posterboard UV') or nodes.new('ShaderNodeTexCoord')
    uv.name = 'Posterboard UV'
    mapping = nodes.get('Posterboard orientation') or nodes.new('ShaderNodeMapping')
    mapping.name = 'Posterboard orientation'
    mapping.inputs['Location'].default_value = (1, 0, 0)
    mapping.inputs['Scale'].default_value = (-1, 1, 1)
    links.new(uv.outputs['UV'], mapping.inputs['Vector'])
    image_node = next(n for n in nodes if n.type == 'TEX_IMAGE' and n.image and '5521D734' in n.image.name)
    links.new(mapping.outputs['Vector'], image_node.inputs['Vector'])
    # Keep the existing Unity package synchronized without re-exporting geometry.
    gltf_path = ROOT / 'Assets/NfsMw/Content/Frontend/Models/FrontendSafeHouseRemake/SafeHouse.gltf'
    gltf = json.loads(gltf_path.read_text())
    before = hashlib.sha256(gltf_path.read_bytes()).hexdigest()
    board = next(m for m in gltf['materials'] if m['name'] == material.name)
    transform = {'offset': [1, 0], 'scale': [-1, 1]}
    board['pbrMetallicRoughness']['baseColorTexture'].setdefault('extensions', {})['KHR_texture_transform'] = transform
    if 'KHR_texture_transform' not in gltf.setdefault('extensionsUsed', []):
        gltf['extensionsUsed'].append('KHR_texture_transform')
    gltf_path.write_text(json.dumps(gltf, indent=2))
    after = hashlib.sha256(gltf_path.read_bytes()).hexdigest()
    report['geometryAndMaterialContractsUnchanged'] = False
    report['geometryUnchanged'] = True
    report.setdefault('posterboardOrientationCorrection', {
        'reason': 'Original board UVs mirror the image and lettering',
        'gltfSha256Before': before, 'geometryBufferUnchanged': True
    }).update(gltfSha256After=after, textureTransform=transform)
    report['models'][str(gltf_path.relative_to(ROOT))]['gltfSha256'] = after
    (ROOT / 'Art/FrontendRooms/Artwork2K/artwork-report.json').write_text(json.dumps(report, indent=2) + '\n')
bpy.context.scene['artwork2K'] = True
bpy.context.scene['artwork2KMethod'] = report['method']
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print(json.dumps({'file': bpy.data.filepath, 'artworks': changed}))
