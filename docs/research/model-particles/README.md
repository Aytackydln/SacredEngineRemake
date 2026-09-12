# Native equipment particles — continuation notes

Research date: 2026-09-09. This work follows the executable catalogue research in
[`../particle-definitions`](../particle-definitions). The model effects now have
an extracted catalogue and CPU simulations feeding Sacred.Engine's item-particle
shader. This is a working reconstruction, with fidelity gaps listed below.

Latest continuation: [color, trail state, and inherited equipment](color-and-inheritance.md).
It corrects the earlier blend diagnosis and establishes the raw Weapon.pak layout.

## Evidence and selection

The supported Steam Sacred Gold executable is
`E:\SteamLibrary\steamapps\common\Sacred Gold\Sacred.exe`.
SHA-256: `4df6659352282a0e57bdf69d2ac33396200fd0b54670d327cb9822ffdc4891cd`.
Decoded `.text` SHA-256:
`ee60108ce8147721717df1632c47b89c2b445a0255922219fa14a5980feadf37`.
Addresses below are virtual addresses in that build, image base `0x400000`.

**These innate effects really do have explicit item-ID tests in Sacred.exe.**
Their identity is not derived from damage types, texture IDs, `Cylinder01`, or
model filename patterns. Runtime IDs are generated from those native predicates;
the reader verifies the executable before extracting them.

| Item IDs | Predicate | Native system | Native attachment |
|---|---|---|---|
| 1771 | `428360` | `MAGICWORMS`, TYPE_FX `35A` | `weapon_fx01` through `weapon_fx02` |
| 3073 | `428190` | `MAGICWHIP`, TYPE_FX `338` | `weapon_fx01` |
| 3072 | `4283C0` | two calls to beam helper `40DA80` | `weapon_fx01` through `weapon_fx02` |
| 3809 | direct test in `renderItemEffects` at `5BF47B` | glow-line helper `40DA80` | `weapon_fx01` through `weapon_fx02` |
| 4010, 4092 | `4282F0` | `MAGICSTREAK`, TYPE_FX `345` | consecutive `fx_streak01` … `10` |
| 5632, 5633 | `428150` | `TORCHSMOKE`, TYPE_FX `313` | `weapon_fx01` |

**The direct native tests distinguish 3072 (beam) and 3073 (whip)** despite both
referencing `LIGHTSABER.GRN`. Their archive names are respectively "Magischer
Zylinder" and "Erleuchtete Himmelspeitsche der Sophia". Non-torch predicates also
test Weapon.pak record `+0x24`, now confirmed as `BaseItemId` by tracing the loader
at `434100`. The fallback and base-visual copying are implemented. The full scan
selects 24 equipment records: eight direct and sixteen inherited; see the latest
continuation for IDs and byte offsets. These are not filename-based exceptions.

World/equipped dispatch: `5CDB60`; torch create `5CDBD1`, worms `5CDCAF`, whip
`5CDE99`, streak `5CDFAD`. Event selectors are respectively `13`, `52`, `33` (hex).
Another streak attachment path is at `5B9E10`. The 3072 draw path begins `5BF710`.
Model preview attachment setup is at `6F3E90` onward: a separate internal flag
record selects these constructors. Those flags are not the Items.pak descriptor
graphics flags. General elemental affixes/`weapon_gl01` are a separate branch.

### Magic weapon and Items.pak billboards

`cWeapon3D::getMagicStaffType` at `5CEA50` scans the live merged modifier rows,
masks the low 16 bits of `BonusG`, and accepts only resolved modifier codes 1–51.
It does not turn every model containing `weapon_gl01` into a glowing weapon. The
static equipment preview honors literal resolved codes and the corresponding
MageStaff generation groups; unrelated swords and shields with the same helper
remain unlit. The recovered FIREBALL variants keep their different native energy
color tables and `PARTICLE_GLOW01.TGA` trails.

FIREBALL draw `76B540` calls `stdLensflare` with selector zero. The texture switch
at `762EF2` resolves that selector to `PARTICLE_FLARE02.TGA`, and `76B5C4` supplies
white `FFFFFFFF` as its diffuse color. The texture is authored RGB color with a
black key and zero alpha, so it has its own RGB lens-flare shader mode. The remake
no longer substitutes `PARTICLE_GLOW01.TGA` or computes a tint for this billboard.

