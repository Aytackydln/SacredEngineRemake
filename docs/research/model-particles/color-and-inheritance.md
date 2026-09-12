# Native equipment effects: color and inheritance

Continuation on 2026-09-09, using the same hash-verified Sacred Gold executable
as [the main notes](README.md). Addresses are virtual addresses, base `0x400000`.

## Why the wings were purple

There is no light-blue item flag or replacement texture to select. The native
`MAGICSTREAK` constructor `7877C0` supplies four different diffuse colors for each
glow quad. Draw `787E70` submits D3D primitive type **5**, a triangle strip, with
four vertices in BL, TL, BR, TR order. Its triangles share the **TL–BR** diagonal.

The remake kept those colors but triangulated across **BL–TR**. This matters
because green is concentrated in TL and BR, and the glow texture is brightest
at its center. Interpolation at the center gives these RGB values, before texture
sampling, opacity and additive accumulation:

| Layer | Native / corrected center | Old remake center |
|---|---|---|
| Streak core | `(64,128,192)` | `(64,0,192)` |
| Streak halo | `(16,32,64)` | `(16,0,64)` |
| Whip | `(32,64,128)` | `(32,0,128)` |

`EffectMeshBuilder` now preserves the native triangle strip diagonal when building
indexed triangles for `NativeModelColored`. No tint multiplier or item-specific
color adjustment is involved. `NativeQuadVerification` calculates barycentric
interpolation at every quad center and compares it with the native values.

**Correction to the previous blend diagnosis:** `BlendDescription.AlphaBlend`
in the installed Vortice library has source `One`, not `SourceAlpha`. The current
SDR pipeline is `One/One`, paired with premultiplied RGB from `SacredItemParticle`.
That is equivalent to Sacred's straight RGB and `SrcAlpha/One`. Removing shader
premultiplication would over-brighten the glow. The verification checks the actual
pipeline values. HDR uses a bounded screen blend in the PQ target. Its source
value is solved from scene paper white and the configured particle highlight
nits, avoiding the severe over-brightening caused by using a PQ code as screen
coverage. It remains an approximation of the original additive SDR framebuffer.

`SeraWings02.grn`, used by both 4010 and 4092 and inherited by 2176, has **seven**
consecutive `fx_streak01..07` helpers. All seven core chains and all seven halos
are present and follow their individually retargeted anchors. Do not invent an
eighth through tenth trail: ten is the native lookup limit, not this model's count.
Some chains overlap or pass behind the body from the default front camera.

## Whip and trail state

The same diagonal correction applies to the whip. Its native predicate remains
3073; 3072 and its ten derived variants use the beam draw helper. Their shared
GRN name does not make them the same native effect.

A separate remake bug occurred when selecting a cached character again: retained
chain state was reused before its new scene was initialized. Scene instances now
reset native simulations when attached. Native effect coordinates remain in model
space, so the scene transform moves the complete wing trail with the character.
The animated spine's rigid pose delta is also applied to every retained trail
point before chain relaxation. The verification checks world movement, walk-pose
movement of a middle chain point, and cached-scene reset.

Streak motion now matches native `787BD4`: each call truncates
`int(min(dt,.5)*200)` without carrying a fractional accumulator, and head
interpolation is endpoint-exclusive. Gold initializes the chain collapsed and
treats a zero-X/Y head as an unplaced system. The January demo instead initializes
the chain along negative Y and lacks that first-placement check. The verification
executes both original functions and compares the managed Gold path with the
recorded positions and retained impulses.

Further scheduling evidence: `5A6727` subtracts the object's previous elapsed
time from the current time, and `5A99EE` calls manager `7CC080` only for positive
elapsed time. That manager invokes the effect's vtable `+8` update. The preview
loop at `6F1550` also uses this manager. No fixed whip substep rate was recovered;
the implementation still performs one relaxation per update. A real Seraphim
attack produces a curved whip (maximum deviation from the head–tail axis about
28 model units in the regression), with the recovered 1.5-unit links.

The earlier note saying the whip initializes `(0,-3*i,0)` was also wrong:
constructor `7872D1` multiplies by float **-1.5** at `891F48`.

## The actual Weapon.pak fields

Loader `434100` reads a **0x100-byte header**, checks byte `+3` as a version,
reads the record count as a **UInt32 at +4**, and reads **0x102 bytes per record**.
`4341C6` reads each record's `+0x80` item ID and writes its ordinal into the live
Items descriptor `+0x18`. That index is synthesized during loading; raw
Items.pak `+0x18` is not a persistent native-particle flag.

