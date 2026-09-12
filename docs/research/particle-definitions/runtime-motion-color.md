# Particle offsets, motion and color

The runtime now consumes the native projection, spawn variation, wind, color
tables and atlas selection recovered from the verified Sacred Gold executable.
This continues the [sample investigation](../particle-emitter-samples/README.md).
The [disassembly](reports/native-evidence.md) includes the relevant routines.

## Coordinate and motion corrections

The simulation coordinates are Cartesian 3D coordinates. They are not the
tile axes passed to `WorldToIso`. `0x6239C0` first projects precise script XY
into the tile plane, then reverses the native camera transform. Consequently,
local particle movement must use the native view basis before returning to
the remake's isometric pixel coordinates.

The reference ortho volume at `0x6282C9` is X `[-267,267]`, Y `[-200,200]`.
The reference conversion at `0x623940` uses 1024 by 768. The default camera
eye at `0x811700` is `(0,1200,600)` relative to its target, with Z up.
For a displacement `(x,y,z)`, the resulting reference-pixel displacement is:

```text
dx = x * 1024 / 534
dy = (y / sqrt(5) - z * 2 / sqrt(5)) * 768 / 400
```

Script tag `7E` and the emission block's Z offset both contribute to height.
The native quad spans `[-size,+size]`; size is a half extent. These constants
are read from executable operands during preprocessing and embedded in the
catalogue, rather than adjusted for particular lamps or item IDs.

Fields named `RandomWidth` in the original layouts are **half-ranges**.
`0x765590` multiplies the stored range by `-1`, then interpolates between that
negative value and the positive value using `rand()/32767`. This corrects the
earlier total-width interpretation: the distribution is `base +/- range`.

`0x7640C0` advances position with the old velocity, changes velocity using the
old gravity, then updates fade, size, rotation and gravity. The implementation
now preserves this order. Smoke's update at `0x76E2D0` replaces motion slots
0 and 1's gravity direction with environment wind. The environment constructor
at `0x418F80` supplies direction `(1,0,0)` and strength `0.2`; these values make
negative-gravity fire drift left while rising. The runtime uses this recovered
default wind; changing weather/save wind is not yet implemented.

`0x7687F0` selects one parameter set per birth. Mode 2 chooses slots 0/1 with
80/20 probability. Mode 3 weights slots by reciprocal interval. There is one
emission timer based on the first interval, not independent full-rate timers
for every slot. Timed births catch up within the frame. Smoke updates before
births, while dwarf magic generates before its main update.

## Serialized bytes and draw behavior

| Layout / offset | Recovered meaning |
| --- | --- |
| Smoke state `+00C`, `+40C`, `+80C` | Three 256-word AARRGGBB color tables |
| Dwarf-magic state `+008` | One 256-word AARRGGBB color table |
| Emission `+54` | Packed per-channel AARRGGBB random half-ranges |
| Smoke state `+D98` | Geyser quiet/burst countdown, decremented by dt |

The state offsets are relative to the serialized payload at object `+20A0`.
All are mapped in `Sacred.Core/Particles`. Unused/unwritten tables stay empty
in the extracted catalogue. Non-table presets initialize just four corner
colors in the same region.

`0x763F50` builds color tables from RGBA segments, storing birth color at index
255 and later colors at decreasing indices. Draw flag `08` reads
`colors[parameterSet * 256 + truncate(fade)]` at `0x762259`. Its alpha is already
the authored alpha curve: applying `fade/255` again would change the effect.

The draw arguments also provide atlas side length. Flag `100` chooses a
birth-selected cell; otherwise the frame follows `(255-fade) * cellCount / 256`.
Rotation is applied to the billboard. No fixture/texture-ID switches are used.

Native flag `01` selects destination ONE, flag `10` selects source ONE;
otherwise the respective factors are INV_SRC_ALPHA and SRC_ALPHA. With two
parameter sets, `0x761F87` overrides the destination factor: slot 0 is additive,
slot 1 alpha-blended. This matters for fire turning dark: an additive black
particle contributes no light instead of covering the fixture with black smoke.
The existing premultiplied sprite pipeline implements additive contributions
by emitting zero destination-attenuation alpha. Texture brightness is no longer
used as an invented particle alpha mask.

## Validation

The independent Unicorn reference runs the original initializer code. All 81
emission/motion blocks and their color data match the managed extractor exactly,
across low, medium and high quality. Extracting colors exposed a one-alpha-byte
rounding difference from double arithmetic; the reader now uses the x87 64-bit
significand for intermediate arithmetic. The check program passes 2,704 checks.

`native_motion_reference.py` separately executes the original integrator with
nonzero XYZ position/velocity, changing gravity, inward acceleration, fade,
size and angular motion. `ParticleRuntimeChecks` passes 25 comparisons covering
that trajectory, independent projection equations, height, fade lookup and
the corrected random half-range.

```powershell
py docs/research/particle-definitions/native_motion_reference.py `
  --memory-image _scratch/particle-current-memory.bin `
  --output docs/research/particle-definitions/reports/native-motion-reference.json
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run `
  --project docs/research/particle-definitions/ParticleRuntimeChecks -- `
  docs/research/particle-definitions/reports/native-motion-reference.json
```

Live validation uses console teleport/lighting commands and the `screenshot`
cheat's rendered frame capture. Reference stills establish placement and shape,
not exact particle phase. Script execution/save-state selection and unsupported
effect families remain outside the implemented smoke/dwarf-magic families.