Items.pak model-descriptor byte `+110` is returned by `426520`. A value of 9 makes
`renderItemEffects` probe `sera03_fx0..3` and draw white `PARTICLE_GLOW03.TGA`
billboards. Item 3111 is the only installed descriptor with this value, and its
wing model contains all four helpers. `SacredEquipmentEffectAnchor` now maps the
zero-based helper family, and retained billboards bind to each animated bone.

### Runtime appearance effect mask

The model-preview flag is now traced to its producer. `604300` builds the `0x22C`-
byte character appearance payload used by network message `C3`. It writes the 19
equipped item IDs at payload `+0x8C`, then writes 19 records of `0x0C` bytes at
payload `+0x10C`. For each equipped object it calls virtual property dispatcher
`5C70C0` with selectors `1F`, `20`, `21`, and `22`; their returned words occupy
record offsets `+0`, `+2`, `+4`, and `+6`. The low byte of selector `1F` is this
effect mask:

| Bit | Live-object source | Preview constructor | Meaning |
|---:|---|---|---|
| `01` | nonzero `+19C` | `774F00`, TYPE_FX `313` | TORCHSMOKE |
| `02` | nonzero `+1A0` | `76B430`, TYPE_FX `308` | FIREBALL |
| `04` | nonzero `+1A4` | `787120`, TYPE_FX `338` | MAGICWHIP |
| `08` | nonzero `+1A8` | `77C500`, TYPE_FX `35A` | MAGICWORMS |
| `10` | nonzero `+1AC` | `77A5C0`, TYPE_FX `31C` | MAGICFIRE; selector `20` is its intensity |
| `20` | nonzero `+1B4` | `77C0C0`, TYPE_FX `328` | MAGICGIFT; selector `21` is its intensity |
| `40` | nonzero `+1BC` | not consumed by `6F3E90` | runtime state marker; selector `22` carries its intensity |
| `80` | nonempty pointer vector `+1C4..+1C8` | `7877C0`, TYPE_FX `345` | MAGICSTREAK |

This is a synthesized snapshot of effect instances already attached to an item
object. It is not stored in Items.pak, Weapon.pak, or the GRN. In particular,
Items.pak graphics flag `0008`, tested by `428460`, controls unrelated render/UI
branches at `44B8BF` and `599F44`; it is not the native particle selector. The
3072 beam is also absent from this mask: native code selects it directly with
item predicate `4283C0` in draw paths `5BF710` and `6F2ECD`. There is therefore no
single persistent "has native particles" byte covering every 3D item. Native
code either creates an effect from explicit item predicates and later reports it
through this runtime mask, or draws the item-specific effect directly.

Bone lookup `401000` indexes 32-byte names at `8E7200`: indices 15–19 are
`weapon_fx01`, `weapon_fx02`, `weapon_fx03`, `weapon_gl01`, `weapon_gm01`;
26–35 are `fx_streak01` … `10`. `401010` indexes `8E7840`: 0–9 are
`stdfx_bone01` … `10`, 10–19 are `fx_streak01` … `10`.

## Recovered parameters

| System | Constructor / init / update / draw | Texture and behavior |
|---|---|---|
| TORCHSMOKE | `774F00 / 7752B0 / 774FE0 / 775050` | `PARTICLE_FIRE01.TGA`; size 7; velocity `(0,0,50)`, random half-range `(10,10,10)`; position half-range `(1.5,1.5,1.5)`; rotation π ± π; high-quality emission interval .03; fade rate −800 → lifetime .31875 seconds; 4×4 age atlas plus type-3 `stdLensflare` glow |
| MAGICWORMS | `77C500 / 77C870 / 77C590 / 77C600` | `PARTICLE_GLOW01.TGA`; size 1; velocity half-range `(40,40,40)`; interval .02; fade −500 → .51 seconds; red corners `00FF4444`; straight-span `stdCreationOnLine` emitter |
| MAGICWHIP | `787120 / 787760 / 787330 / 7874A0` | `PARTICLE_GLOW01.TGA`; 50 nodes; link 1.5; billboard half-size 4; displacement retention `13.5 × .05 = .675`; one chain relaxation per native update call |
| MAGICSTREAK | `7877C0 / 788120 / 787AC0 / 787E70` | same texture; 50 nodes; link .75; half-size 1.5 plus half-size 10 halo; retention .675; direction is bone-rotated +X; weight .7 decreases .014 per link; native steps `int(min(dt,.5) × 200)` |

