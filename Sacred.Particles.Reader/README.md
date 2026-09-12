# Sacred.Particles.Reader

Offline preprocessor for the particle catalogue in Sacred Gold's `Sacred.exe`.
It generates C# sources compiled into `Sacred.Particles`. The game loads those
embedded definitions without opening the executable, loading the reader, or
evaluating native instructions. Normal builds use the generated sources already
in this repository.

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

The embedded catalogue contains **210 `TYPE_FX_` entries**, including **14 fully
decoded definitions** in the smoke and dwarf-magic families. Low, medium and high
quality each contain 27 written parameter sets. Texture names, draw flags,
factory addresses and class-local preset numbers come from the executable;
generated numeric IDs are extracted data, not manually authored dispatch rules.

The other 195 entries have unsupported families. One additional geyser
initializer needs an external CRT/random routine and remains unsupported.
Unsupported definitions do not contain fabricated parameter blocks. Unknown
bytes in the recovered Core layouts remain explicitly unmapped. Native time
units, complete draw-flag semantics and the simulation/rendering loop still need
work before these definitions can reproduce the original effects in the world.

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
