"""Read-only PE/string leads, archive identities and byte-comparison summaries."""
import collections
import hashlib
import json
import re
import struct
import subprocess
import sys
from pathlib import Path

ROOT=next(p for p in Path(__file__).resolve().parents if (p/'SacredItemSimulator.sln').is_file())
OUT=ROOT/'docs/research/particle-emitter-samples'
GAME=Path(sys.argv[1] if len(sys.argv)>1 else r'E:\SteamLibrary\steamapps\common\Sacred Gold')

def write(name,obj):
    (OUT/name).write_text(json.dumps(obj,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')

def lines(name,rows):
    (OUT/name).write_text(''.join(json.dumps(r,ensure_ascii=False)+'\n' for r in rows),encoding='utf-8')

def load(name):
    return [json.loads(l) for l in (OUT/name).read_text(encoding='utf-8').splitlines()]

def identity(path):
    with path.open('rb') as stream: digest=hashlib.file_digest(stream,'sha256').hexdigest()
    return dict(path=str(path),size_bytes=path.stat().st_size,sha256=digest)

exe=(GAME/'Sacred.exe').read_bytes()
pe=struct.unpack_from('<I',exe,0x3c)[0]
assert exe[:2]==b'MZ' and exe[pe:pe+4]==b'PE\0\0'
machine,nsections,timestamp=struct.unpack_from('<HHI',exe,pe+4)
optional_size=struct.unpack_from('<H',exe,pe+20)[0]
optional=pe+24
magic=struct.unpack_from('<H',exe,optional)[0]
assert magic==0x10b, 'This probe currently supports the local PE32 executable only.'
base=struct.unpack_from('<I',exe,optional+28)[0]
sections=[]
for i in range(nsections):
    off=optional+optional_size+i*40
    name=exe[off:off+8].split(b'\0',1)[0].decode('ascii')
    virtual_size,rva,raw_size,raw_offset=struct.unpack_from('<IIII',exe,off+8)
    flags=struct.unpack_from('<I',exe,off+36)[0]
    sections.append(dict(name=name,rva=rva,virtual_size=virtual_size,raw_size=raw_size,
                         raw_offset=raw_offset,flags=flags,executable=bool(flags&0x20000000)))

def locate(offset):
    for s in sections:
        if s['raw_offset']<=offset<s['raw_offset']+s['raw_size']:
            return base+s['rva']+offset-s['raw_offset'],s['name'],s['executable']
    return None,None,False

assets={r['name'].upper():r for r in load('Texture.pak.candidates.jsonl')}
strings=[]
pattern=re.compile(r'PARTICLE_|PARTIKEL|EMITTER|SMOKE|SPARK|RAUCH|FLAMME|LICHTER_|BASALTRISS|COALPOT|CANDLE',re.I)
for match in re.finditer(rb'[\x20-\x7e]{4,}',exe):
    value=match.group().decode('ascii')
    if not pattern.search(value): continue
    offset=match.start(); va,section,_=locate(offset)
    refs=[]
    if va is not None:
        needle=struct.pack('<I',va); start=0
        while (found:=exe.find(needle,start))>=0:
            refva,refsection,executable=locate(found)
            refs.append(dict(file_offset=found,preferred_va=refva,section=refsection,section_executable=executable,
                surrounding_16_before_16_after_hex=exe[max(0,found-16):found+20].hex()))
            start=found+1
    asset=assets.get(value.upper())
    strings.append(dict(text=value,file_offset=offset,preferred_va=va,section=section,
        raw_context_32_before_96_after_hex=exe[max(0,offset-32):offset+len(match.group())+96].hex(),
        candidate_texture_descriptor_id=asset['descriptor_id'] if asset else None,
        literal_address_occurrences=refs,
        reference_caution='Occurrences are raw little-endian address-byte matches, not disassembly-confirmed xrefs or evidence that an emitter uses this asset. Preferred VA assumes the PE image base, not a live process address.'))
lines('Sacred.exe.particle-string-leads.jsonl',strings)
write('Sacred.exe.image.json',dict(machine=machine,optional_magic=magic,preferred_image_base=base,
    coff_timestamp_raw=timestamp,sections=sections,string_lead_count=len(strings),
    scope='Static analysis of local Sacred.exe. No process memory or runtime call trace was captured.'))

archive_paths=[GAME/'Sacred.exe',GAME/'pak/Items.pak',GAME/'pak/mixed.pak',GAME/'pak/tiles.pak',
               GAME/'World/Static.PAK',GAME/'World/Floor.PAK',GAME/'World/sectors.wldx']
archive_paths+=list((GAME/'pak').glob('texture*.pak'))
write('source-archives.json',[identity(p) for p in archive_paths if p.exists()])
source_paths=[ROOT/'Sacred.Core/Pak/Items/ItemsPakEntryModelDescLayout.cs',
              ROOT/'Sacred.Core/World/StaticObjectRecord.cs',ROOT/'Sacred.Assets/Paks/Items/ItemsPakArchive.cs',
              ROOT/'Sacred.World/Rendering/WorldStaticSpriteProvider.cs',ROOT/'Sacred.World/Particles/WorldParticleMapper.cs']
write('reader-provenance.json',dict(git_head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
    reader_files=[identity(p) for p in source_paths],
    caution='Workspace already contained uncommitted changes, including particle/descriptor code. This dataset records the reader file hashes used and does not treat all existing comments/heuristics as independently proven Sacred.exe behavior.',
    screenshot_game_build='Unknown. Images are provided as Sacred game captures; PPOS/FPS are visible, but do not prove the exact executable, expansion, mod or renderer used. Local files are from a Sacred Gold installation; confirm compatibility in future runtime investigations.'))

obs=[o for o in load('observations.jsonl') if o['label']=='user_marked_emitter']
item_ids=sorted({o['fixture_match']['item_id'] for o in obs})
items={r['record_id']:r for r in load('Items.pak.jsonl')}
negative_items=[]
statics=load('Static.pak.jsonl')
for model in ('RUIN026','WH_SET_05','Bench 2','Table 5','CW_Tree 37(B)'):
    row=next((r for r in statics if r['model_name']==model),None)
    if row: negative_items.append(dict(item_id=row['item_id'],model_name=model,
        label='archive_comparison_only',basis='Ordinary contextual asset; this type-level label is not proven by every placement or screenshot.'))
compare_ids=sorted(set(item_ids+[n['item_id'] for n in negative_items]))
coal=[o for o in obs if o['fixture_match']['item_id']==9239]
coal_bytes=[bytes.fromhex(o['decoded_authored_fields']['static_record_hex']) for o in coal]
diffs=[i for i in range(64) if len({r[i] for r in coal_bytes})>1]
write('same-item-coalpot-comparison.json',dict(item_id=9239,
    observation_ids=[o['observation_id'] for o in coal],differing_offsets=diffs,
    differences=[dict(offset=i,offset_hex=f'0x{i:02X}',values=[r[i] for r in coal_bytes]) for i in diffs],
    all_other_bytes_identical=True,
    differing_field_ranges=dict(payload_instance_id=[0,4],sector_id=[12,14],projected_x=[14,18],projected_y=[18,22]),
    interpretation='Only instance identity, sector and position differ among these four full Static.PAK records. No per-instance particle size/rate/texture difference is visible in the remaining bytes. The screenshot effect difference could involve runtime particle age/state, context, or attribution; this does not prove an item-ID-only dispatch.'))

static_bytes=(GAME/'World/Static.PAK').read_bytes()
count=struct.unpack_from('<I',static_bytes,4)[0]
world_rows=[]
for ident in range(count):
    descriptor_type,offset,size=struct.unpack_from('<III',static_bytes,0x100+ident*12)
    if offset==0 or size<64 or offset+size>len(static_bytes): continue
    item_id=struct.unpack_from('<I',static_bytes,offset+4)[0]
    if item_id not in item_ids: continue
    raw=static_bytes[offset:offset+64]
    x,y=struct.unpack_from('<ii',raw,14)
    world_rows.append(dict(static_id=ident,item_id=item_id,model_name=items[item_id]['model_name'],
        file_offset=offset,descriptor_type=descriptor_type,raw_record_hex=raw.hex(),
        payload_instance_id=struct.unpack_from('<I',raw)[0],sector_id=struct.unpack_from('<H',raw,12)[0],
        flags_raw=struct.unpack_from('<I',raw,8)[0],projected_xy=[x,y],
        projected_inverse_world_xy=[x/96+y/48,y/48-x/96],
        surface_render_layer=struct.unpack_from('<h',raw,43)[0],
        next_static_id=struct.unpack_from('<I',raw,31)[0],
        status='authored_occurrence_of_observed_fixture_type; not visually verified as emitting',
        coordinate_caution='Inverse projection is a graphic anchor. The whole-world scan does not resolve tile linked-list membership, indoor selection, visibility or active emission.'))
lines('world-occurrences.jsonl',world_rows)
write('world-occurrence-counts.json',dict(total=len(world_rows),
    by_item=[dict(item_id=i,model_name=items[i]['model_name'],count=sum(r['item_id']==i for r in world_rows)) for i in item_ids]))
byte_rows=[]
for offset in range(128):
    byte_rows.append(dict(offset=offset,offset_hex=f'0x{offset:02X}',
        values=[dict(item_id=i,model_name=items[i]['model_name'],value=items[i]['raw_bytes_u8'][offset],
                     sample_group='positive_fixture_or_provisional_fissure' if i in item_ids else 'contextual_comparison') for i in compare_ids],
        interpretation='Raw comparison only; correlation does not establish byte purpose.'))
lines('Items.pak.byte-comparison.jsonl',byte_rows)
write('comparison-controls.json',negative_items)

families=[]
for family in sorted({o['visual']['family'] for o in obs}):
    group=[o for o in obs if o['visual']['family']==family]
    families.append(dict(family=family,observation_count=len(group),
        item_ids=sorted({o['fixture_match']['item_id'] for o in group}),
        observation_ids=[o['observation_id'] for o in group]))
write('findings.json',dict(
    scope='Screenshot evidence conversion and reproducible local archive context for subsequent particle-parameter research.',
    scene_count=8,source_image_count=16,marked_regions=35,
    positive_fixture_matches=27,provisional_fissure_regions=8,visual_negative_controls=24,
    additional_context_regions=29,fixture_item_types=len(item_ids),families=families,
    findings=[
        dict(status='observed_archive_bytes',claim='All 35 proposed fixtures have zero Static.PAK bytes 0x2e..0x32, zero Items model extent at 0x32, and no 0x0002 bit in Items graphic flags at 0x02. These sampled fields alone cannot supply their visible particle animation or an all-emitter classifier.'),
        dict(status='visual_asset_comparison',claim='Decoded Coalpot 1 (9239), DUN_light_1 (9320) and Candle 6 (9212) fixture sprites contain the bowl/sconce but no flame. Their screenshot crops show added warm flame/smoke.'),
        dict(status='visual_asset_comparison',claim='The LICHTER fixture sprites contain the cyan bowls themselves. Some screenshot crops also show separated star-like glints. Do not equate the blue base pixels with proof of a particle texture match.'),
        dict(status='useful_paired_example',claim='Items 9239 Coalpot 1 is associated with the broad bright effect in scene 2262_3132 and three much smaller flames in scene 3501_3630. Compare complete instance records and runtime/environment state before assuming particle size is constant for an item ID.'),
        dict(status='observed_archive_bytes',claim='The four compared Coalpot 1 Static.PAK records differ only in instance identity, sector and projected position. Every other byte is identical; see same-item-coalpot-comparison.json.'),
        dict(status='coordinate_fit',claim='Repeated braziers and sconces support about 0.625 screen pixels per projected coordinate unit. Projection fit is a locating aid, with manual landmarks and explicit residuals.'),
        dict(status='unresolved',claim='Exact particle texture, quad size, socket offset, lifetime, emission rate, velocity, blend mode and dispatch code have not been recovered.'),
    ],
    new_binary_field_mappings=[],
    layout_change_reason='No new byte semantics were established; existing layout mappings were exported with raw bytes. No Sacred.* production changes are needed for this evidence conversion.',
    next_investigations=[
        'Use 2262_3132_P01 versus the three 3501_3630 positives as the same-item/different-effect comparison. Check their complete Static.PAK bytes, neighboring linked records and scene/runtime state.',
        'Trace the static-object setup/update dispatch for Items 9212, 9239, 9320, 21917, 21918, 21920 and 21921 in the hashed Sacred.exe; prove whether it uses item IDs, descriptor fields, other tables or scripts.',
        'Use Sacred.exe.particle-string-leads.jsonl to start disassembly-confirmed xref research and Texture.pak.candidates.jsonl to compare actual pixel assets. Literal pointer matches alone are not confirmed calls.',
        'Resolve all eight fissure regions against overlapping BasaltRiss records before using their item or instance bytes as supervised positives.',
        'Capture multiple original-game frames at a fixed scene and known timestamps to measure motion, lifetime and birth counts. In remake debugging use the console screenshot cheat, as required by AGENTS.md.',
        'Confirm the screenshot game build/renderer matches the hashed local Sacred Gold files before treating differences as byte-semantic evidence.',
    ]))
print(f'Wrote {len(strings)} executable string leads, file identities and byte comparisons.')
