"""Manual fixture landmarks, in unmodified screenshot pixels. No image resampling."""
import statistics

SCALE = 0.625
LANDMARKS = {
    '2262_3132': [(511752, 623, 578, 24, 8)],
    '2286_3135': [(511701, 1065, 546, 47, 103)],
    '2551_1689': [(563188,1114,208,32,12),(563189,1294,298,32,12)],
    '3191_1152': [(728489,713,151,32,12),(728499,593,211,32,12),
                  (728502,473,271,32,12),(728532,323,346,32,12)],
    '3501_3630': [(832891,1314,95,24,8),(833092,1105,409,24,8),(833332,385,558,24,8)],
    '4585_986': [(979138,1425,146,50,53),(979230,373,162,50,53),(979233,523,237,50,53)],
    '5083_616': [(994786,679,57,58,34),(994987,502,145,58,34),(995130,323,233,58,34),
                (994897,1004,267,58,34),(995258,644,447,58,34),(994901,1634,584,58,34),
                (995272,1275,764,58,34),(995355,290,758,58,34)],
}

def calibrate(scenes, instances):
    result = {}
    for s in scenes:
        sid = s['id']; points = []
        for ident,x,y,local_x,local_y in LANDMARKS.get(sid, []):
            k = instances[sid,ident]['known']
            points.append(dict(static_id=ident, observed_feature_xy=[x,y],
                feature_offset_from_projected_anchor_xy=[local_x,local_y],
                projected_xy=[k['projected_x'],k['projected_y']],
                fitted_translation_xy=[x-SCALE*(k['projected_x']+local_x), y-SCALE*(k['projected_y']+local_y)]))
        if points:
            tx = statistics.median(p['fitted_translation_xy'][0] for p in points)
            ty = statistics.median(p['fitted_translation_xy'][1] for p in points)
        else:
            tx = s['player_x']-30-SCALE*(s['world_x']-s['world_y'])*48
            ty = s['player_y']-20-SCALE*(s['world_x']+s['world_y'])*24
        for p in points:
            p['residual_xy_pixels'] = [round(p['fitted_translation_xy'][0]-tx,2),round(p['fitted_translation_xy'][1]-ty,2)]
        result[sid] = dict(scale=SCALE, translation_xy=[tx,ty], landmarks=points,
            transform='screen_xy = scale * projected_xy + translation_xy; then add scaled mixed-piece offsets for sprite bounds',
            method='Manual matching of bowl centers; scale 0.625 supported by 120-pixel screen spacing / 192 projected units between repeated dungeon braziers and 180/288 for repeated sconces.',
            confidence='low' if not points else ('medium' if len(points)==1 else 'medium_high'),
            limitations='Manual feature positions are approximate, typically several pixels; particles/occlusion alter apparent bowl centers. One-landmark scenes cannot independently determine scale. Ruins scene uses a coarse player-centered fallback.')
    return result

def reproject(row, scene, cal):
    b = row['approximate_screen_bounds']; k = row['known']
    x = k['projected_x']*cal['scale'] + cal['translation_xy'][0]
    y = k['projected_y']*cal['scale'] + cal['translation_xy'][1]
    return {**{side:round((b[side]-b['foot_x' if side in ('left','right') else 'foot_y'])/.6*cal['scale'] + (x if side in ('left','right') else y),2) for side in ('left','top','right','bottom')},
            'foot_x':round(x,2),'foot_y':round(y,2)}
