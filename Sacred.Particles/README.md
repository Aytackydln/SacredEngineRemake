# Particle catalogue

Latest decoding: [War Cry Sign](../docs/research/particle-definitions/war-cry-sign-fx.md)
adds the complete swept-fin event recipe: ground glow, eighty sector tests,
opposite fins and repeated highlights. Three layouts map saved state, event
duration, resources and mutable vertices. Coverage is **135 recipes / 103 FX rows**:
34 script and 99 event recipes plus two portals. **2,161,938 comparisons** pass
against 648 original updates/full renders and 80,262 complete submissions.
All 135 previews pass **34,973,095 geometry checks**; hidden console validation
produces **23 captures**. Reader/fresh-executable checks, generated-source
verification and Viewer compilation pass. Combat caller/notification dispatch,
moving attachment and world/Terminal surfaces remain pending.

Previous decoding: [Transdim, Pong Ball and Zhadum](../docs/research/particle-definitions/pulse-flare-fx.md) add three event recipes
with complete native ground/billboard renderers: all fifteen randomized Transdim
planes, Pong Ball's conditional hit flares and persistent glow, and Zhadum's triggered
hold/fade. Six layouts map saved blocks, resource tails and base controls. Coverage
is **134 recipes / 102 FX rows**: 34 script and 98 event recipes plus two portals.
**148,099 comparisons** pass against 1,620 original updates/full renders and 9,954
plane submissions. All 134 previews pass **33,032,790 geometry checks** across nine
actors. Hidden console validation produces **77 captures**; reader/generated checks
and Viewer/Terminal builds pass. White surface tint also restores BFG's ground glow.
Scene commands, world/Terminal surfaces and Pong minigame integration remain pending.

Previous decoding: [Panzer and Stahl](../docs/research/particle-definitions/column-flare-fx.md) add two event recipes with
complete 71-plane columns. Shared playback preserves strict grow/hold/fade phases,
rotation, stored slice heights, alpha falloff and native float light RGB. Three
layouts map saved state, event duration inputs and light/texture handles. Coverage
is **131 recipes / 99 FX rows**: 34 script and 95 event recipes plus two portals.
**720,093 comparisons** pass against 720 original updates/full renders and 51,120
complete plane submissions. All 131 previews pass **32,980,465 geometry checks**
across nine actors. Hidden console validation produces **48 captures**;
reader/generated checks and Viewer/Terminal builds pass. Standalone duration is
explicitly ten seconds; owner buffs, scene lights and world/Terminal surfaces
remain pending.

Previous decoding: [BFG and Trap02 Pill/Ghost](../docs/research/particle-definitions/sprite-flare-fx.md) add three event recipes
across three separate constructors sharing the billboard-flare helper. BFG keeps
all three animated grayscale layers and its ground glow; both traps preserve the
native asymmetric size pulse, blend selection and Pill removal flag. Five layouts
map saved state, base control inputs and resource handles. Coverage is **129 recipes
/ 97 FX rows**: 34 script and 93 event recipes plus two portals. **99,797 comparisons**
pass against 2,430 original updates/full renders, including exact boundaries,
restored continuation and three camera bases. All 129 previews pass **32,707,699
geometry checks** across nine actors. Hidden console validation produces **45
captures**; reader/generated checks and Viewer/Terminal builds pass. Owner/audio/light
integration, Trap02 parent gameplay and world/Terminal surfaces remain pending.

Previous decoding: [Live Undead, Call of Death and Trap01](../docs/research/particle-definitions/controlled-ground-flares.md) add three
complete event previews across three separate constructors sharing stdLensflare.
All four revival layers, fractional death-layer carry, both Trap01 toggle colours
and sixteen atlas cells preserve native state, float stores, UVs and blending.
Five layouts map saved state, event radius and toggle/notification bytes. Coverage
is **126 recipes / 94 FX rows**: 34 script and 90 event recipes plus two portals.
**133,884 comparisons** pass against 5,400 original updates/full renders, including
exact boundaries and restored continuation. All 126 previews pass **32,701,793
geometry checks** across nine actors. Hidden console validation produces **68
captures**; reader/generated checks and Viewer/Terminal builds pass. Gameplay
scene events, owner/audio integration and world/Terminal surfaces remain pending.

The shared simulator defaults to CPU SIMD with a scalar fallback. The
[execution settings, native ground collision and verification](../docs/research/particle-definitions/cpu-simd-simulation.md)
document the runtime path and the requirements for a future GPU simulation backend.

The world runtime now uses the [native offset, motion and color mappings](../docs/research/particle-definitions/runtime-motion-color.md),
including fade-indexed RGBA tables, wind, random half-ranges and native atlas/blend selection.

`SacredParticleCatalogue.LoadEmbedded()` loads the generated Sacred Gold FX
catalogue directly from this assembly. It contains 210 type entries, with complete
parameters for 16 script particle presets, eighteen direct-mesh script recipes and
98 event variants (including twenty-four event surface recipes) across low, medium and high quality. Unsupported
entries retain metadata and diagnostics. Loading requires no `Sacred.exe` or
instruction decoder at runtime.

