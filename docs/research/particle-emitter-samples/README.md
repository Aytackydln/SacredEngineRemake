# Sacred particle-emitter screenshot evidence

**Follow-up:** [Particle definition research](../particle-definitions/README.md) now
locates the matching FX creations in `FunkCode.bin` and extracts their native
`Sacred.exe` presets. Its batch tools match all 35 observations automatically;
all 27 strong fixture matches have an FX command at the exact same tile. The
unknown parameter values below remain the original screenshot-evidence snapshot;
see the follow-up reports for the recovered definitions.

This is the reusable evidence conversion for the eight coordinate-named scenes in the user's `Particle Emitters` folder. It preserves all 16 original PNGs and supplies **35 marked emitter regions, 24 selected negative controls and 29 context/ambiguity regions**, each with a lossless crop. There are 27 strong fixture matches and eight provisional fissure-region matches. No runtime particle texture/size/rate/lifetime mapping is claimed.

Start with [findings.json](findings.json), [emitters.compact.jsonl](emitters.compact.jsonl), and [gallery.html](gallery.html). The gallery opens locally, filters by scene/label, links crops to complete originals, and shows decoded fixture sprites alongside screenshot evidence. [texture-gallery.html](texture-gallery.html) browses candidate texture assets, with optional alpha-mask visualization.

| Scene / player PPOS | Marks | Fixture evidence |
| --- | ---: | --- |
| 2146, 2947 | 8 | BasaltRissBig01 (15497), BasaltRissBig02 (15498), BasaltRissMed02 (15501), BasaltRissMed04 (15503) — provisional overlapping fissures |
| 2262, 3132 | 1 | Coalpot 1 (9239) |
| 2286, 3135 | 1 | LICHTER_HAENGEND_KLEIN (21917) |
| 2551, 1689 | 2 | DUN_light_1 (9320) |
| 3191, 1152 | 4 | DUN_light_1 (9320) |
| 3501, 3630 | 3 | Coalpot 1 (9239) |
| 4585, 986 | 8 | LICHTER_HAENGEND_MITTEL (21918), LICHTER_KLEIN (21920), LICHTER_MITTEL (21921) |
| 5083, 616 | 8 | Candle 6 (9212) |

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
| `Static.pak.jsonl` | 4230 scene/object rows from a generous viewport search, including raw 64-byte records, flags, linked-list fields and tile coordinates. Unreviewed rows are not negative examples. |
| `Items.pak.jsonl` | 907 definitions with complete 128-byte descriptors, byte arrays, known fields and file offsets. |
| `mixed.pak.jsonl` | 877 composed fixture groups, piece geometry/UVs, raw records and atlas names. |
| `fixture-sprites.jsonl`, `fixtures/` | 11 decoded fixture sprites, dimensions and anchors. These are asset extractions, not captured game frames. |
| `Texture.pak.candidates.jsonl`, `textures/` | 276 decoded PARTICLE/FX/animated-mini-object candidates with archive IDs, dimensions and alpha statistics. No emitter association inferred from names. |
| `Sacred.exe.particle-string-leads.jsonl` | 103 string leads with file offsets, preferred VAs, raw context and literal address matches. These are not disassembly-confirmed xrefs. |
| `Sacred.exe.type-names.jsonl` | Existing 0x44-stride TYPE-name catalogue extraction, with raw slots; not a particle parameter table. |
| `Items.pak.byte-comparison.jsonl` | Every descriptor offset compared across sampled fixture types and ordinary context types. Correlations are not field semantics. |
| `world-occurrences.jsonl` | 1461 authored placements of the 11 sampled fixture types across Static.PAK. Presence does not prove active emission. |
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

From the repository root, run `docs/research/particle-emitter-samples/tools/regenerate.ps1`. It uses the supplied original screenshot directory, the local Sacred Gold installation and `C:\Users\Aytac\.dotnet\dotnet.exe` by default. The script reads game files and regenerates this research folder. Manual regions and calibrations are in `tools/annotations.py` and `tools/calibration.py`; source PNGs are copied unchanged and crops are exact rectangular pixel extractions, with no image synthesis or newly captured screenshots.