Chain update: normalize `(old node − preceding updated node + retained displacement
+ direction × weight)`, multiply by link length, add preceding node. Retain
`.675 × (new position − old position)` for the next update. Whip has no direction
weight. Native whip constructor initializes nodes `(0,−1.5*i,0)`; streak initializes
them to zero. Native streak interpolates the moving head across its substeps.

The trail draw routines submit **glow quads at each point**, not ribbons or
scrolling `FX_STREAKS01.TGA`. The native corner order is BL, TL, BR, TR:

| Quad | Packed AARRGGBB corner colors |
|---|---|
| Whip | `FF000080 FF404080 FF004080 FF400080` |
| Streak core | `FF0000C0 FF8080C0 FF0080C0 FF8000C0` |
| Streak halo | `40000040 40202040 40002040 40200040` |

Draw flags go through `643430` to `643470`: flag 4 enables alpha blending;
flag 8 off sets render state 19 to 5 (source alpha); flag 16 on sets state 20 to
2 (destination one). This establishes **source-alpha additive blending** for the
native chain/beam path. The fixed-function color stage multiplies texture and
diffuse RGB, while its alpha stage multiplies texture and diffuse alpha. The
streak constructor at `7877C0` writes the eight packed corner colors shown above;
no item, texture, rarity, or later runtime color selector rewrites them.

The earlier explanation blaming double-applied alpha was incorrect. Vortice's
`BlendDescription.AlphaBlend` starts with source **One**, and the remake's SDR
particle pipeline uses **One/One** with premultiplied shader output. That is
equivalent to native **SrcAlpha/One** with straight shader output.

The color regression was the quad diagonal: native strips use BL/TL/BR and
BR/TL/TR; the remake used BL/BR/TR and BL/TR/TL. With these nonuniform corner
colors, the native streak center is `(64,128,192)`, while the old remake center
was `(64,0,192)`. Preserving the native diagonal restores light blue without
changing extracted colors or tuning texture IDs. Whip and halo quads need the
same correction. All seven wing core/halo pairs are checked individually.

Beam helper `40DA80` also draws repeated camera-facing quads:
`max(1, floor(length × density))`, endpoint exclusive. The texture is confirmed
as `PARTICLE_GLOW01.TGA`: renderer constructor pushes `8E7B90` at `401341` and
stores its handle at `+D4` at `40135A`; draw reads that slot at `40DAE5`.
3072 core: half-size 1.5, `FF8888FF`, density .8; halo: half-size 10, `408888FF`,
density .1. Worm blade glow: half-size 3, `FFFF4040`, density .5, randomized upper
half-size 4. Torch additionally draws a half-size-20 `80FFEA3B` glow; that color
must not tint its fire particles, whose initializer corners are `FFFFFFFF`.

## Layouts and implementation

Damage-driven weapon visuals exist in `cWeapon3D::toggleVisuals` at
`5CE08E`. The maximum fire, magic, and poison values are compared strictly, so
ties create no elemental visual. The winner must also exceed one third of the
maximum physical damage. FIRE and GIFT use
`clamp((damage - 25 * .5) / (275 / 3 - 25 * .5), 0, 1)`; MAGIC uses
`clamp((damage - 25 / 3) / (275 / 4 - 25 / 3), 0, 1)`.
These values drive the native elemental-damage visuals and remain active in the
remake. They do not identify permanent item-model trails or glows: the FIREBALL
modifier selector and innate item predicates are evaluated as separate paths.

`ModelEffectSourceWriter` evaluates the FIRE initializer at `77A8E0` and GIFT
initializer at `77C3E0` twice, at intensity zero and one. This extracts their
base parameter blocks and intensity slopes: emission size `2 + 2i`, size-change
rate `10 + 15i`, fade rate `-600`, and high-quality interval `.1`. Both use
`PARTICLE_GLOW01.TGA`, native draw flags 3, and line emission. The native glow
colors are `40FF8844`, `507033FF`, and `4044FF44`; FIRE/MAGIC/GIFT use densities
`.5/.7/.5` and half-sizes `4i`, `10i`, and `2 + 4i` respectively.

