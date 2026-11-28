"""Finalize the CI build with exact approved assets from the original user ZIP.
Usage: python tools/finalize_approved.py CI-base.zip approved-v0.3.2.zip output.zip
The approved source is supplied by the user; no graphics are regenerated.
"""
from pathlib import Path
import hashlib, json, zipfile, sys, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'tools/assets-approved-v033.json').read_text())
base_manifest=json.loads((root/'tools/assets-sha256.json').read_text())
assert len(manifest)==24 and set(manifest)==set(base_manifest)
changed={name for name in manifest if manifest[name]!=base_manifest[name]}
assert len(changed)==20 and all(n.endswith('.png') for n in changed)
base,approved,out=map(Path,sys.argv[1:])
assert out.resolve() not in (base.resolve(),approved.resolve())
def digest(b):return hashlib.sha256(b).hexdigest()
prefix='UNPeacekeepers/'
with zipfile.ZipFile(base) as b,zipfile.ZipFile(approved) as a:
    assert b.testzip() is None and a.testzip() is None
    names=b.namelist()
    assert len(names)==len(set(names))
    for n,h in base_manifest.items():assert digest(b.read(prefix+n))==h,n
    for n,h in manifest.items():assert digest(a.read(prefix+n))==h,n
    with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
        for name in names:
            assert name.startswith(prefix) and '..' not in Path(name).parts
            if name.endswith('/'):continue
            data=a.read(name) if name[len(prefix):] in changed else b.read(name)
            z.writestr(name,data)
with zipfile.ZipFile(out) as z,zipfile.ZipFile(base) as b:
    assert z.testzip() is None
    for n,h in manifest.items():assert digest(z.read(prefix+n))==h,n
    dll=prefix+'Assemblies/UNPeacekeepers.dll'
    assert z.read(dll)==b.read(dll) and z.read(dll)[:2]==b'MZ'
    assert not any('/Source/' in n or n.endswith('/Assembly-CSharp.dll') for n in z.namelist())
    for name in z.namelist():
        if name.endswith('.xml'):ET.fromstring(z.read(name))
    meta=ET.fromstring(z.read(prefix+'About/About.xml'))
    assert meta.findtext('packageId')=='local.geneva.unpeacekeepers'
    assert '0.3.3' in meta.findtext('description')
    for n in z.namelist():
        if n[len(prefix):] not in changed:assert z.read(n)==b.read(n),n
sha=digest(out.read_bytes())
out.with_suffix('.zip.sha256').write_text(sha+'  '+out.name+'\n')
print(f'Final verified: {len(changed)} exact approved PNGs, {len(manifest)} assets, compiled DLL retained, {out.name} ({sha})')
