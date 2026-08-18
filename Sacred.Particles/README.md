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
| `MiniObjTex*`, flags `0x00020008` | candle flames | Animated static sprite; `Static.pak` decides whether it is surface-switched | `StaticAlphaSprite` |
| Mixed fixture with a particle socket | `Coalpot 1`, `DungeonA79` | Ordinary static art plus a separately animated `Texture.pak` particle and local light | `StaticAlphaSprite` + `ItemParticle` + `ProceduralHalo` |
| `SimpleLight*`, no texture/mixed group, extent greater than zero, flags `0x00020009` | lights near `5100,606` | Texture-free halo marker; extent supplies size | `ProceduralHalo` |
| `LICHTER_*` mixed sprite, low render class `0x09`, transform `0x0100`, zero extent/texture/timing | emitters at `2288,3131` and `4584,974` | Mixed fixture whose blue-white pixels are emissive while its stone/metal remains normally lit; the emitter generates animated star glints | `StaticAlphaSprite` + `ProceduralSparkle` |

## World sample mappings

| Screenshot coordinate | File chain | Result |
| --- | --- | --- |
| `2260,3136` | `Static.pak` 511752 -> Items `Coalpot 1` -> `mixed.pak` 671 | The coalpot is the fixture beneath the circled jet. Its mixed sprite remains static; a transposed `PARTICLE_FIRE02.TGA` 4x4 atlas supplies the separate fire billboard. The nearby surface-switched `MiniObjTex20` is unrelated tower candle art. |
| `2288,3131` | Static item `LICHTER_HAENGEND_KLEIN` -> `mixed.pak` 21917 -> `MIX3423.444` | The circled blue emitter, including its glints, is authored in the mixed alpha cutout; there is no MiniObj timing record |
| `4584,974` | Items 21918/21920/21921 -> `mixed.pak` -> `MIX3423/3424/3425` | The circled blue emitters are the `LICHTER_*` catalogue; class `0x09` alone is not selective because ordinary world sprites share it |
| `5100,606` | Items `DungeonA79` mixed fixtures plus animated `MiniObjTex20` and separate `SimpleLight*` records | `DungeonA79` supplies the circled iron torch socket, `PARTICLE_FIRE02.TGA` supplies its fire, and a separate local halo supplies emitted light. Candles remain ordinary animated static sprites; texture-free halo markers are separate authored objects. |

For animated mini-objects, `Static.pak` bytes `0x2e..0x32` are atlas columns,
atlas rows, unused/static source size, frame duration in 50 Hz ticks, and frame
count. For non-animated mini-objects, bytes `0x2e`, `0x2f`, and `0x30` are source
X, source Y, and square source size. `Sector.pak` environment flags at `0x1cc`
describe boundaries/interiors and do not select particle effects.

`MiniObjTex20` has two observed `Static.pak` usages: flags `0x60`, layer 1
(1,127 candle/torch records), and flags `0x68`, layers 2/4/8 (1,312
surface-switched animated mini-object records). The added `0x08` is the surface-switch flag;
it is not an item-level particle or halo classification.