`Sacred.Core/Particles/SacredModelParticleStateLayout.cs` maps native serialized
state. TORCHSMOKE and MAGICWORMS serialize `0xA0` bytes from object `+20A0`:
elapsed `+00`, active byte `+04`, emission stage `+08`, four corner colors
`+0C..+18`, motion block `+1C` (0x20), emission block `+3C` (0x64).
Serializers: `775100`, `77C6C0`. These reuse the existing fully mapped emission
and motion layouts; they are not invented new game-file formats.

Whip/streak serialized block starts at object `+20A0`, size `0x4BC`: elapsed
`+00`, active `+04`, 50 `(position, displacement)` pairs at `+08`, stride `0x18`,
inertia at `+4B8`. Serializers: `7875B0`, `787F70`. Draw quads/texture handles
follow the block and are not part of it. The continuation additionally maps
Weapon.pak base IDs, corrects its header/record origin, and maps preview scale
and translations in `SacredEquipmentLayout`.

`ModelEffectSourceWriter` runs the actual torch/worm initializer in the bounded
x86 evaluator and validates that all parameter bytes were written without unknown
object reads. Torch stops before its sound call at `7753C4`. Other draw/chain
constants and literal predicate IDs are extracted from verified instructions.
Output: `Sacred.Particles/Generated/EmbeddedModelEffects.g.cs`.

`EquipmentEffectSceneFactory` selects the generated definition through the item
ID carried by `EquipmentEffectAttachment`. `NativeModelEffectSimulation` keeps
freely emitted particles separate from the current emitter pose. Model transform
changes rebase that detached history into the new model space. For whip and streak
chains, point zero remains attached to the moving equipment while points 1–49 are
rebased to preserve their position against the scrolling terrain. Camera movement
is converted from tile coordinates through the 96x48 isometric projection before
the retained points are moved in model space. The native relaxation step then
pulls them after the emitter. Animation moves the emitter through its attachment
bone while retained nodes keep their existing history; transforming the entire
history by the bone pose only produces rigid flicker.
Simulation time is real elapsed time, independent of walk animation playback
speed and the pose sampling throttle. Render-frame deltas that produce fewer
than two Gold streak relaxation steps are batched before invoking the recovered
update. During those frames the visible head remains on the current animated
helper. Completed batches retain Gold's endpoint-exclusive relaxation internally,
while point zero is rendered at that helper so it does not lag or alternate
between bone poses on an uncapped render loop.

`Granny1AttachmentRetargeter` carries attachment positions/directions through the
same source-to-character bind-space conversion as equipment. It walks ancestors,
and recognizes duplicated helper trees by matching the full source bind transform
to a named bone present in the target skeleton. This is a remake adaptation based
on GRN data, not a recovered native name substitution. For SeraWings02,
`Bone_spine` has the same bind transform as `Bip01 Spine2` within export precision.
Its seven streak anchors now bind to that animated spine, instead of `__Root`.
The first anchor's composed rest Z changes from 13.65 to 72.23; the last two from
−9.79 to 48.79. Matching only positions or hardcoding `Bone_spine` was avoided.
Helper directions use the authored quaternion hierarchy rather than the full bind
matrix. `fx_streak02` and `fx_streak03` contain reflected scale-shear matrices;
transforming +X by those matrices reversed both trails inward and made them overlap
two lower helpers. Their authored rotations point all seven streaks outward.

Native quad mode 10 encodes 24-bit RGB plus one as an exact float integer in
`Normal.Z`; X/Y remain billboard offsets. Alpha is the draw color's alpha.
The vertex shader decodes and interpolates the four extracted colors. Mode 9
uses the regular uniform tint. Both preserve the original particle texture RGB.

## Reproduce and validate

From the repository root, using `C:\Users\Aytac\.dotnet\dotnet.exe`:

```powershell
dotnet run --project Sacred.Particles.Reader -- --exe 'E:\SteamLibrary\steamapps\common\Sacred Gold\Sacred.exe' --output Sacred.Particles/Generated --check
dotnet build SacredEngineRemake/SacredEngineRemake.csproj --no-restore -v quiet
dotnet run --project docs/research/model-particles/verification -- 'E:\SteamLibrary\steamapps\common\Sacred Gold\pak'
```

