"""Manual observations in original clean-image pixels. Values describe this frame only."""

# Each row: static candidate, crop xywh, effect envelope xywh, visual note.
POSITIVES = {
    '2146_2947': [
        (477971, [1055, 0, 170, 115], [1080, 26, 99, 57], 'Small scorched fissure partly hidden behind the upper ruin wall; dark haze and orange edges. Several overlapping fissures make the individual source ambiguous.'),
        (477965, [1240, 42, 160, 116], [1273, 73, 89, 50], 'Small scorched fissure behind/right of the raised ruin; dark smoke-like patch with an orange edge, partly occluded by stone.'),
        (477998, [1310, 86, 245, 207], [1337, 188, 195, 65], 'Broad scorched ground below the upper-right luminous orange cluster. Dark smoke-like lobes above orange cracks; assignment of the detached orange cluster to this source is uncertain.'),
        (478058, [1165, 245, 240, 155], [1190, 300, 174,  65], 'Broad fissure below the large central-right orange cluster; black/gray lobes and orange embers along the ground edge.'),
        (478136, [922, 340, 172, 110], [948, 367, 130, 60], 'Cluster of overlapping scorched fissures. The green line supervises the group, not a reliably isolated single instance.'),
        (478138, [1035, 387, 148, 101], [1052, 409, 104, 50], 'Medium scorched fissure immediately above the short lower-right underline; black haze and an orange ground edge.'),
        (478272, [250, 492, 190, 133], [268, 523, 139,  60], 'Scorched patch below the dead tree at lower left; broad dark smoke-like cloud and orange crack edges.'),
        (478247, [455, 490, 210, 150], [478, 536, 169, 63], 'Large scorched patch beside the lower stone arch; dark cloud above an orange rim. Nearby unmarked fissures also show dark haze.'),
    ],
    '2262_3132': [
        (511752, [555, 498, 128, 143], [579, 537,  65, 47], 'Small coal bowl beside the work table and rune stone; bright white/yellow oval with orange fringe and a diffuse gray/orange trail toward upper left. The table, NPC and stone overlap this crop.'),
    ],
    '2286_3135': [
        (511701, [978, 435, 170, 188], [1044, 516, 40, 51], 'Cyan/blue hanging lamp on a gnarled tree above water. Blue bowl is clearly visible; detached moving particles cannot be isolated confidently in this single frame. User underline supplies the positive emitter label.'),
    ],
    '2551_1689': [
        (563188, [1061, 147, 91, 155], [1072, 163, 64, 56], 'Tall black metal brazier left of the doorway; white/yellow hot center, orange-red flame and diffuse dark/gray plume toward upper left.'),
        (563189, [1233, 247, 113, 147], [1253, 260, 65, 49], 'Matching tall brazier right of the doorway, same warm flame family at a different captured shape.'),
    ],
    '3191_1152': [
        (728489, [666, 103, 94, 155], [677, 118, 61, 48], 'Uppermost of four tall black braziers along the ruin wall; warm flame and gray/orange haze.'),
        (728499, [549, 162, 89, 157], [562, 180, 55, 42], 'Second brazier from the top; bright yellow/orange flame. Yellow numeral 1 nearby belongs to the interface/combat overlay.'),
        (728502, [427, 218, 106, 164], [439, 234, 63, 48], 'Third brazier from the top; orange flame with a broad dim plume on the left.'),
        (728532, [274, 304, 101, 149], [294, 320, 55, 43], 'Lowest/leftmost brazier; a stone column occludes part of its bowl and shaft. Visible orange flame projects to the right of the column.'),
    ],
    '3501_3630': [
        (832891, [1274,  61, 80, 86], [1292,  75, 42, 31], 'Small three-legged coal bowl on the upper-right roof; tiny yellow/orange flame, much smaller in screen extent than the large dungeon brazier flames.'),
        (833092, [1062, 369,  80, 98], [1081, 387, 45, 34], 'Small coal bowl on sand in front of the right house; low warm flame and faint gray plume.'),
        (833332, [344, 519, 89, 101], [360, 535, 49, 34], 'Small coal bowl beside the left house entrance; low orange/yellow flame and faint haze.'),
    ],
    '4585_986': [
        (979138, [1371, 87, 108, 122], [1395, 110,  60, 52], 'Blue wall-mounted bowl at upper right; pale star-like flecks near/above its cyan center.'),
        (979230, [329, 78, 113, 144], [348, 89, 67, 81], 'Blue wall-mounted bowl at upper left; several separated pale blue/white four-point glints above it.'),
        (979233, [484, 151, 91, 142], [497, 163, 65, 79], 'Second blue wall-mounted bowl on the diagonal wall; pale star glints rise above the bowl, readable against the black void.'),
        (979205, [832, 231, 137, 202], [854, 246, 73, 81], 'Tall blue bowl on a slender pedestal beside the statue; sparse white/blue star glints above the cyan bowl.'),
        (979204, [1171, 407, 81, 151], [1190, 418, 48, 53], 'Small blue pedestal/orb on the right wall; white/blue glints close above its cyan cap.'),
        (979307, [305, 518, 118, 174], [320, 531, 57, 64], 'Tall blue bowl/pedestal at lower left, partly occluded by the stone pillar; pale glints above the bowl.'),
        (979312, [574, 707, 88, 129], [593, 718, 51, 53], 'Small blue orb/pedestal near the bottom center. Combat effects and characters are close by; avoid treating their purple/green effects as lamp particles.'),
        (979381, [101, 725, 84, 140], [117, 738, 49,  62], 'Small blue orb/pedestal at the extreme lower left; a few pale glints above the cyan bowl. Its base is near the bottom image boundary.'),
    ],
    '5083_616': [
        (994786, [624, 12, 89, 93], [637, 22, 61,  40], 'Wall sconce near the top center-left; small orange flame and elongated gray/white smoke plume toward upper left.'),
        (994987, [459, 105,  70,  80], [471, 120, 46, 37], 'Wall sconce on the left interior corner; orange flame with a short pale smoky tail toward upper left.'),
        (995130, [276, 191, 73, 91], [287, 206, 51, 39], 'Sconce on the left freestanding pillar; bright orange tip and diffuse gray-white upper-left plume.'),
        (994897, [957, 224, 86,  90], [968, 237,  60,  40], 'Sconce attached to the upper-right table pillar; warm flame with a pale smoky streak.'),
        (995258, [594, 404, 82, 100], [605, 415,  60,  40], 'Sconce on the central-left table pillar; warm flame and gray smoke-like trail toward upper left.'),
        (994901, [1594, 539,  70, 109], [1601, 548, 58,  40], 'Sconce on the far-right table pillar; orange tip and a short diffuse gray trail.'),
        (995272, [1233, 723, 80, 93], [1242, 735,  60,  40], 'Sconce on the lower-right table pillar; orange flame with gray/white plume toward upper left.'),
        (995355, [246, 716,  80, 102], [256, 727,  60,  40], 'Sconce on the lower-left wall beside a banner; orange flame and faint pale plume. A character-held bright effect to its right is unrelated.'),
    ],
}

