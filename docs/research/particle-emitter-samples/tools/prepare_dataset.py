"""Build compact semantic evidence and an offline review gallery from archive exports."""
import collections
import hashlib
import html
import json
from pathlib import Path
from annotations import POSITIVES, NEGATIVES, CONTEXT
from calibration import calibrate, reproject

ROOT = next(p for p in Path(__file__).resolve().parents if (p/'SacredItemSimulator.sln').is_file())
OUT = ROOT/'docs/research/particle-emitter-samples'

def read_lines(name):
    return [json.loads(line) for line in (OUT/name).read_text(encoding='utf-8').splitlines() if line]

def write(name, value):
    (OUT/name).write_text(json.dumps(value,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')

def lines(name, rows):
    (OUT/name).write_text(''.join(json.dumps(r,ensure_ascii=False)+'\n' for r in rows),encoding='utf-8')

scenes = json.loads((OUT/'scene-inputs.json').read_text())
statics = read_lines('Static.pak.jsonl')
instances = {(r['scene_id'],r['record_id']):r for r in statics}
items = {r['record_id']:r for r in read_lines('Items.pak.jsonl')}
mixed = {r['record_id']:r for r in read_lines('mixed.pak.jsonl')}
calibrations = calibrate(scenes, instances)
write('projection-calibration.json', calibrations)
scene_map = {s['id']:s for s in scenes}
for row in statics:
    # Original exporter bounds are retained; rerunning this script is idempotent.
    row['review_screen_bounds'] = reproject(row,scene_map[row['scene_id']],calibrations[row['scene_id']])

observations = []
scene_rows = []
selected = set()
fissure_alternatives = [
    [477971,477978,477979,477981], [477965,477973], [477998], [478058],
    [478136,478121,478150,478118,478119,478134,478135], [478138,478124], [478272], [478247,478233],
]

def bbox_regions(scene, crop):
    x,y,w,h = crop
    assert x>=0 and y>=0 and x+w<=scene['width'] and y+h<=scene['height'], (scene['id'],crop)

def nearby(scene_id, crop, count=8):
    x,y,w,h = crop
    candidates=[]
    for row in statics:
        if row['scene_id']!=scene_id: continue
        b=row['review_screen_bounds']
        dx=max(b['left']-(x+w),0,x-b['right']); dy=max(b['top']-(y+h),0,y-b['bottom'])
        distance=(dx*dx+dy*dy)**.5
        if distance>40: continue
        candidates.append((distance+abs((b['left']+b['right'])/2-(x+w/2))*.02,row))
    return [dict(static_id=r['record_id'], item_id=r['item_id'], model_name=r['model_name'],
                 spatial_match_only=True) for _,r in sorted(candidates,key=lambda z:z[0])[:count]]

for scene in scenes:
    sid=scene['id']; data=POSITIVES[sid]
    assert len(data)==len(scene['marks'])
    source='images/'+sid.replace('_',' ')+'.png'
    for mark,(ident,crop,effect,note) in zip(scene['marks'],data):
        bbox_regions(scene,crop)
        r=instances[sid,ident]; item=items[r['item_id']]; selected.add(ident)
        group=mixed.get(item['known']['resolved_mixed_group_id'])
        family = ('scorched_fissure_smoke' if sid=='2146_2947' else
                  'blue_lamp_glints' if sid in ('2286_3135','4585_986') else
                  'large_brazier_fire' if sid in ('2551_1689','3191_1152') else
                  'wall_sconce_fire_smoke' if sid=='5083_616' else 'coal_bowl_fire')
        is_fissure = sid=='2146_2947'
        match_confidence = 'candidate_only' if is_fissure else 'high_visual_and_spatial'
        fixture_atlases = sorted({p['atlas_name'] for p in group['group']['pieces']}) if group else []
        observations.append(dict(
            observation_id=mark['id'],scene_id=sid,label='user_marked_emitter',
            label_basis='Green underline in the user-supplied annotated copy; source mechanism is not proven by annotation.',
            source_image=source,annotated_image=source[:-4]+' underlined.png',
            underline_bbox_xywh=[mark[k] for k in ('x','y','width','height')],
            crop_xywh=crop,crop_image='crops/'+mark['id']+'.png',
            visual=dict(family=family,description=note,effect_envelope_xywh=effect,
                effect_envelope_size_pixels=effect[2:],
                size_interpretation='Approximate manual envelope of visible effect region in this still, including overlap/background; NOT an individual particle, source texture size, emission volume, or authored quad size.',
                palette=['cyan','pale blue','white'] if 'blue' in family else ['dark gray','black','orange'] if is_fissure else ['white/yellow','orange/red','gray smoke'],
                apparent_direction='upper-left trail visible' if family in ('coal_bowl_fire','wall_sconce_fire_smoke') else 'glints above fixture' if family=='blue_lamp_glints' and sid!='2286_3135' else 'not established',
                measured_velocity=None,individual_particle_size_pixels=None,
                temporal_evidence='One captured state; annotated copy is not an independent time sample.'),
            fixture_match=dict(status=match_confidence,static_id=ident,item_id=r['item_id'],model_name=r['model_name'],
                executable_type_name=r['executable_type_name'],mixed_group_id=item['known']['resolved_mixed_group_id'],
                fixture_atlas_names=fixture_atlases,
                basis='Manual visual family and relative world placement match, supported by decoded fixture sprite. Fissure groups remain provisional because several records overlap.' if is_fissure else 'Manual fixture appearance and repeated spatial arrangement, cross-checked against the decoded fixture sprite; projection is approximate.',
                world_tile_xy=[r['known']['tile_world_x'],r['known']['tile_world_y']],
                projected_xy=[r['known']['projected_x'],r['known']['projected_y']],
                projected_inverse_world_xy=[r['projected_world_inverse']['x'],r['projected_world_inverse']['y']],
                coordinate_note='Filename/PPOS gives player position, not emitter position. Tile coordinates and projected coordinates above come from this candidate Static.PAK instance; inverse coordinates describe its graphic anchor, not necessarily the flame socket.',
                fixture_sprite='fixtures/item-'+str(r['item_id'])+'.png',
                review_screen_bounds=r['review_screen_bounds'],
                alternative_fissure_candidates=[dict(static_id=aid,item_id=instances[sid,aid]['item_id'],
                    model_name=instances[sid,aid]['model_name'],
                    world_tile_xy=[instances[sid,aid]['known']['tile_world_x'],instances[sid,aid]['known']['tile_world_y']],
                    status='spatially plausible overlapping fissure; unconfirmed')
                    for aid in fissure_alternatives[scene['marks'].index(mark)]] if is_fissure else []),
            particle_parameters=dict(texture_id=None,texture_name=None,spawn_position_offset=None,
                authored_size=None,emission_rate_per_second=None,burst_count=None,lifetime_seconds=None,
                initial_velocity=None,acceleration=None,rotation=None,color_curve=None,alpha_curve=None,
                blend_mode=None,atlas_grid=None,animation_frame_duration=None,random_seed=None,
                status='Not recovered. See raw descriptor bytes and candidate texture/executable catalogues; fixture atlas names are not particle texture assignments.'),
            decoded_authored_fields=dict(items_descriptor_offset=item['descriptor_file_offset'],
                items_descriptor_hex=item['raw_model_descriptor_hex'],items_known=item['known'],
                static_record_offset=r['file_offset'],static_record_hex=r['raw_record_hex'],
                static_sprite_params_2e_32=[r['known'][k] for k in ('sprite_param2_e','sprite_param2_f','orientation_or_frame','animation_frame_duration_ticks','animation_frame_count')]),
            spatial_alternatives=nearby(sid,crop),
        ))
    for i,(crop,name,note) in enumerate(NEGATIVES[sid],1):
        bbox_regions(scene,crop); oid=f'{sid}_N{i:02}'
        observations.append(dict(observation_id=oid,scene_id=sid,label='visual_negative_control',
            label_basis='Manually selected unmarked ordinary fixture with no separate emission visibly attached in this frame. This is an observational negative, not proof it can never emit.',
            source_image=source,crop_xywh=crop,crop_image=f'crops/{oid}.png',
            visual=dict(object=name,description=note),fixture_match=None,spatial_alternatives=nearby(sid,crop)))
    for i,(crop,kind,note) in enumerate(CONTEXT[sid][1],1):
        bbox_regions(scene,crop); oid=f'{sid}_C{i:02}'
        observations.append(dict(observation_id=oid,scene_id=sid,label='context_'+kind,
            label_basis='Context or ambiguity annotation; excluded from positive/negative fixture training labels.',
            source_image=source,crop_xywh=crop,crop_image=f'crops/{oid}.png',visual=dict(description=note),fixture_match=None))
    scene_rows.append(dict(scene_id=sid,player_ppos_xy=[scene['world_x'],scene['world_y']],ppos_z_displayed=0,
        dimensions=[scene['width'],scene['height']],source_image=source,
        scene_description=CONTEXT[sid][0],projection=calibrations[sid],
        positive_observation_ids=[m['id'] for m in scene['marks']],
        negative_control_count=len(NEGATIVES[sid]),context_region_count=len(CONTEXT[sid][1]),
        global_image_regions=[dict(kind='diagnostic_text',description='Top-left FPS and PPOS; FPS is display frame rate, not particle emission rate.'),
            dict(kind='hud',description='Portrait/status at top right and quickbar at bottom where present; excluded from world-object labels.'),
            dict(kind='archive_context',description='Full nearby static records, terrain cells and floors are provided separately. Occlusion, interior layer selection and viewport edges mean these are candidate records, not exhaustive pixel segmentations.')]))

lookup=collections.defaultdict(list)
for obs in observations:
    if obs.get('fixture_match'): lookup[obs['scene_id'],obs['fixture_match']['static_id']].append(obs['observation_id'])
for r in statics:
    ids=lookup.get((r['scene_id'],r['record_id']),[])
    r['positive_observation_ids']=ids
    r['label']='candidate_for_user_marked_region' if ids and r['scene_id']=='2146_2947' else 'matched_user_marked_fixture' if ids else 'unreviewed_context'
lines('Static.pak.jsonl',statics)
lines('observations.jsonl',observations)
write('scenes.json',scene_rows)
write('selected-static-ids.json',sorted(selected))
lines('emitters.compact.jsonl', [dict(observation_id=o['observation_id'],scene_id=o['scene_id'],
    visual=o['visual'],fixture_match=o['fixture_match'],particle_parameters=o['particle_parameters'],
    crop_image=o['crop_image']) for o in observations if o['label']=='user_marked_emitter'])
print(f'Prepared {len(observations)} observations, including {len(selected)} proposed fixture instances.')

def gallery():
    # Inline data and normal relative images work from file:// without fetch/CORS.
    cards=[]
    for o in observations:
        note=o['visual'].get('description',''); f=o.get('fixture_match')
        fixture='' if not f else f"<p>{html.escape(f['model_name'])} · Static {f['static_id']} · Items {f['item_id']} · tile {f['world_tile_xy']} · {f['status']}</p><img class='fixture' src='{f['fixture_sprite']}' title='Decoded fixture sprite; runtime particles excluded'>"
        cards.append(f"<article data-scene='{o['scene_id']}' data-label='{o['label']}'><h3>{o['observation_id']}</h3><p>{o['label']}</p><a href='{html.escape(o['source_image'])}'><img class='crop' src='{o['crop_image']}'></a>{fixture}<p>{html.escape(note)}</p><details><summary>JSON evidence</summary><pre>{html.escape(json.dumps(o,indent=2))}</pre></details></article>")
    page="""<!doctype html><meta charset='utf-8'><title>Sacred particle-emitter evidence</title>
<style>body{font:16px system-ui;background:#182025;color:#eee;margin:24px}a{color:#90cfef}header{position:sticky;top:0;background:#182025;padding:12px 0;z-index:1}select{font:inherit;padding:8px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:18px}article{background:#263138;padding:16px;border-radius:8px}img.crop{max-width:100%;max-height:390px;object-fit:contain;image-rendering:auto}.fixture{max-width:140px;max-height:140px;background:#111;padding:10px}pre{white-space:pre-wrap;font-size:12px;overflow-wrap:anywhere}h3{margin:0}p{line-height:1.45}</style>
<header><h1>Sacred particle-emitter evidence</h1><p>35 green-marked regions. Exact source-pixel crops, archive matches and explicit unknowns. Click a crop for its complete original image. Small extra images are decoded fixture assets.</p>
<select id='scene'><option value=''>All scenes</option>"""+''.join(f"<option>{s['id']}</option>" for s in scenes)+"""</select>
<select id='label'><option value=''>All evidence</option><option value='user_marked_emitter'>Marked emitters</option><option value='visual_negative_control'>Negative controls</option><option value='context_'>Context and ambiguities</option></select></header><main>"""+''.join(cards)+"""</main>
<script>function filter(){document.querySelectorAll('article').forEach(a=>a.hidden=(scene.value&&a.dataset.scene!==scene.value)||(label.value&&!a.dataset.label.startsWith(label.value)))}const scene=document.querySelector('#scene'),label=document.querySelector('#label');scene.onchange=label.onchange=filter;</script>"""
    (OUT/'gallery.html').write_text(page,encoding='utf-8')

gallery()
