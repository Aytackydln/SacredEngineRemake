# Particle catalogue

`Sacred.Particles` contains renderer-independent effect semantics shared by the
world and inventory renderers. A decoded texture's channel encoding and the
authored effect mode select a shader family; the numeric `Texture.pak` type does
not. In particular, type `4` means zlib-compressed ARGB4444 storage.

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
| `2260,3136` | `Static.pak` 511752 -> Items row 9239 -> `mixed.pak` 671 | The mixed sprite is the fixture beneath the circled jet. No particle-atlas/socket association has yet been found in its mapped fields, so the runtime does not add an object-name special case. |
| `2288,3131` | Static item `LICHTER_HAENGEND_KLEIN` -> `mixed.pak` 21917 -> `MIX3423.444` | The circled blue emitter, including its glints, is authored in the mixed alpha cutout; there is no MiniObj timing record |
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