The prior remake parser used a 0x102-byte header and a UInt16 count at +3. It
accidentally read 4,872 records instead of the native 4,883, and compensated for
the shifted record start by subtracting two from most field offsets. Fixing only
the base ID without fixing this origin would read the wrong bytes.

| Location | Field / evidence |
|---|---|
| Header `+03`, byte | `Version`; native checks it is at least 8 |
| Header `+04`, UInt32 | `EntryCount`; supported archive contains 4,883 |
| Record `+00`, float | `PreviewScale`; `4342CA` → uniform scale helper `6539D0` |
| Record `+04/+08/+0C`, floats | Existing preview rotation fields, with corrected record-relative offsets |
| Record `+10/+18`, floats | `PreviewOffsetX/Z`; added to matrix `+30/+38` at `43437B/434381` |
| Record `+24`, UInt32 | **`BaseItemId`**, zero when no base visual is selected |
| Record `+28`, 64 bytes | Null-terminated equipment name |
| Record `+68`, 24 bytes | `slotDefault[6]`, six UInt32 item-type IDs |
| Record `+80`, UInt32 | Item ID whose equipment record this is |

All existing layout fields were moved to the correct record origin without
changing the bytes read for damage, category or preview rotation. The old
`Short1` and `TypeIdentifier` API values remain compatibility views of the upper
half of preview scale and high byte of base ID; neither is a separate file field.

Every non-torch predicate first tests the supplied item ID, then obtains its
weapon record using the synthesized index and tests `BaseItemId` at `+24`.
Torch's predicate has no inheritance fallback. The generated definitions now
record `UsesBaseItem`, recovered from that memory operand.

The loader also processes base visuals at `425A35`: it copies the base item's
entire 128-byte Items descriptor to the derived entry, preserves the derived
weapon index, and restores the derived identity at descriptor `+20`. The raw
descriptors for the additional effect items have empty model names. Merely
recognizing their effect IDs would therefore leave them without models.
`SacredEquipmentVisualResolver` performs the native copy in weapon record order,
while keeping the archive bytes unchanged. Native particle dispatch checks the
immediate base ID; it does not recursively search arbitrary ancestry for an effect.

## Additional equipment selected by these predicates

Full scan of the **native header-declared** weapon table:

| Effect | Direct IDs | Additional IDs via `BaseItemId` |
|---|---|---|
| Streak | 4010, 4092 | **2176** → 4010: Abdiels Schwingen |
| Beam | 3072 | **2296–2305** → 3072: ten light-saber variants, including Lukes Lichtklinge (2304) |
| Worms | 1771 | **7219–7223** → 1771: five vampire swords |
| Whip | 3073 | None in this table |
| Torch | 5632, 5633 | No native fallback |

The five predicate families cover **23 equipment records**, sixteen more than
their seven direct identities. The direct 3809 glow-line branch in
`renderItemEffects` brings the complete generated catalogue to **24 records** and
eight direct identities. The verification prints every ID and archive
name and creates geometry for all inherited variants. Debug fixtures 13–15
exercise inherited beam/wings, inherited worms, and the second direct wing ID.

This does **not** establish that these are every possible particle-bearing item.
The runtime effect mask documented in the main notes also includes affix-driven
FIREBALL, MAGICFIRE and MAGICGIFT instances. For example, `5CDD5D` calls `5CEA50`
and checks `weapon_gl01` before creating TYPE_FX `308`; that selector scans
runtime modifiers, not one universal "has particles" file bit. Those paths remain
separate research and should not be approximated by testing arbitrary graphic flags.

## Reproduction and validation

Use the main README commands with the supplied dotnet path. The existing native
evidence script now includes the weapon loader, descriptor copy, preview scale,
and manager update ranges. `verification/Program.cs` loads the real archives and
checks the header count, every record's item/base IDs, inherited models, all
eight direct simulations, native quad interpolation, every wing anchor, cached
scene restart, whip attack bending and all sixteen inherited effect geometries.

Live checks use console cheats and the screenshot cheat only. Current SDR remake
captures: [Seraphim](captures/seraphim-corrected.png),
[whip during attack](captures/whip-corrected-attack.png),
[inherited wings and beam](captures/inherited-wings-beam.png).
These are remake validation captures, not screenshots of the original game.
