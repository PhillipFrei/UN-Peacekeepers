"""Static compatibility and regression checks against the previously installed v0.3.5.
Not an engine test or a claim that RimWorld saves have been loaded."""
from pathlib import Path
import subprocess, re, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
baseline='df57b3d248ec562c8f620181b77b6c44684bcf35'
def old(path):return subprocess.check_output(['git','show',baseline+':'+path],cwd=root)
def current(path):return (root/path).read_bytes()
def segment(data,start,end):
    return data[data.index(start):data.index(end,data.index(start))]
site='UNPeacekeepers/Source/UNMissionSites.cs'
assert segment(old(site),b'        bool Reserved\n',b'        public override string GetInspectString()')==segment(current(site),b'        bool Reserved\n',b'        public override string GetInspectString()'), 'Map cleanup changed'
# Original XML definitions (equipment, quest roots, letters, etc.) and dependency identity remain unchanged.
defs=subprocess.check_output(['git','ls-tree','-r','--name-only',baseline,'UNPeacekeepers/Defs'],cwd=root,text=True).splitlines()
for name in defs:assert current(name)==old(name),name
for tag in ['packageId','supportedVersions','modDependencies','loadAfter']:
    p='UNPeacekeepers/About/About.xml'
    assert ET.tostring(ET.fromstring(current(p)).find(tag))==ET.tostring(ET.fromstring(old(p)).find(tag)),tag
# All saved fields used in the old sources still exist in each corresponding source file.
source=subprocess.check_output(['git','ls-tree','-r','--name-only',baseline,'UNPeacekeepers/Source'],cwd=root,text=True).splitlines()
def keys(data):
    return set(re.findall(rb'Scribe_\w+\.Look\(ref\s+[^,]+,\s*"([^"]+)"',data))
count=0
for name in source:
    if name.endswith('.cs'):
        prior=keys(old(name));assert prior<=keys(current(name)),(name,prior-keys(current(name)))
        count+=len(prior)
# Original timing formula and core reward/cleanup constants are retained.
for marker in [b'o.durationTicks=(kind==0?',b'o.defenceTicks=Rand.RangeInclusive(1,3)*60000;']:
    line=next(l for l in old(site).splitlines() if marker in l)
    assert line in current(site).splitlines(),line
policy='UNPeacekeepers/Source/UNMissionPolicy.cs'
for marker in [b'const int RefillTicks=',b'int Goodwill(int tier)',b'int BaseSilver(int tier)',b'int ExcessSilver(',b'int ScaledSilver(',b'bool CanCloseSite(']:
    line=next(l for l in old(policy).splitlines() if marker in l)
    assert line in current(policy).splitlines(),line
print(f'Regression verified: unchanged cleanup, {len(defs)} original definition files, {count} saved field keys, timing/reward formulas and package identity.')