[`Sacred.Particles.Reader`](../Sacred.Particles.Reader/README.md) preprocesses the
original executable into the checked-in `Generated/*.g.cs` sources. Its README
documents regeneration, verification and the runtime API. The definitions are a
foundation for implementing the native world simulation and rendering paths.

The [compiled-script/native preset investigation](../docs/research/particle-definitions/README.md)
supersedes the earlier fixture-based hypotheses for the sampled fire, lava smoke
and blue glints. `FunkCode.bin` places independent FX objects; `Sacred.exe` chooses
their native presets and texture names. The catalogue below also describes
existing renderer approximations and should not be read as a complete native mapping.

`Sacred.Particles` contains renderer-independent effect semantics shared by the
world and inventory renderers. A decoded texture's channel encoding and the
authored effect mode select a shader family; the numeric `Texture.pak` type does
not. In particular, type `4` means zlib-compressed ARGB4444 storage.

The latest [Fire Circle/Healing decoding](../docs/research/particle-definitions/fire-circle-and-healing-fx.md)
adds both native texture/palette branches to each class. Fire Circle uses native
standard emission and draining; Healing uses a decoded full-vector radial burst
with exact CRT sampling order and zero-time reseeding. Five layouts map original
save/event/creation bytes. Native comparisons pass 2,870,264 checks over 3,564
original updates/draw dispatches. Shared CPU/Terminal output preserves Healing's
pink Gouraud corner; gameplay events, audio/light and world GPU corner rendering
remain pending.

The preceding [Superfluid/Confusion decoding](../docs/research/particle-definitions/superfluid-and-confusion-fx.md)
covers all eight Superfluid strike states and Confusion's two rotating planes and
two crossed helical strips. Shared playback preserves native timers, random phase
reset/alpha carry, exponential plane height/reset and every saved byte.
Five layouts map save/event/helper inputs. 1,865,865 comparisons pass against 3,120
complete original updates/renders. Owner/audio/light integration, world placement
and Terminal surfaces remain pending.

The preceding [shield decoding](../docs/research/particle-definitions/shield-fx.md)
covers Shielded's two color branches, Holy Shield's rotating trail/ground symbol,
and Magic Shield's thirty markers and two pulsing rings. Shared playback preserves
growth/hold/shrink, hit consumption, native brightness decay and saved bytes.
Seven layouts map state, base creation and event inputs. 494,676 comparisons pass
against 3,360 original updates/full renders. Owner/audio/light integration,
world placement and Terminal surfaces remain pending.

The preceding [strike-symbol decoding](../docs/research/particle-definitions/strike-symbol-fx.md)
covers Crusader Strength and Speed Up with all eight texture states, native vertical
strip alpha gradients and both complete flare passes. Layouts map the saved timers,
random frame state and Speed Up's event rate. 76,848 comparisons pass against 1,920
original updates/full renders. World placement, light/finish notification handling
and Terminal surfaces remain pending.

The preceding [regeneration/light decoding](../docs/research/particle-definitions/regeneration-and-light-fx.md)
covers Mana Regen, HP Regen and MagLite with their authored layered ground planes,
rotating symbols, saved timers, strict retirement and Mana duration extension.
Typed layouts map MagLite light strength and native event inputs. 202,545 checks
compare 1,620 complete original updates/renders including the entire flare helper.
Actor following, scene light/owner mutation and world surfaces remain pending.

The preceding [Anducar decoding](../docs/research/particle-definitions/anducar-fx.md)
covers both event members with their five randomized models, quality counts,
endpoint delays, grow/hold/shrink controller and model matrices. Typed layouts
preserve the full 0x18C-byte saved state and event vectors. Playback comparisons
pass 247,176 checks against 1,080 original updates/renders, including exact
boundaries and snapshot continuation. Standalone body/glow previews are available;
native scene lighting/shadows and world mesh integration remain pending.

The preceding [Pong Batter decoding](../docs/research/particle-definitions/pong-batter-fx.md)
covers both family members. Shared playback preserves saved hit flash, scrolling
surface colours/UVs, all twelve beam segments and native endpoint-exclusive glow
sampling. A layout maps the twelve-byte saved state. Asset Viewer renders both
passes and offers the `fx-hit` cheat. Coverage is **106 recipes / 77 rows**;
minigame control, world scene placement and Terminal mesh rendering remain pending.

The previous [Underworld Glyph decoding](../docs/research/particle-definitions/glyph-fx.md)
covers all four members. Shared playback preserves the saved randomized waves,
180 points, ribbon thickness, model rotation and both scrolling texture passes.
Asset Viewer renders all 23 passes. State/event layouts map the native saved block
and selector. Coverage was **104 recipes / 75 rows** at that checkpoint; world mesh integration remains
pending. The recovered model-helper blend argument also corrects Forge's model.

