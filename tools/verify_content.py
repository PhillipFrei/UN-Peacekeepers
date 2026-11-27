"""Static content gate: localization coverage, definitions, stable assets and package identity."""
from pathlib import Path
import re, xml.etree.ElementTree as ET, hashlib, json
root=Path(__file__).resolve().parents[1]; mod=root/'UNPeacekeepers'
for path in mod.rglob('*.xml'):ET.parse(path)
keys=[]
for language in ['German','English']:
    labels=[e.tag for p in (mod/'Languages'/language/'Keyed').glob('*.xml') for e in ET.parse(p).getroot()]
    assert len(labels)==len(set(labels)), f'{language}: duplicate keys'
    keys.append(set(labels))
assert keys[0]==keys[1], keys[0]^keys[1]
source='\n'.join(p.read_text() for p in (mod/'Source').glob('*.cs'))
used=set(re.findall(r'"(UN\w+)"\.Translate\(',source))
assert used<=keys[0], used-keys[0]
defs=[e.findtext('defName') for p in (mod/'Defs').rglob('*.xml') for e in ET.parse(p).getroot() if e.findtext('defName')]
assert len(defs)==len(set(defs)), 'Duplicate defName'
refs=set(re.findall(r'GetNamed(?:SilentFail)?\("(UN_\w+)"',source))
assert refs<=set(defs), refs-set(defs)
for name,digest in json.loads((root/'tools/assets-sha256.json').read_text()).items():
    assert hashlib.sha256((mod/name).read_bytes()).hexdigest()==digest,name
assert ET.parse(mod/'About/About.xml').findtext('packageId')=='local.geneva.unpeacekeepers'
assert len(ET.parse(mod/'Defs/MainButtonDefs/UN_Tab.xml').getroot())==0
assert (mod/'Source/UNConsole.cs').read_text().count('yield return new Command_Action')==1
print(f'Content verified: {len(keys[0])} matched DE/EN keys, {len(defs)} definitions, unchanged original assets.')
