"""Preserve UI package/font payloads with original source hashes and offsets."""
from decode_ui import *
from texture_archive import cstring
from font_metrics import parse_font
from audit_capture import read_generated_png
manifest=json.loads((OUT/'manifest.json').read_text())
report={'packages':[],'fonts':[],'errors':[]}
known={}
for t in manifest['textures']:known.setdefault(t['name'].upper(),t)
for archive in manifest['archives']:
    if 'error' in archive:continue
    data,_=unwrap((GAME/archive['source']).read_bytes())
    for c in read_chunks(data):
        if c.kind not in (0x30201,0x30203,0x30210):continue
        payload=data[c.data_offset:c.end]
        entry={'source':archive['source'],'chunkOffset':c.offset,'chunkId':f'{c.kind:08x}','storedSha256':sha256(payload),'storedBytes':len(payload)}
        isfont=c.kind==0x30201
        bucket='Fonts' if isfont else 'Packages'
        dest=OUT/bucket/(sha256(payload)+('.font.bin' if isfont else '.package.bin'))
        dest.parent.mkdir(exist_ok=True)
        if not dest.exists():dest.write_bytes(payload)
        entry['originalPath']=dest.relative_to(PROJECT).as_posix()
        if isfont:
            entry['name']=cstring(payload[:256]).upper()
            entry['formatVersion']=struct.unpack_from('<H',payload,520)[0] if payload[512:516]==b'FNTF' else None
            try:
                t=known[entry['name']]
                atlas=read_generated_png((PROJECT/t['path']).read_bytes())
                font,evidence=parse_font(payload,atlas,t['path'])
                metrics=dest.with_suffix('.json');metrics.write_text(json.dumps(font,indent=2)+'\n')
                entry.update(metricsPath=metrics.relative_to(PROJECT).as_posix(),glyphCount=len(font['glyphs']),status='metrics-validated')
            except (KeyError,ValueError) as error:
                entry.update(status='original-preserved-metrics-unconverted',reason=str(error))
            report['fonts'].append(entry)
        else:
            try:
                if c.kind==0x30210:
                    length=struct.unpack_from('<I',payload,16)[0]
                    if 4+length>len(payload):raise ValueError('Compressed package outside chunk')
                    raw,codec=unwrap(payload[4:4+length])
                    entry['compression']=codec['codec']
                else:raw=payload;entry['compression']='none'
                if raw[:4]!=b'FE\x6e\xe7' or struct.unpack_from('<I',raw,4)[0]!=len(raw)-8:raise ValueError('Unexpected FNG envelope')
                name=cstring(raw[40:296]);entry['nameCandidate']=name if re.fullmatch('[A-Za-z0-9_ .-]+\\.fng',name,re.I) else None
                target=dest.with_suffix('.fng');target.write_bytes(raw)
                entry.update(decodedPath=target.relative_to(PROJECT).as_posix(),decodedSha256=sha256(raw),status='decompressed-layout-not-interpreted')
            except (ValueError,FileNotFoundError) as error:entry.update(status='original-preserved-compression-unconverted',reason=str(error))
            report['packages'].append(entry)
report['totals']={'packageReferences':len(report['packages']),'uniquePackages':len({x['storedSha256'] for x in report['packages']}),'decompressedPackages':len({x['decodedSha256'] for x in report['packages'] if 'decodedSha256'in x}),'fontReferences':len(report['fonts']),'uniqueFonts':len({x['storedSha256'] for x in report['fonts']}),'validatedFontRecords':sum(x['status']=='metrics-validated' for x in report['fonts'])}
(OUT/'packages-and-fonts.json').write_text(json.dumps(report,indent=2)+'\n')
print(report['totals'])
