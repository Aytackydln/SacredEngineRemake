# Additional native particle families and event bursts

The verified Gold code image is the same profile used by Sacred.Particles.Reader
(`ee60108ce8147721717df1632c47b89c2b445a0255922219fa14a5980feadf37`).
The catalogue now contains 210 original FX types, 15 decoded script presets,
and sixteen decoded event
variants. Event variants use the original `(type ID, event subtype)` pair;
they do not acquire synthetic game IDs or override script creation selectors.

## Magic Prison

Factory `0x5A1DA3`, constructor `0x79C960`, initializer `0x79CC80`, update
`0x79C9F0`, render `0x79CA40`. The factory requests 100 particle slots.
The initializer writes a complete emission block, motion block and 256 colors
before `0x79CD8D`. That boundary avoids an unrelated object-ID/audio call.
Quality selects the authored emission interval. The update calls stdMovement
before stdCreation, unlike dwarf magic; the embedded definition records this
order separately from wind use.

Render `0x79CA8F` calls stdLensflare (`0x7631E0`) with the constructor texture,
half-size 40, color `0xFF8080FF`, zero rotation/offset, a single atlas cell,
and source/destination ONE blend arguments. The ordinary particle pass follows
at `0x79CABD`, with atlas side 1 and flags `0x19`. Both draws are reproduced.

The serializer `0x79CAD0` reads/writes 0x48C bytes beginning at object +0x20A0.
`SacredMagicPrisonParticleStateLayout` maps the payload:

| Payload offset | Meaning |
| --- | --- |
| 0x000 | elapsed time |
| 0x004 | active flag |
| 0x008 | 256 packed AARRGGBB colors |
| 0x408 | standard motion block |
| 0x428 | standard emission block |

The following texture handle at object +0x252C is runtime state, outside that
serialized payload. Padding at +0x005..+0x007 remains unknown.

## PuzzleSolved variant events

Factory `0x5A0F0F`, constructor `0x76CE60`, initializer `0x76D260`, update
`0x76CF20`. The factory requests 500 particle slots. Event +0x38 selects the
subtype; the initializer stores it at object +0x252C.

| Event subtype | Native initialization | Texture | Lifetime behavior |
| --- | --- | --- | --- |
| 0 | up to 500 particles seeded from the posed actor mesh | PARTICLE_SPARK06.TGA | standard motion and fade |
| 1 | up to 250 particles seeded from the posed actor mesh | PARTICLE_SMOKE03.TGA | black alpha smoke, standard motion and fade |
| 2 | 50 radial particles, Z=40, alternating Z velocity +/-70, size 25, mass -10 | PARTICLE_SPARK06.TGA | fade rate -512, size rate -30 |
| 3 | 75 radial particles, Z=0, Z velocity 70, size 10, mass 10 | PARTICLE_GLOW03.TGA | fade rate -200, size rate -8 |
| 4/5 | up to 150 particles seeded from the posed actor mesh | PARTICLE_SMOKE03.TGA | scatter with wind sampled at birth |

Birth energy is 255. Positions are computed by the original initializer using
sin/cos; the generated catalogue stores its resulting native cParticle array.
These are seeded once and integrated by stdMovement; there is no timed emitter
or looping toggle for these variants. Subtype 2 stops evaluation at `0x76D545`,
after the complete color table and before audio. Subtype 3 returns normally.
The render dispatch table at `0x76D08C` selects the texture; shared draw arguments
at `0x76D00E` give flags `0x19`, atlas side 1.

The serializer `0x76D0B0` confirms the 0x490-byte payload at object +0x20A0.
`SacredPuzzleSolvedVariantParticleStateLayout` maps the same common fields as
Magic Prison, plus byte 0x48C for the event subtype. The trailing padding is
explicitly unknown. Subtypes 0/1/4/5 use the actual posed actor mesh. For 4/5 the
extractor initializes the native environment constructor defaults: strength 0.2,
direction (1,0,0), recovered at `0x418F80`. Dynamic weather/save wind is not yet
supplied to these previews. Subtype evaluation stops at `0x76D355`, `0x76D42C`,
`0x76D862`, and `0x76D9F1`, respectively, before actor resolution/mesh seeding.
Gameplay callers exist
at `0x530706` (subtype 2) and around `0x541AA1`/`0x541F21` (subtype 3), but their
semantic attribution to level-up or critical-hit feedback is not established.
The UI therefore retains the native family and subtype names. The separately
confirmed level-up effect belongs to Generic, documented below.

## Actor mesh seeding and original PuzzleSolved