The previous [Forge Element decoding](../docs/research/particle-definitions/forge-element-fx.md)
covers all five members. Shared playback preserves grow/hold/compress/fade stages,
model transforms, both ground flares and the 31-segment scrolling RGB trail.
It exposes native release and the finished Air branch's frame/type requests.
Two layouts map state/event fields. Asset Viewer renders the four passes and
offers Release controls. Coverage was **100 recipes / 71 rows** at that checkpoint; world mesh
integration and subsequent Air-object dispatch remain pending.

The previous [Element Stone decoding](../docs/research/particle-definitions/element-stone-fx.md)
covers all seven family members. `SacredElementStoneFxDefinition` carries native
model references, authored strip vertices, materials and flare constants;
`SacredElementStoneFxPlayback` shares the native clock and geometry independently
of a renderer. Asset Viewer renders the seven layers. World scene placement and
Terminal mesh rendering remain pending. Viewer coverage was 95 recipes / 66 rows at that checkpoint.

The previous [aura and cast decoding](../docs/research/particle-definitions/aura-and-cast-fx.md)
adds eight constructors and eleven event recipes. Stateful glow factors, delayed
starts and repeated random bursts preserve native timing and snapshot continuation;
ground lensflare geometry and source ONE RGB work through the shared renderer output.
The viewer covered 88 recipes across 59/210 rows at that checkpoint.

## Existing renderer behavior and observations

| Source signal | Observed example | Meaning | Shader family |
| --- | --- | --- | --- |
| RGB colour plus varying alpha | `MINIOBJ4X4_16_2_20.TGA`, `MIX3424.444` | Alpha-blended colour sprite | `StaticAlphaSprite` |
| Useful alpha with neutral or black RGB | `PARTICLE_GLOW01`, `FX_STREAKS01` | Shader-tinted alpha mask | `ItemGlow` or `ItemParticle`, selected by authored mode |
| Opaque alpha with black background | `PARTICLE_FIRE01` | Black is zero energy | `ItemParticle` |
| Items offset `0x04`, flags `0x00020008`, positive extent | candle/lantern halos | Offset `0x04` is the `Texture.pak` atlas ID; `Static.pak` supplies animation timing, and the unlit/class-8/extent combination supplies the visible halo | `StaticAlphaSprite` + `ProceduralHalo` |
| Texture-free class-9 item, positive extent, flags `0x00020009` | illumination records near `5100,606` | Invisible world-light marker; kept separate for the future illumination/shadow pass | not drawn by the halo pass |
| Mixed class-9 sprite, exact numeric candidate fields, strong blue pixel cluster | emitters at `2288,3131` and `4584,974` | Mixed fixture whose blue pixels are emissive while its stone/metal remains normally lit; the emitter generates animated star glints | `StaticAlphaSprite` + `ProceduralSparkle` |

## World sample mappings

| Screenshot coordinate | File chain | Result |
| --- | --- | --- |
| `2260,3136` | `Static.pak` 511752 -> Items row 9239 -> `mixed.pak` 671 | The fixture is separate from a script-created `TYPE_FX_FIRE_L` at tile `2260,3137`, with height 26 and the native fire preset. |
| `2288,3131` | Static item `LICHTER_HAENGEND_KLEIN` -> mixed fixture atlas `MIX3423.444` | The bowl is authored in the mixed sprite. Separate glints come from script-created `TYPE_FX_DWARFMAGIC_BLU` at tile `2292,3132`, using `PARTICLE_SPARK04.TGA`. |
| `4584,974` | Items 21918/21920/21921 -> `mixed.pak` -> `MIX3423/3424/3425` | The circled blue emitters are the `LICHTER_*` catalogue; class `0x09` alone is not selective because ordinary world sprites share it |
| `5100,606` | Mixed fixtures plus animated class-8 mini objects and separate class-9 light records | The animated mini objects supply visible candle halos. Texture-free light records are classified separately and intentionally do not become visible halos. |

For mini objects, Items.pak descriptor offset `0x04` is the `Texture.pak`
descriptor ID. For animated mini-objects, `Static.pak` bytes `0x2e..0x32` are atlas columns,
atlas rows, unused/static source size, frame duration in 50 Hz ticks, and frame
count. For non-animated mini-objects, bytes `0x2e`, `0x2f`, and `0x30` are source
X, source Y, and square source size. `Sector.pak` environment flags at `0x1cc`
describe boundaries/interiors and do not select particle effects.

`MiniObjTex20` has two observed `Static.pak` usages: flags `0x60`, layer 1
(1,127 candle/torch records), and flags `0x68`, layers 2/4/8 (1,312
surface-switched animated mini-object records). The added `0x08` is the surface-switch flag;
it is not an item-level particle or halo classification.

## Additional decoded events

The embedded catalogue now supplies 15 script presets and thirteen native one-time
event variants. Magic Prison includes its halo and native update order;
PuzzleSolved_v event subtypes 2/3 provide authored seed particles. Eleven
actor-mesh bursts include the confirmed Generic subtype 2 level-up effect. See
[native byte mappings and validation](../docs/research/particle-definitions/expanded-native-effects.md).
