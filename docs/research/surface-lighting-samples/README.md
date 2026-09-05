# Sacred surface-lighting samples

This dataset maps the coordinate-named screenshots in `C:\Users\Aytac\Pictures\Screenshots\Sacred\Unlit objects` to Sacred Gold's authored records.

`positive` means the placed source object is matched to a green region in the matching `lights outlined` screenshot. This includes `5085 606 lights outlined.png`. `negative` means the object is outside the green regions or is an object in a scene without a `lights outlined` copy. A visible glow/halo is not treated as surface lighting. Files whose names contain `particle` or `halo outlined` are supporting visual context and do not change these labels.

Files:

- `scenes.json`: annotation policy, screenshot paths, camera approximation, counts, and manually matched positive `Static.PAK` IDs.
- `findings.json`: compact counts, positive item types, selected hard negatives, and the current single-source-of-truth investigation.
- `Static.pak.jsonl`: one row per visible or potentially visible placed static instance, including its raw 0x40-byte record, decoded fields, item name, evidence, and approximate bounds.
- `Items.pak.jsonl`: unique item definitions referenced by those instances, with all currently mapped descriptor properties, the complete raw 0x80-byte descriptor, and aggregated evidence. `mixed` means the item type occurs in both positive and negative placements.
- `mixed.pak.jsonl`: unique composed sprite groups referenced by the instances, with raw group and piece records plus decoded geometry/UV fields.
- `tiles.pak.jsonl`: unique terrain/floor tile definitions in the viewport, including full raw records and scene evidence. Tiles are negative source samples; an illuminated tile is the receiving surface.
- `sectors.wldx.jsonl`: every approximately visible outdoor terrain cell with tile, pathing, elevation, material, and baked-brightness properties.
- `Floor.pak.context.jsonl`: decoded visible floor-overlay records. The current loader discards their record IDs, so exact raw `Floor.PAK` bytes cannot be joined without changing the loader.

JSONL lets an LLM or script stream, filter, and join large files. Join `Static.pak.jsonl.known.type_id` to `Items.pak.jsonl.record_id`, item `known.resolved_mixed_group_id` to `mixed.pak.jsonl.record_id`, and WLDX/floor tile IDs to `tiles.pak.jsonl.record_id`.

The screen transform is fitted from the coordinate-named screenshots at 0.6 render scale. Bounds are deliberately approximate and favor recall: large sprites whose placement point is just outside the frame are retained. The positive IDs were manually matched against the green outlines; they are the supervised labels. Actors, dropped loot, particles, and other runtime-only entities have no stable `Static.PAK` record and are not guessed into an archive row.
