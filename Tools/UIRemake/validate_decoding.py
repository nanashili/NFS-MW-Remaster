"""Check source/cache/PNG integrity and record legacy DDS diagnostic mismatches."""
from decode_ui import *
from audit_capture import read_generated_png
from texture_decode import decode_blocks
m=json.loads((OUT/'manifest.json').read_text());assert not m['errors']
report={'verifiedSourceArchives':0,'verifiedPngs':0,'verifiedHuffCacheRecords':0,'nativeDdsComparison':[],'knownNonChunkArchives':[]}
for a in m['archives']:
    assert sha256((GAME/a['source']).read_bytes())==a['sha256'],a['source']
    report['verifiedSourceArchives']+=1
    if 'error' in a:report['knownNonChunkArchives'].append(a['source'])
for t in m['textures']:
    data=(PROJECT/t['path']).read_bytes();assert sha256(data)==t['pngSha256']
    im=read_generated_png(data);assert (im.width,im.height)==(t['width'],t['height'])
    assert [min(im.rgba[3::4]),max(im.rgba[3::4])]==t['alphaExtrema']
    report['verifiedPngs']+=1
    compression=t.get('streamCompression',{})
    if compression.get('codec')=='HUFF-native-CompLib':
        raw=(OUT/'HuffCache/Textures'/(compression['storedSha256']+'.raw')).read_bytes()
        assert sha256(raw)==compression['uncompressedSha256']
        assert len(raw)==compression['uncompressedSize']
        report['verifiedHuffCacheRecords']+=1
    dds_path=OUT/'NativeExporter/HUDS_Custom_00/textures'/('0x'+t['nameHash'].upper()+'.dds')
    if 'GLOBAL/HUDS_Custom_00.bin' in t['sources'] and dds_path.exists():
        dds=dds_path.read_bytes()
        # The established 2018 exporter omits P8 palette bytes; compare DXT samples only.
        fourcc=dds[84:88]
        if fourcc in (b'DXT1',b'DXT3',b'DXT5'):
            native=decode_blocks(dds[128:],t['width'],t['height'],fourcc.decode())
            report['nativeDdsComparison'].append({'name':t['name'],'matches':native.rgba==im.rgba,'ddsPayloadStartsWithCompressionHeader':dds[128:132] in (b'JDLZ',b'HUFF')})
original=json.loads((PROJECT/'Assets/NfsMw/Content/Frontend/UI/Resources/MostWantedUI/Catalog.json').read_text())
byidentity={(t['nameHash'],t['pngSha256']) for t in m['textures']}
report['historicalOriginalsVerified']=0
for t in original['textures']:
    p=PROJECT/'Assets/NfsMw/Content/Frontend/UI/Resources'/(t['resourcePath']+'.png')
    assert sha256(p.read_bytes())==t['pngSha256'],t['name']
    assert (t['nameHash'],t['pngSha256']) in byidentity,t['name']
    report['historicalOriginalsVerified']+=1
report['nativeDdsComparisonNote']='Legacy AssetDumper writes incorrect streamed payloads in this gauge archive; its DDS outputs are diagnostic only and are not used for recovered PNGs.'
report['status']='PASS';(OUT/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(report)