# Negative labels mean no separate emission visibly associated with this selected fixture in this frame.
NEGATIVES = {
    '2146_2947': [([393,348,207,183], 'Broken freestanding stone arch', 'Stone masonry and its shadow; nearby ground smoke is a separate source.'), ([112,358,130,190], 'Dead tree trunk and branches', 'No separate particle plume attached to the trunk; nearby scorched patch is excluded.'), ([1254,390,145,133], 'Brown/green shrub beside ruined wall', 'Ordinary foliage; no discrete emitted particles resolved.')],
    '2262_3132': [([199,0,310,654], 'Round stone tower with conical shingle roof', 'Roof, walls, door, braces; no separate plume attached to the structure.'), ([972,154,190,285], 'Conifer in front of wooden building', 'Branches and ordinary foliage.'), ([954,406,127,106], 'Moss-covered rock by path', 'No separate emission visible.')],
    '2286_3135': [([740,0,551,445], 'Timber-framed thatched house', 'Doors, windows, flowerpots and static building surfaces.'), ([288,357,165,406], 'Ordinary leafy tree in left grove', 'No distinct emitted particles.'), ([752,394,151,98], 'Flower bed beside the player', 'Colored flowers are not evidence of emission.')],
    '2551_1689': [([214,110,200,353], 'Large standing stone statue on left', 'No separate emission attached to this statue.'), ([648,288,46,181], 'Narrow fluted column above player', 'Static column; green combat/loot effect to lower right is unrelated.'), ([864,560,272,114], 'Rock ledge in front of platform', 'Ordinary rock surface.')],
    '3191_1152': [([905,110, 50,159], 'Fluted stone column near upper center-right', 'No separate emission.'), ([486,432,97,177], 'Tall column left of player', 'No separate emission.'), ([772,235,143,232], 'Central ruined statue and pedestal', 'No discrete emitted particles associated with statue.')],
    '3501_3630': [([944,570,409,168], 'Wooden fence across foreground', 'Static fence and shadows.'), ([1334,623,236,182], 'Two palm trees at water edge', 'Ordinary leaves and trunks.'), ([1314,391,56,62], 'Barrel at foot of exterior stairs', 'No separate emission.')],
    '4585_986': [([733,144,113,272], 'Large standing statue behind upper central lamp', 'Statue separated from blue emitting lamp in front.'), ([521,342,81,160], 'Closed wooden door in diagonal wall', 'No separate emission attached to door.'), ([228,525,77,104], 'Stone pillar surface above lower-left lamp', 'Stone pillar is the control; adjacent blue lamp is excluded.')],
    '5083_616': [([655,505,171,106], 'Table with bottles near player', 'Furniture and bottle highlights; no distinct plume associated with table.'), ([768,82, 60,134], 'Slim pole beside upper red carpet', 'Static pole, not the nearby character spell.'), ([1218,522,42,78], 'Blue wall banner on central enclosure', 'Static cloth graphic; central orange pit is a different region.')],
}

