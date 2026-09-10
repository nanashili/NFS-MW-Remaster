"""Read installed MW car archives through the existing AssetDumper; retain provenance."""
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GAME = Path('/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted')
OUT = ROOT / 'Art/Cars'
WINE = '/Users/tihan-nico/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine'
EXPORTER = ROOT / 'Tools/WarehouseAssets/Exporter/AssetDumper.exe'

def win(path):
    return 'Z:' + str(path).replace('/', '\\')

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    mappings = json.loads(Path(__file__).with_name('source-model-folders.json').read_text())
    report = {'exporterSha256': digest(EXPORTER), 'carFolders': mappings, 'archives': [], 'errors': []}
    for folder in sorted((GAME / 'CARS').iterdir()):
        if not folder.is_dir():
            continue
        # Include shared wheels, spoilers, brakes, and trailers in the raw archive.
        for archive_name in ('GEOMETRY.BIN', 'TEXTURES.BIN'):
            source = folder / archive_name
            if not source.exists():
                continue
            dest = OUT / 'Models' / folder.name / archive_name.removesuffix('.BIN')
            before = digest(source)
            marker = dest / 'source.json'
            if not marker.exists() or json.loads(marker.read_text()).get('sha256') != before:
                dest.mkdir(parents=True, exist_ok=True)
                run = subprocess.run([WINE, '--bottle', 'NFS MW', win(EXPORTER), 'mw', win(source), win(dest)], capture_output=True, text=True, timeout=180)
                (dest / 'export.log').write_text(run.stdout + run.stderr)
                if run.returncode or 'Finished dumping!' not in run.stdout:
                    report['errors'].append({'source': str(source), 'log': str(dest / 'export.log')})
                    print('FAILED', folder.name, archive_name, flush=True)
                    continue
            entry = {'source': str(source), 'sha256': before, 'sourceUnchanged': digest(source) == before,
                     'output': str(dest.relative_to(ROOT)), 'models': len(list(dest.rglob('*.obj'))),
                     'textures': len(list(dest.rglob('*.dds')))}
            assert entry['sourceUnchanged'], source
            marker.write_text(json.dumps(entry, indent=2) + '\n')
            report['archives'].append(entry)
            (OUT / 'decoding-manifest.json').write_text(json.dumps(report, indent=2) + '\n')
            print(folder.name, archive_name, entry['models'], 'meshes', entry['textures'], 'textures', flush=True)
    print('Done:', len(report['archives']), 'archives;', len(report['errors']), 'errors', flush=True)
    if report['errors']:
        raise SystemExit(1)

if __name__ == '__main__':
    main()
