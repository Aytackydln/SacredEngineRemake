# Sacred.Particles.Reader

Offline preprocessor for the particle catalogue in Sacred Gold's `Sacred.exe`.
It generates C# sources compiled into `Sacred.Particles`. The game loads those
embedded definitions without opening the executable, loading the reader, or
evaluating native instructions. Normal builds use the generated sources already
in this repository.

The reader also generates equipment FX constructor/helper associations into
`EmbeddedModelFxAttachments.g.cs`. Names come from the original GRN helper table,
and targets come from native preview constructor calls. See
[equipment FX attachments](../docs/research/particle-definitions/weapon-fx-attachments.md).

## Regenerate the embedded catalogue

From the repository root:

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run --project Sacred.Particles.Reader -- `
  --exe 'E:\SteamLibrary\steamapps\common\Sacred Gold\Sacred.exe' `
  --output Sacred.Particles/Generated
```

Add `--check` to compare the generated sources with the executable without
writing files; missing or stale sources return exit code 1. The three quality
catalogues are prepared before writing any output. Build the consuming project
after regenerating them.

This is an explicit preprocessing step rather than a build-time source generator:
building or running the remake does not require an installed copy of Sacred.
The preprocessor reads the executable as bytes and never starts a Sacred process.
It decodes the loader's rolling XOR in memory, verifies the native code hash, and
follows the original factory, creation, initializer and texture-selection paths
using a bounded managed instruction evaluator. Iced is used only by the reader
to decode instructions. No modified executable or custom runtime data file is
produced.

## Runtime API

```csharp
using Sacred.Particles;