Native helper `0x767FB0` obtains posed geometry through `0x44FC30`. It caps the
requested count against both system capacity and available vertices, then walks
the mesh with stride `max(1, vertexCount / count)`. Position variation is added to
each vertex; the emission PositionOffset is not added. Other random fields use
the standard authored half ranges. Birth energy is 255, parameter-set index is
zero, and an atlas variant count of zero or one selects frame zero.

The runtime supplies the current posed player geometry, converted to the native
particle coordinate basis. These bursts are unavailable for point targets or
absent models; no surrogate actor geometry is synthesized.

Model seeding does not imply attachment. The level-up caller supplies a spawn
position to the FX factory (`0x57F4FA..0x57F504`), and Generic subtype 2/9 update
`0x7966F1..0x79670C` only invokes standard movement and stores its alive result.
It does not resolve or copy actor position after birth. Decoded actor bursts
therefore keep their sampled spawn origin and terrain height. The player preview
offers a separate, default-off follow override; self targeting alone never
repositions existing particles.

Original PuzzleSolved factory `0x5A0ED9`, constructor `0x76C930`, initializer
`0x76CD50`, seeds up to 500 vertices. Evaluation ends at `0x76CDFE` before actor
and audio calls. Native flags +0x7C are zero for this preview. Its extra homing
behavior when flag 0x4000 is set is not implemented. The selected Spark06 draw
uses flags 0x13, atlas side 1. Serializer `0x76CBA0` confirms a 0xA0-byte payload
at object +0x20A0, mapped in `SacredPuzzleSolvedParticleStateLayout`:
time 0, alive 4, speed 8, four corner colors 0x0C..0x18, motion 0x1C,
emission 0x3C. Padding 5..7 remains unknown.

## Confirmed level-up and impact smoke

Gold experience handler `0x57E160` adds experience and compares the resulting
level to the previous level. On gaining a level it creates TYPE_FX_GENERIC at
`0x57F4FF`, supplies subtype 2 at `0x57F54E`, and chooses a packed event color.
The default color is 0xFFD0A527 at `0x57F523`; actor/ownership branches use
0xFFC09517 or 0xFFC02020. The catalogue preview uses the default color and labels
this native event **Level-up**. It is not a renamed PuzzleSolved effect.

Generic factory `0x5A1BC9`, constructor `0x796150`, initializer `0x796E10`:
event +0x38 is packed color and +0x3C is subtype. Subtype 2 seeds up to 500 actor
vertices, size 7 +/-3, size change -5, Spark06, draw flags 0x11, atlas side 1.
Parameters are complete at `0x797021`, before actor resolution. Its update
branch `0x7966F1` uses standard movement without extra dynamic behavior.

Generic subtype 9 seeds up to 300 vertices with size 5, mass -20, size change 10,
fade change -350, and RGB 0x222222. Its draw uses flags 0x0C and atlas side 1.
Damage callers exist at `0x5336D2` and `0x560CD4`; the catalogue labels it
**Actor impact smoke**, without claiming critical-hit semantics. The identity
lookup region `0x7975AA..0x7975DC` is omitted from managed parameter evaluation;
it reads no actor fields and the runtime supplies the mesh separately. Parameters
are complete at `0x79764E`. Independent native evaluation executes that original
lookup path with identity stubs preserving native stack conventions.

Serializer `0x796C60` confirms Generic's 0x4C8-byte payload at object +0x20A0.
`SacredGenericParticleStateLayout` maps time 0, active 4, colors 8, motion 0x408,
emission 0x428 and subtype 0x4C4. The auxiliary helix/arrow region
0x48C..0x4C3 remains explicitly unknown until those subtypes are decoded.

## More actor mesh bursts

Generic subtypes 0 and 5 also use the native actor-vertex helper. Both are finite,
spawn-anchored bursts, with four fixed corner colors and Spark06 draws (flags
0x11, atlas side 1). The preview's red event color is read from the actor-state
caller at `0x541BDB`; subtype 5 uses that same explicit preview color, without
claiming a specific gameplay caller or critical-hit meaning.

| Generic subtype | Mesh limit source | Parameter boundary | Native update | Authored motion |
| --- | --- | --- | --- | --- |
| 0 | 500 at `0x796F24` | `0x796ED8` | `0x79630F` -> stdMovementWithGround `0x7645C0` | mass 150 +/-30, size 5 +/-2, XY velocity width 3, size change -2 |
| 5 | 250 at `0x7971E5` | `0x7971BD` | `0x7965F2` -> stdMovementWithGround `0x7645C0` | size 3, gravity change 30, size change -1.5 |