CONTEXT = {
    '2146_2947': ('Outdoor ruined stone complex, blue/black scorched ground, broken arches, rubble, dead trees, shrubs and living trees.', [([796,85,182,307], 'unresolved_effect', 'Several detached orange/yellow luminous patches and a tall flame above the ground; individual source correspondence is unresolved.'), ([1015,187,344,154], 'unresolved_effect', 'Orange streak and large luminous irregular patch northeast of the player; do not automatically assign it to the nearest crack.'), ([803,451,95,109], 'actor_effect', 'Player with small orange hand/weapon effect.'), ([602,81,151,115], 'actor_effect', 'Upper-left hostile actor, red targeting ring and nearby orange effect.'), ([390,463,176,99], 'unmarked_possible_emitter', 'Additional scorched patches around the lower arch; absence of green annotation does not make these hard negatives.')]),
    '2262_3132': ('Village crossroads beside a round tower, wooden building, trees, benches, rune stones, pond and work table.', [([538,79,230,219], 'unmarked_water_effect', 'Bright cyan vertical water jet/waterfall into a pond. It may be animated texture, particles, or both; unmarked and unresolved.'), ([499,563,96,93], 'actor', 'NPC at table beside the marked coalpot.'), ([814,469,65,92], 'actor', 'Player at crossroads.')]),
    '2286_3135': ('Village garden with timber house, flowerbeds, trees, fences and pond around a gnarled tree.', [([878,440,431,243], 'water_context', 'Pond and cyan surface around the marked hanging lamp; do not count water pixels as lamp particles.'), ([804,467,88,105], 'actor_effect', 'Player with an orange hand/weapon glint.'), ([1570,0,121,200], 'hud', 'Portrait and status indicators, partly clipped at right.')]),
    '2551_1689': ('Dark rocky sanctuary/cave-like open enclosure with large statue, columns, tiled platform, doorway and two braziers.', [([698,429,63,44], 'runtime_or_unresolved', 'Small bright green streak left of player; source unresolved, not one of the annotated braziers.'), ([752,447,147,113], 'actor_effect', 'Player and nearby weapon/creature with bright white/purple detail.'), ([311,560,55,51], 'runtime_or_unresolved', 'Small cyan/yellow glint near lower-left platform edge.')]),
    '3191_1152': ('Ruined stone sanctuary with aligned braziers, arched doorways, banners, many columns and broken statues.', [([822,457, 60,111], 'actor', 'Player in the middle of the tiled platform.'), ([476,607,49,47], 'runtime_or_unresolved', 'Small cyan object/effect near lower-left column.'), ([540,195,43,62], 'hud_or_actor', 'Yellow numeral 1 beside brazier; not a particle texture sample.')]),
    '3501_3630': ('Desert settlement with flat-roof houses, market stalls, forge, cacti, fences, barrels, tables, palms and water.', [([711,0,134,166], 'unmarked_fire_fixture', 'Forge/furnace opening contains orange fire, but is unmarked. Mechanism unresolved; keep out of negatives.'), ([795,450,90,123], 'actor_effect', 'Player near foreground fence; small warm hand/weapon highlight.'), ([1489,490,196,225], 'water_context', 'Shallow blue water and sand; not brazier emission.')]),
    '4585_986': ('Dungeon hall with stone walls, black voids, statues, doors, blue magical fixtures and active combat.', [([833,548,88,132], 'unmarked_same_family', 'Additional tall cyan bowl/pedestal below player is visibly present but unmarked. Important withheld example; never auto-label negative.'), ([445,357,661,456], 'combat_effects', 'Multiple actors, purple streaks/orbs, green highlights, white flashes, red/orange targeting rings and floating red/yellow damage numbers.'), ([1643,691,37,174], 'capture_overlay', 'Narrow inset/thumbnail strip at extreme lower right; not a second world object in this scene.')]),
    '5083_616': ('Large interior dining hall with tables, benches, pillars, banners, chandeliers, red carpets and central dark enclosure with orange pit.', [([1038,531,110,75], 'unmarked_fire_or_lava', 'Orange/yellow shape at bottom of central enclosure. Static texture, animation, and particle contributions unresolved.'), ([1390,252,174,85], 'unmarked_lighting_fixture', 'Bright chandelier with diffuse gold halo; not one of the underlined wall sconces.'), ([165,37,132,91], 'unmarked_lighting_fixture', 'Upper-left chandelier and yellow illumination.'), ([1246,0,74,107], 'actor_effect', 'Upper-right actor with large bright held/spell effect.'), ([866,52,66, 90], 'actor_effect', 'Actor beside upper red carpet has bright yellow hand/spell effect.'), ([466,753,45, 80], 'actor_effect', 'Lower-left actor beside red carpet with large bright warm effect.')]),
}