var catalogue = SacredParticleCatalogue.LoadEmbedded(SacredParticleQuality.High);
// creation is a decoded SacredScriptCreateObject, whose TypeId came from tag 0x02.
if (catalogue.TryGetDefinition(creation.TypeId, out var definition)
    && definition.Status == SacredParticleDefinitionStatus.Decoded)
{
    var draw = definition.Draw;
    var parameterSets = definition.ParameterSets;
    // Feed these native parameters into the future world particle implementation.
}
```

Loading is cached per quality. `Definitions` includes unsupported entries with
diagnostics, and `ExecutableSha256` / `NativeCodeSha256` identify the source.
The stored type ID is the catalogue key; the executable's table index is not an
ID. Script execution and choosing the active script/save state remain separate
from loading particle definitions.

## Coverage and remaining work

The [latest continuation](../docs/research/particle-definitions/layered-fx.md) adds
three constructors and all member rows, five event recipes, complete layered
draws and quality branches, native flare animation and four byte layouts.

The embedded catalogue contains **210 `TYPE_FX_` entries**, with **16 decoded
script presets** in the smoke, dwarf-magic and Magic Prison families, plus
**49 decoded native event samples** of PuzzleSolved, PuzzleSolved_v, Generic,
Changeling, Sparks, Dustcloud, Windstrike, Burning Bone, Arac Attack, Laser Beam
Impact, Dwarf Shoot Impact, Elve SM Hit, Dragon Ground Fire, Wall of Fire Line,
Bat Signal, Shaddar Magic, Torch Smoke, Changeling Cast, Magic Fire, Magic Gift
and Fireball (all five palettes), Teleport (both IN/OUT members) and Reanimate, Nature Healing, Time Control and all three Small Fire subtypes,
including the confirmed level-up burst. `EventDefinitions` and
`TryGetEventDefinition(typeId, subtype, out definition)` expose event variants
without assigning invented IDs. Native bursts carry authored initial particle
records, a first-update point burst, or a native actor-mesh seed count and lifetime parameters; continuous
systems carry emission parameters. Mesh bursts require the current posed model.
Texture names, draw arguments, capacities and birth/integration order come from
the executable. The Magic Prison preset also includes its native halo draw.

The [preceding continuation](../docs/research/particle-definitions/teleport-and-reanimate-fx.md)
adds two constructors and every member of both families: Teleport IN/OUT (2/2)
and Reanimate (1/1). Teleport has a manual 500-particle algorithm and native
six-stage envelope; its definition has no fabricated standard parameter blocks.
Reanimate uses a rotating birth origin, point-release interpolation and decoded
draw scale. Five explicit layouts record the recovered state/event/particle bytes.
Native proofs cover 7,128 teleport and 798 reanimate original update calls.

The [preceding continuation](../docs/research/particle-definitions/line-and-fireball-fx.md)
adds three previously unsupported constructors and seven event recipes. Magic
Fire/Gift use the newly decoded stdCreationOnLine helper, independent endpoint
histories and release interpolation. Fireball's palette dispatch and sinusoidal
flare size are implemented. Five new layout classes record serialized state,
attachment vectors, line-release histories and the base light radius. Native proof covers 63 complete
parameter/palette byte comparisons and 1,512 original updater calls.
Native strength coefficients are decoded from both line-family initializers and
embedded as metadata. Runtime evaluation is verified against 36 more original
initializers, 72 complete emission/motion blocks and stored-strength rounding,
including the full unsigned event input range.

The [preceding continuation](../docs/research/particle-definitions/cyclic-and-flare-fx.md)
completes Smoke's 13 presets with Geyser's native countdown controller and adds
Torch Smoke and Changeling Cast constructors, including their flare rendering.
Native checks cover 4,500 Geyser updates, the two new event initializers/updaters,
and eleven original lensflare alpha branches. Geyser runs on both particle GPU
backends and preserves its controller in snapshots.

The [preceding eight constructor implementations](../docs/research/particle-definitions/standard-fx-families.md)
add fourteen native event recipes. Their complete initializers include all 256
palette entries; original-code checks cover 126 emission/motion/palette comparisons
and 1,008 updater calls. Bat Signal is continuous and needs actor context; the
others are finite bursts or timed releases. Previous-age emission, the laser's
skipped first movement and native draw pivot/rotation gates are implemented.

The previously decoded Sparks and Dustcloud families require no actor. Their
`BurstOnFirstUpdate` metadata preserves their different native creation/movement
orders. Sparks derives its burst count from the factory's 80-slot vector;
Dustcloud emits 40 once, gated by the serialized byte at state +0x005.
See [new FX decoding](../docs/research/particle-definitions/new-point-bursts.md)
for byte mappings and independent original-updater references.

Windstrike and Burning Bone add finite `EmissionDurationSeconds` and
`SingleBirthInitialization`. The native release clock initializes with one birth
after movement has populated the free-slot list, then advances by count × interval.
Both preserve the crossing update before stopping emission. Windstrike's draw reads
per-particle RGB; Burning Bone requires actor context and uses a 4×4 fire atlas.
Its current preview uses the actor origin; bone attachment and nonzero event size/time
inputs remain pending. See [timed FX families](../docs/research/particle-definitions/timed-fx-families.md).

`NativeConstructorAddress` is recovered for supported and unsupported entries by
following the factory's allocation/constructor path, including direct jumps.
Shared addresses establish family membership; delegated or unresolved constructors
remain null. The [membership report](../docs/research/particle-definitions/reports/fx-family-membership.md)
joins all 210 Items.pak FX rows and records coverage over every family member.

Generic subtype 6 carries `UsesActorBlockRadius`: its embedded parameters are
normalized at unit radius, then resolved with the actual actor's `Items.pak`
blocking radius at playback. A zero authored radius uses Gold's native fallback
of 50. Playback requires this input; it does not guess an actor size from mesh bounds.
The Asset Viewer/Engine callers pass their resolved actor data. See
[FX progress](../docs/research/particle-definitions/asset-viewer-progress.md).

There are 194 script entries still pending mappings, including families needing
actor/event input. Geyser's initializer and controller are decoded. Unsupported definitions
contain no invented parameter blocks. Recovered serialized state bytes are
mapped in the layout classes; padding remains explicitly unknown.
See [additional native effects](../docs/research/particle-definitions/expanded-native-effects.md)
for addresses, payload mappings and independent native validation.

The current profile supports the verified Sacred Gold native code hash
`ee60108ce8147721717df1632c47b89c2b445a0255922219fa14a5980feadf37`.
The source executable used here has SHA-256
`4df6659352282a0e57bdf69d2ac33396200fd0b54670d327cb9822ffdc4891cd`.
Unknown code builds fail with a diagnostic rather than reusing these addresses.

## Validation

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run --project docs/research/particle-definitions/ParticleReaderChecks -- `
  docs/research/particle-definitions/reports/reader-native-reference.json `
  'E:\SteamLibrary\steamapps\common\Sacred Gold\Sacred.exe'
```

The checks compare all **81 parameter sets byte-for-byte** against independently
evaluated native initializers across all three qualities. They also check the
embedded data against fresh preprocessing, all 35 sampled script commands,
malformed inputs, cancellation and explicit unsupported results. The executable
argument is optional when checking only the embedded catalogue and native
reference. A standalone Native AOT smoke application referencing only
`Sacred.Particles` also successfully loaded all three qualities.

See [the research notes](../docs/research/particle-definitions/README.md) for
byte layouts, native addresses and sample evidence.