The movement calls pass ground mode zero: reflect vertical velocity on contact,
without damping or clamping particle position. Ground collision is preserved
as `GroundCollision` in generated definitions, rather than being lost during
offline catalogue serialization. Existing native ground-motion references cover
the same integration path.

Changeling factory `0x5A1B93`, constructor `0x795B30`, initializer `0x795E20`,
update `0x795BC0`, render `0x795C00` supplies two more mesh variants. Event +0x38
selects them. Both seed at most 500 actor vertices (literal at `0x795F01`), use
size 5 +/-2, size change -2, and four fixed green 0xFF80FF80 corners. Subtype 0
has mass -150 +/-30; nonzero subtype has mass +150 +/-30 and XY velocity width
3. Both update with ground bounce and use Spark06, flags 0x11, atlas side 1.

Actor identity/mesh calls `0x795EEC..0x795F1B` sit between motion and color
writes. The managed evaluator skips just that region and continues through
the fourteen creature-type writes, stopping at `0x795FCB` before transformation
or audio. Independent Unicorn validation executes the original path with native
calling-convention stubs for those external lookups and the mesh helper.
Particle previews do not invoke the gameplay transformation logic.

Serializer `0x795C70` confirms a 0xF0-byte payload at object +0x20A0.
`SacredChangelingParticleStateLayout` maps time 0, alive 4, four colors 8..0x14,
motion 0x18, emission 0x38, and the fourteen authored animal types 0x9C..0xD3.
The final 0x1C bytes remain unknown. Gold selects an animal-list index by modulo
fourteen at `0x7960BE`; these values are creature types, not texture IDs.

Generic subtype 6 is now decoded, completing the direct actor-vertex helper calls.
Its parameters depend on the actor's actual Items.pak blocking radius. Gold
`0x428CE0` reads the 128-byte item descriptor at +0x14 and substitutes 50 for a
valid item whose radius is zero; invalid IDs return zero. The particle initializer
scales size, gravity and size change at `0x7972B0..0x797302`. This use is recorded
on `ItemsPakEntryModelDescLayout.BlockRadius`; it is not decoded with an invented
actor radius. The offline reader evaluates its coefficients at unit radius;
`UsesActorBlockRadius` requires the actual Items.pak radius at playback.
Size is radius * 0.03, gravity radius * -5, and size-change rate radius * 0.3;
the parameter template retains the original float coefficient bytes. This event
seeds at most 300 vertices, uses standard movement and draws Spark06 with native
flags 0x19. Native colors are a 256-entry green table. Independent execution of
the original initializer now agrees byte-for-byte for this subtype at all three
qualities. Eighteen additional radius references cover the zero-field fallback,
positive radii and the full unsigned range. Further mesh/ghost families use other
geometry helpers. See [Asset Viewer progress](asset-viewer-progress.md).

## Remaining requested effects

The previously unsupported **Sparks** and **Dustcloud** families now have native
event previews. Their first-update births, parameter/colour bytes and serializers
are covered by [new FX decoding](new-point-bursts.md), independently of the
Generic/PuzzleSolved/Changeling variants documented above.

Generic subtypes 7/8 have additional time-dependent updates and resampling,
so their parameters alone are insufficient for faithful playback. Native body-part
and lost-soul families also require actor geometry/animation behavior beyond
standard sprite integration. They remain unsupported. No critical-specific
caller has been confirmed for the newly exposed variants.

## Validation and controls

`expanded_native_reference.py` independently executes the original initializers
in Unicorn. All emission/motion bytes, color-table bytes and 125 seeded particle
records match the managed extractor byte for byte at all three quality settings:
510 comparisons. The existing reader regression suite still compares all 81
previously recovered parameter sets. Runtime playback checks cover both CPU
modes, all qualities, self/point targets, loop rejection for native bursts,
exact seed counts, fixed/following origins and natural expiry (7,590 checks). In-game validation uses console cheats
and the screenshot cheat only.

```text
set particle-target self
set particle-play Level-up
set particle-play GENERIC:9
set particle-play GENERIC:0
set particle-play GENERIC:5
set particle-play CHANGELING:0
set particle-play CHANGELING:1
set particle-enable MAGICPRISON
set particle-play PUZZLE_SOLVED_V:2
set particle-target 2250,3142
set particle-play PUZZLE_SOLVED_V:3
set particle-stop all
```

The player window groups the native bursts under One-time (Play). Continuous
FX and model equipment toggles remain under Other; continuous FX also have a
Play button for a timed emission preview.