The running remake accepts these console cheats, including while unfocused:

```text
set character 1
set zoom 3
set overlays off
set debug-panel off
set hdr off
set lighting day
screenshot native-seraphim
set animation attack
```

Wait for the character-loaded console message before taking a screenshot.
Fixture 1 = 3073 + 4010; 7 = 1771; 10 = 5633; 11 = 3072 + 4010;
12 = 5632; 13 = inherited 2304 + 2176; 14 = inherited 7219; 15 = 3073 + 4092.
`set character next` also cycles these. Only the screenshot cheat
was used for captures, written under the original game's `Screenshots/Remake`.
`_scratch/item_fx_live.py` launches the remake with redirected stdin/stdout and
feeds appended lines from `_scratch/item-fx-commands.txt`; log is
`_scratch/item-fx-live.log`. It uses a hidden helper process and does not require
window focus. Do not pass `--terminal`, which reallocates the console handles.

`_scratch/ItemParticleProbe --attachments` prints raw versus composed wing helper
bindings. Its archive must load `Models.tmp` as well as `models.pak` to resolve
animations. `_scratch/item-particle-samples.txt` contains all six descriptors and
bone positions. The companion `native_evidence.py` regenerates relevant native
disassembly from a verified decoded image; no executable bytes are committed here.

The verification project loads all six real item models and a real character
animation, runs 240 frames per effect, checks finite coordinates and visible
particles, checks every chain link against its recovered length, checks that model
movement carries the chain head while retained points stay behind and move backward
relative to it, and checks all seven wing anchors against the animated spine. It
also verifies that walking changes both the wing emitter and the trail's shape.
The movement check freezes the skeletal pose and moves only the player/camera,
proving that locomotion leaves the retained wing points behind independently of
animation.
It also checks the serialized layout sizes. The current results are 50 quads for
1771, 54 for 3072, 50 for 3073, 700 for the seven 4010 trails including halos, and
13 for each torch (12 particles plus one lens flare). Chain layers are batched:
4010 uses 14 surfaces, not 700 draws.

### Validation completed on 2026-09-13

- SacredEngineRemake build: zero warnings and zero errors.
- Reader `--check`: all four generated sources match the supported executable.
- Verification project: the original samples plus 1876, 3112, 3116, 3111, and
  the 3809 glow line pass against the installed Gold archives.
- Live Sacred.Engine: all six items selected through console cheats; captures
  taken through the screenshot cheat with the window unfocused. These are remake
  captures, not original-game references; they show current fidelity limitations.
- Live Seraphim retest after the blend correction: all seven `fx_streak01..07`
  trails render with the fixed native corner colors and the authored `40` halo
  alpha. SeraWings02 contains exactly seven consecutive streak helpers.

| Items | Capture |
|---|---|
| 3073 whip and 4010 wing trails | [Seraphim](captures/3073-4010-trails.png) |
| 1771 red blade glow and sparks | [Worms](captures/1771-worms.png) |
| 3072 beam and 4010 wing trails | [Beam](captures/3072-beam.png) |
| 5632 fire | [Small torch](captures/5632-torch.png) |
| 5633 fire | [Large torch](captures/5633-torch.png) |

## Remaining fidelity work

1. The engine HDR particle pipeline uses a calibrated, bounded screen blend in
   PQ. It preserves emissive accumulation without using a PQ code directly as
   screen coverage, but remains an approximation of native SDR source-alpha
   additive output.
2. `renderGlowLine` randomizes the worm blade-glow half-size from 3 to 4 on each
   draw. The retained remake mesh currently uses 3.
3. The native random generator and exact random-call ordering remain to recover.
   Torch rotation, its 4×4 age atlas, and the separate type-3 lens flare are now
   implemented. Quality-dependent model presets have not been generalized into
   the runtime quality selector.
4. Static archives do not contain the live merged modifier rows consumed by the
   original selector. MageStaff generation-group suffixes are used only for the
   equipment preview; literal resolved modifier codes remain exact.

Continue from the evidence and limitations above, not the earlier screenshots
showing streaks at the feet or the older manually tuned inventory parameters.
