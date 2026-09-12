"""Generate the readable index, field guide, texture browser and integrity report."""
import collections
import hashlib
import html
import json
import struct
from pathlib import Path

ROOT=next(p for p in Path(__file__).resolve().parents if (p/'SacredItemSimulator.sln').is_file())
OUT=ROOT/'docs/research/particle-emitter-samples'

def load(name):
    return [json.loads(l) for l in (OUT/name).read_text(encoding='utf-8').splitlines()]

def write(name,value):
    (OUT/name).write_text(json.dumps(value,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')

fields_items=[
    (0,2,'GraphicType','Raw representation bits; not a universal particle selector.'),
    (2,2,'GraphicFlags','Separate from GraphicType; existing mapped bits include static shadow 0x0001 and light/halo 0x0002.'),
    (4,4,'MiniObjectTextureId','Texture descriptor ID when the mini-object union is applicable.'),
    (8,4,'TextureId','General model texture field; an incidental numeric lookup is not an emitter association.'),
    (16,4,'MixedBaseGroupId','Resolve through mixed.pak to composed fixture sprite pieces.'),
    (32,4,'ItemId','Repeated item ID.'),(36,4,'SoundProfileId','Existing sndProfiles.pak profile mapping.'),
    (44,2,'StaticSpriteFrameCount','Fixture sprite animation field; not particle birth count.'),
    (46,1,'Category','Gameplay/UI family.'),(48,1,'StaticSpriteFrameDuration10Ms','Existing static sprite timing mapping; not emission rate.'),
    (49,1,'DescriptorFlags','Descriptor state flags.'),(50,2,'ModelExtent','Context-dependent authored extent; not established as particle size for these fixtures.'),
    (55,32,'ModelNameBytes','NUL-terminated ISO-8859-1 resource name.'),
    (91,2,'StaticShadowAtlasCellIndex','Existing shadow union mapping.'),(93,2,'StaticShadowAnchorX','Signed existing shadow anchor field.'),
    (95,2,'StaticShadowAnchorY','Signed existing shadow anchor field.'),(99,1,'StaticShadowProjection','Existing shadow projection selector.'),
    (100,2,'StaticShadowContactExtent','Existing contact footprint field.'),
    (102,4,'EffectTextureId','Existing attached-model-effect union field. Do not assume it means particle texture for a mixed world sprite.'),
]
fields_static=[
    (0,4,'PayloadInstanceId','Identity stored in record; exported record_id is the PAK table index.'),
    (4,4,'TypeId','Join to Items.pak record_id.'),(8,4,'Flags','Raw placement/render flags.'),(12,2,'SectorId','Authored sector identifier.'),
    (14,4,'ProjectedX','Signed projected graphic anchor X.'),(18,4,'ProjectedY','Signed projected graphic anchor Y.'),
    (31,4,'NextStaticId','Tile linked-list successor.'),(43,2,'SurfaceRenderLayer','Signed authored layer.'),
    (46,1,'SpriteParam2E','Context-dependent atlas columns or source X in the existing mini-object decoder.'),
    (47,1,'SpriteParam2F','Context-dependent atlas rows or source Y in the existing mini-object decoder.'),
    (48,1,'OrientationOrFrame','Context-dependent source square size/orientation/frame byte.'),
    (49,1,'AnimationFrameDurationTicks','Existing mini-object animation duration in 50 Hz ticks when applicable.'),
    (50,1,'AnimationFrameCount','Existing mini-object animation frame count when applicable.'),
    (51,1,'ElevationTier','Raw authored elevation tier; exported explicitly from the record.'),
]

def field_table(fields,size,source):
    occupied={i for off,n,_,_ in fields for i in range(off,off+n)}
    return dict(record_size=size,byte_order='little-endian for multi-byte integers',layout_source=source,
        fields=[dict(offset=off,offset_hex=f'0x{off:02X}',size=n,name=name,interpretation=note) for off,n,name,note in fields],
        unmapped_byte_offsets=[i for i in range(size) if i not in occupied],
        provenance='Existing repository layout mappings at the hashed reader version. No new byte semantics were inferred by this conversion.')

write('field-guide.json',dict(
    dataset_schema_version=1,
    units=dict(screen='Original clean-image pixels; x right, y down; origin top left.',
        rectangles='xywh = [left,top,width,height], half-open right/bottom. Manual envelopes are approximate.',
        source_texture='Decoded asset pixels; independent of screenshot scale and particle quad size.',
        filename_coordinates='Player PPOS/world tile coordinates shown in diagnostic overlay, not exact emitter coordinates.',
        projected='Static.PAK signed projected graphic-anchor coordinates. Inverse: x=ProjectedX/96+ProjectedY/48; y=ProjectedY/48-ProjectedX/96.',
        null='Unknown/not recovered, not zero or disabled.'),
    labels=dict(user_marked_emitter='Positive visual supervision from user underline; mechanism unproven.',
        visual_negative_control='Selected ordinary fixture with no visibly attached separate emission in this still.',
        context='Unmarked possible effects, runtime actors, UI or occlusion; not training negatives.',
        unreviewed_context='Nearby archive row, possibly hidden/off-screen/inactive. No emitter label inferred.'),
    joins=['observations.fixture_match.static_id -> Static.pak.record_id WITH scene_id',
        'Static.pak.item_id -> Items.pak.record_id',
        'Items.pak.known.resolved_mixed_group_id -> mixed.pak.record_id',
        'mixed.pak.group.pieces[].atlas_name identifies fixture atlas, not emitted particle texture',
        'Texture.pak.candidates.name -> decoded_png; descriptor IDs are accompanied by archive paths',
        'world-occurrences.item_id -> Items.pak.record_id; these whole-world rows are not emission observations'],
    items=field_table(fields_items,128,'Sacred.Core/Pak/Items/ItemsPakEntryModelDescLayout.cs'),
    static=field_table(fields_static,64,'Sacred.Core/World/StaticObjectRecord.cs'),
    binary_cautions=['Do not reinterpret unknown bytes, repeated CD bytes, or union fields without code/data evidence.',
        'Items offset 0x2E and Static offset 0x2E have different meanings.',
        'Existing heuristic particle/halo code and old scratch notes are context, not independently verified game behavior.',
        'Runtime particle parameters remain null; all raw fixture bytes are retained for future work.']))

observations=load('observations.jsonl'); positives=[o for o in observations if o['label']=='user_marked_emitter']
items={r['record_id']:r for r in load('Items.pak.jsonl')}
statics={(r['scene_id'],r['record_id']):r for r in load('Static.pak.jsonl')}
groups={r['record_id'] for r in load('mixed.pak.jsonl')}
assets=load('Texture.pak.candidates.jsonl')
seen=set()
for o in observations:
    assert o['observation_id'] not in seen
    seen.add(o['observation_id'])
    for key in ('source_image','crop_image'):
        assert (OUT/o[key]).is_file(),(o['observation_id'],key)
    if o.get('fixture_match'):
        f=o['fixture_match'];assert (o['scene_id'],f['static_id']) in statics
        assert f['item_id'] in items and f['mixed_group_id'] in groups
        assert (OUT/f['fixture_sprite']).is_file()
        assert all(v==0 for v in o['decoded_authored_fields']['static_sprite_params_2e_32'])
        assert o['decoded_authored_fields']['items_known']['model_extent']==0
        assert not o['decoded_authored_fields']['items_known']['graphic_flags_raw']&2
        assert len(bytes.fromhex(o['decoded_authored_fields']['static_record_hex']))==64
        assert len(bytes.fromhex(o['decoded_authored_fields']['items_descriptor_hex']))==128
        assert o['particle_parameters']['texture_id'] is None
for r in items.values(): assert len(bytes.fromhex(r['raw_model_descriptor_hex']))==128
for r in statics.values(): assert len(bytes.fromhex(r['raw_record_hex']))==64
for r in assets:
    data=(OUT/r['decoded_png']).read_bytes()
    assert data[:8]==b'\x89PNG\r\n\x1a\n'
    assert struct.unpack('>II',data[16:24])==(r['width'],r['height'])
for r in json.loads((OUT/'image-manifest.json').read_text()):
    assert hashlib.sha256((OUT/r['relative_path']).read_bytes()).hexdigest().lower()==r['sha256'].lower()
counts={f.name:sum(1 for _ in f.open(encoding='utf-8')) for f in OUT.glob('*.jsonl')}
for f in OUT.glob('*.json'): json.loads(f.read_text(encoding='utf-8'))
for f in OUT.glob('*.jsonl'):
    for line in f.read_text(encoding='utf-8').splitlines(): json.loads(line)
assert len(positives)==35 and len(observations)==88
write('validation.json',dict(status='passed',unique_observation_ids=len(seen),
    source_image_sha256_verified=16,positive_archive_joins_verified=35,
    item_record_lengths_verified=len(items),static_record_lengths_verified=len(statics),
    candidate_texture_dimensions_verified=len(assets),jsonl_row_counts=counts,
    crop_pixels='See crop-verification.json: every saved crop pixel compared against original clean PNG.',
    validation_limits='These checks establish artifact integrity and internal joins, not the truth of provisional image matches or particle-parameter hypotheses.'))

world_counts=json.loads((OUT/'world-occurrence-counts.json').read_text())
table=['| Scene / player PPOS | Marks | Fixture evidence |','| --- | ---: | --- |']
for sid in sorted({o['scene_id'] for o in positives}):
    os=[o for o in positives if o['scene_id']==sid]
    names=sorted({f"{o['fixture_match']['model_name']} ({o['fixture_match']['item_id']})" for o in os})
    table.append(f"| {sid.replace('_', ', ')} | {len(os)} | {', '.join(names)}"+(' — provisional overlapping fissures' if sid=='2146_2947' else '')+' |')
readme='''# Sacred particle-emitter screenshot evidence

This is the reusable evidence conversion for the eight coordinate-named scenes in the user's `Particle Emitters` folder. It preserves all 16 original PNGs and supplies **35 marked emitter regions, 24 selected negative controls and 29 context/ambiguity regions**, each with a lossless crop. There are 27 strong fixture matches and eight provisional fissure-region matches. No runtime particle texture/size/rate/lifetime mapping is claimed.

Start with [findings.json](findings.json), [emitters.compact.jsonl](emitters.compact.jsonl), and [gallery.html](gallery.html). The gallery opens locally, filters by scene/label, links crops to complete originals, and shows decoded fixture sprites alongside screenshot evidence. [texture-gallery.html](texture-gallery.html) browses candidate texture assets, with optional alpha-mask visualization.

'''+ '\n'.join(table)+'''

## What the evidence establishes

The decoded `Coalpot 1`, `DUN_light_1` and `Candle 6` fixture sprites contain no flame. Their screenshot crops show additional warm effects. The `LICHTER_*` sprites contain the cyan bowls; several screenshots also show separated pale star-like glints. A fixture's mixed atlas is therefore **not** an established emitted-particle texture.

All 35 proposed fixture records have zero Static bytes `0x2E..0x32`, zero Items `ModelExtent`, and no Items graphic-flags `0x0002` bit. These particular fields do not provide their visible particle animation or a universal emitter classifier. Full raw records and per-offset comparisons are retained.

The four sampled `Coalpot 1` instances share the same item definition and every Static record byte outside instance ID, sector and projected position. Their effects look different in the captured frames. [same-item-coalpot-comparison.json](same-item-coalpot-comparison.json) preserves the precise differences. Particle age/state, surroundings and effect attribution still need investigation; this is not proof of a size parameter or ID dispatch.

## Files and joins

| File | Content |
| --- | --- |
| `observations.jsonl` | All 88 semantic regions: labels, descriptions, crop boxes, effect envelopes, source paths, raw fixture bytes and explicit unknown parameters. |
| `scenes.json` | Scene descriptions, player PPOS, image dimensions and coverage notes. |
| `image-manifest.json`, `*.pair.json` | Original paths/hashes and measured green-line components. Three marked copies are one pixel narrower; comparisons use the shared top-left rectangle. Non-green changed pixels are reported, not silently ignored. |
| `Static.pak.jsonl` | '''+str(counts['Static.pak.jsonl'])+''' scene/object rows from a generous viewport search, including raw 64-byte records, flags, linked-list fields and tile coordinates. Unreviewed rows are not negative examples. |
| `Items.pak.jsonl` | '''+str(counts['Items.pak.jsonl'])+''' definitions with complete 128-byte descriptors, byte arrays, known fields and file offsets. |
| `mixed.pak.jsonl` | '''+str(counts['mixed.pak.jsonl'])+''' composed fixture groups, piece geometry/UVs, raw records and atlas names. |
| `fixture-sprites.jsonl`, `fixtures/` | 11 decoded fixture sprites, dimensions and anchors. These are asset extractions, not captured game frames. |
| `Texture.pak.candidates.jsonl`, `textures/` | '''+str(len(assets))+''' decoded PARTICLE/FX/animated-mini-object candidates with archive IDs, dimensions and alpha statistics. No emitter association inferred from names. |
| `Sacred.exe.particle-string-leads.jsonl` | 103 string leads with file offsets, preferred VAs, raw context and literal address matches. These are not disassembly-confirmed xrefs. |
| `Sacred.exe.type-names.jsonl` | Existing 0x44-stride TYPE-name catalogue extraction, with raw slots; not a particle parameter table. |
| `Items.pak.byte-comparison.jsonl` | Every descriptor offset compared across sampled fixture types and ordinary context types. Correlations are not field semantics. |
| `world-occurrences.jsonl` | '''+str(world_counts['total'])+''' authored placements of the 11 sampled fixture types across Static.PAK. Presence does not prove active emission. |
| `sectors.wldx.context.jsonl`, `Floor.pak.context.jsonl`, `tiles.pak.jsonl` | Terrain/floor context and tile definitions for the broad viewport search. Exact Floor.PAK record IDs are unavailable from the current loader; this is stated in each floor row. |
| `projection-calibration.json` | Approximate 0.625 projection fitted to manually selected fixture landmarks; residuals and weak cases included. Initial 0.6 search bounds are retained separately. |
| `field-guide.json` | Coordinate/rectangle units, null semantics, labels, joins, known field offsets and currently unmapped byte offsets. |
| `source-archives.json`, `reader-provenance.json` | SHA-256 identities of local game archives/executable and relevant reader code. |
| `validation.json`, `crop-verification.json` | Parse/link/hash/size checks and pixel-by-pixel crop verification. |

Join a visual observation's `fixture_match.static_id` to `Static.pak.jsonl.record_id` **with `scene_id`**; then join its `item_id` to `Items.pak.jsonl.record_id`, and the resolved mixed group ID to `mixed.pak.jsonl.record_id`. `world_tile_xy` locates the selected placement. The screenshot filename gives the player position; it is not the emitter's exact position. The inverse-projected coordinate is the graphic anchor, not a recovered particle socket.

## Interpretation limits for subsequent prompts

- `null` particle parameters mean unknown, never zero or disabled. An effect envelope is a manual screen-space region containing particles/background/overlap; it is not a measured particle quad or texture size. No individual-particle segmentation is claimed.
- One clean capture plus its marked copy supplies one visual state. Display FPS is not particle birth rate. Velocity, lifetime, rate, rotation, blending and animation cannot be recovered reliably from these stills alone.
- Green lines are positive supervision. Selected ordinary props are observational negatives. Unmarked waterfalls, furnace fire, chandeliers, additional blue lamps, lava-like patches, and all actor/combat effects are explicitly withheld as context/ambiguity.
- The eight fissure matches remain candidates because of overlap and a weak camera fit. Their original crops and alternative nearby records are retained. Do not train on their chosen item IDs as confirmed truth.
- Nearby archive rows can belong to hidden floors, roofs, clipped sprites or inactive objects. The dataset provides broad scene context, not an exhaustive pixel-level segmentation or visibility proof.
- Local archives are from Sacred Gold in the classic Sacred lineage. The screenshots do not identify the exact build/mod/renderer; confirm compatibility before runtime conclusions. Existing repository comments and heuristics were not all independently re-proven.

## Continue the research

Use the ordered `next_investigations` in `findings.json`. Prioritize the same-item coalpot comparison, then trace original static-object setup/update code for the seven strongly matched fixture types. Resolve literal string/address leads with actual disassembly before assigning textures or interpreting adjacent bytes. Confirm fissure placement labels before correlating their descriptor bytes. Add newly proven game-file byte meanings to the corresponding production layout classes; this conversion established no new byte semantics and made no production changes.

## Reproduce

From the repository root, run `docs/research/particle-emitter-samples/tools/regenerate.ps1`. It uses the supplied original screenshot directory, the local Sacred Gold installation and `C:\\Users\\Aytac\\.dotnet\\dotnet.exe` by default. The script reads game files and regenerates this research folder. Manual regions and calibrations are in `tools/annotations.py` and `tools/calibration.py`; source PNGs are copied unchanged and crops are exact rectangular pixel extractions, with no image synthesis or newly captured screenshots.
'''
(OUT/'README.md').write_text(readme,encoding='utf-8')

cards=[]
for a in assets:
    cards.append(f"<article><h3>{html.escape(a['name'])}</h3><p>{a['width']} × {a['height']} · ID {a['descriptor_id']} · {a['storage_format']}</p><img loading='lazy' src='{html.escape(a['decoded_png'])}'><p>Alpha 0 / partial / 255: {a['transparent_pixels']} / {a['translucent_pixels']} / {a['opaque_pixels']}</p></article>")
page="""<!doctype html><meta charset='utf-8'><title>Sacred candidate particle textures</title><style>body{font:15px system-ui;background:#20272b;color:#eee;margin:24px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:16px}article{background:#303a40;padding:12px}img{max-width:100%;height:190px;object-fit:contain;background:repeating-conic-gradient(#444 0% 25%,#666 0% 50%) 50%/20px 20px}.alpha img{filter:brightness(0) invert(1)}h3{font-size:14px;overflow-wrap:anywhere}input{font:inherit;padding:10px}</style><h1>Candidate texture catalogue</h1><p>Archive assets only. These are not confirmed assignments to the screenshot emitters. Alpha view uses a browser filter to display the stored alpha as a white mask; original PNGs remain unchanged.</p><input id='search' placeholder='Filter texture name'><label><input type='checkbox' id='alpha'>Alpha mask view</label><main>"""+''.join(cards)+"""</main><script>search.oninput=()=>document.querySelectorAll('article').forEach(a=>a.hidden=!a.textContent.toLowerCase().includes(search.value.toLowerCase()));alpha.onchange=()=>document.body.classList.toggle('alpha',alpha.checked);</script>"""
(OUT/'texture-gallery.html').write_text(page,encoding='utf-8')
print('Documentation generated; all JSON, joins, source hashes and PNG dimensions validated.')
