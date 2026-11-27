"""Validate the complete distributable; fail rather than ship an asset-less mod."""
from pathlib import Path
import hashlib,json,zipfile,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
mod=root/'UNPeacekeepers'
manifest=json.loads((root/'tools/assets-sha256.json').read_text())
for name,sha in manifest.items():
    assert hashlib.sha256((mod/name).read_bytes()).hexdigest()==sha, name
for file in mod.rglob('*.xml'):ET.parse(file)
assert ET.parse(mod/'About/About.xml').findtext('packageId')=='local.geneva.unpeacekeepers'
assert len(ET.parse(mod/'Defs/MainButtonDefs/UN_Tab.xml').getroot())==0
for lang in ['German','English']:
    keys=[e.tag for p in (mod/f'Languages/{lang}/Keyed').glob('*.xml') for e in ET.parse(p).getroot()]
    assert len(keys)==len(set(keys)), 'duplicate translation keys'
dll=mod/'Assemblies/UNPeacekeepers.dll'
assert dll.read_bytes()[:2]==b'MZ' and dll.stat().st_size>10000
out=root/'UN_Peacekeepers_v0.3.1-alpha.zip'
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(mod.rglob('*')):
        if p.is_file() and 'Source' not in p.relative_to(mod).parts and p.suffix not in ('.pdb',):
            z.write(p,'UNPeacekeepers/'+p.relative_to(mod).as_posix())
with zipfile.ZipFile(out) as z:
    assert z.testzip() is None
    assert 'UNPeacekeepers/Assemblies/UNPeacekeepers.dll' in z.namelist()
    for name,sha in manifest.items():assert hashlib.sha256(z.read('UNPeacekeepers/'+name)).hexdigest()==sha
sha=hashlib.sha256(out.read_bytes()).hexdigest()
(out.with_suffix('.zip.sha256')).write_text(sha+'  '+out.name+'\n')
print(f'Verified {len(manifest)} unchanged assets, XML, DLL and ZIP: {out.name} ({sha})')
