# Sacred.Particles.Reader

Latest decoding: [Ring of Poison and Kamikaze](../docs/research/particle-definitions/expanding-burst-fx.md) adds two
complete event previews: a 150-slot particle burst, fifty expanding ring strips
and two independently rotating ground flares. Four layouts map saved state,
resources and Kamikaze's unsigned event radius rate. Coverage is **140 recipes /
107 FX rows**: 34 script and 104 event recipes plus two portals. **860,619 native
comparisons** pass against 1,440 original updates/renders and 34,560 complete
surface submissions, including standard particle draw dispatch and movement.
Both previews pass **16,546 viewer/world checks**; all 140 recipes pass
**34,998,385 geometry checks** across nine actors. Hidden console validation
produces **26 screenshot-cheat captures** with no missing textures. Fresh reader,
generated-source checks and Viewer/Terminal builds pass. Particle output uses the
shared world/Terminal path; world ring/ground surfaces, scene commands, lights
and combat event integration remain pending.

Previous decoding: [Bubble Trap Explosion and Terror Sprinter](../docs/research/particle-definitions/ring-surface-fx.md)
adds three event recipes across two constructors, including both Terror Sprinter
modes. Complete 50/20-segment rings preserve randomized rune resources, ground
glow, packed colors, blending and strict boundaries. Six layouts map saved blocks,
object resources and event inputs. Coverage is **138 recipes / 105 FX rows**:
34 script and 102 event recipes plus two portals. **984,001 comparisons** pass
against 1,728 original updates/full renders and 48,816 complete submissions.
The three recipes pass **8,744 targeted viewer checks**; hidden console validation
produces **37 captures** with no missing textures. Fresh catalogue comparisons,
generated-source verification and the Viewer build pass. Combat caller/owner buffs,
scene lights, moving attachment and world/Terminal surfaces remain pending.

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

Offline preprocessor for the particle catalogue in Sacred Gold's `Sacred.exe`.
It generates C# sources compiled into `Sacred.Particles`. The game loads those
embedded definitions without opening the executable, loading the reader, or
evaluating native instructions. Normal builds use the generated sources already
in this repository.

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

Previous decoding: [Fire Circle and Healing](../docs/research/particle-definitions/fire-circle-and-healing-fx.md) add four
event recipes covering both +0xA9 branches of both native constructors. Fire Circle
preserves its two textures, quality-scaled emission, strict cutoff and draining tail;
Healing seeds all 400 radial slots, preserves zero-time reseeding and both corner
palettes. Five layouts map saved state and creation/event inputs. Coverage is
**123 recipes / 91 FX rows**: 34 script and 87 event recipes plus two portals.
**2,870,264 comparisons** pass against 3,564 original updates/draw dispatches.
All 123 previews pass **32,693,065 geometry checks** across nine actors. Hidden
console validation captures all four samples and regressions. Reader/generated
checks and Viewer/Terminal builds pass. Shared CPU/Terminal output preserves the
pink Gouraud corner; gameplay event, audio/light and world GPU corner integration
remain pending.

Previous decoding: [Superfluid and Confusion](../docs/research/particle-definitions/superfluid-and-confusion-fx.md) add two
event recipes, covering both separate sole-member constructors. Superfluid includes
all eight strike states and its complete glow; Confusion draws two rotating ground
planes and both 100-vertex helical ribbons. Native random phase/alpha carry,
exponential height/reset, strict timers and five save/event/helper layouts are mapped.
Coverage is **119 recipes / 89 FX rows**: 34 script and 83 event recipes plus two
portals. **1,865,865 comparisons** pass against 3,120 original updates/full renders.
All 119 previews pass **30,950,832 geometry checks** across nine actors; hidden
console captures cover both rows, phase/height reset, retirement and regressions.
Reader/generated-source checks pass, and Viewer/Terminal build cleanly. Owner,
audio/light integration, world placement and Terminal surfaces remain pending.

Previous decoding: [Shielded, Holy Shield and Magic Shield](../docs/research/particle-definitions/shield-fx.md) add four
event recipes across three separate sole-member constructors. Both Shielded color
branches and all eight strike states are covered; Holy Shield rotates a brightness
trail and ground flare, while Magic Shield grows/shrinks thirty markers and two
independently pulsing ring layers. Both circular shields consume the native hit bit.
Coverage is **117 recipes / 87 FX rows**: 34 script and 81 event recipes plus two
portals. Seven new layouts map saved state and creation/event inputs.
**494,676 comparisons** pass against 3,360 original updates/full renders. All 117
previews pass **30,899,796 geometry checks** across nine actors; hidden console
captures cover all four recipes, hit/decay/retirement and regressions.
Reader/generated-source checks pass, and Viewer/Terminal build cleanly. Owner,
audio/light integration, world placement and Terminal surfaces remain pending.

Previous decoding: [Crusader Strength and Speed Up](../docs/research/particle-definitions/strike-symbol-fx.md) add two related event
recipes, covering both sole-member constructors and all eight texture states.
Coverage is **113 recipes / 84 FX rows**: 34 script and 77 event recipes plus two
portals. Native vertical strips preserve the opaque-base/transparent-top gradient;
both complete glow passes, strict timers, Speed Up's event rate and saved bytes
are mapped. **76,848 comparisons** pass against 1,920 original updates/full renders.
All 113 previews pass **30,860,440 geometry checks** across nine actors; hidden
console captures cover both new rows and regressions. Reader/generated-source
checks pass, and Viewer/Terminal build cleanly. World placement, light/finish
notifications and Terminal surface rendering remain pending.

Previous decoding: [Mana/HP Regeneration and MagLite](../docs/research/particle-definitions/regeneration-and-light-fx.md).
Three related layered ground-flare classes add three event recipes. Coverage is
**111 recipes / 82 FX rows**: 34 script and 75 event recipes plus two portals.
Mana/HP each draw a rotating symbol and five glow planes; MagLite draws 21 slices.
Layouts map the sixteen-byte saved timers, MagLite light strength and event inputs.
Native comparisons pass **202,545 checks** against 1,620 complete updates/renders,
including the entire flare helper, strict retirement and Mana extension. All 111
previews pass over 30.85 million geometry checks. Hidden console captures exercise
all three rows, `fx-extend`, retirement/restart and regressions; reader/generated
source checks pass and Viewer/Terminal build cleanly. Audio, actor following,
MagLite owner/light integration and world surfaces remain pending. All known
shared-constructor families are covered; the five Trap04 fire rows take a
non-allocating factory path and remain without particle recipes.

Previous decoding: [Anducar Spikes: both event members](../docs/research/particle-definitions/anducar-fx.md)
adds two event mesh recipes. The reader recovers all five GRNs, textures, quality
counts, random operands, timing and material arguments from the original code.
State/event layouts map the 0x18C-byte saved block and two endpoint vectors.
247,176 checks compare 1,080 original updates/renders, exact boundaries and restored
continuation. The catalogue embeds **34 script recipes and 72 event recipes per
quality**: 16 standard script emitters, 18 script meshes, and two new event meshes.
Script creation is unsupported for this family. Native world shadows/lighting
and scene integration remain pending.

Previous decoding: [Pong Batter: both family members](../docs/research/particle-definitions/pong-batter-fx.md)
adds one constructor and two direct mesh recipes. The reader executes all 36
constructor vertices and the original twelve edge endpoint assemblies; materials,
UV scale/scroll, hit-response size/density and grayscale operands come from native
code. A layout maps the twelve-byte saved payload. 490,920 comparisons execute
720 original updates/full renders, including every glow-line quad submission.
The catalogue embeds **34 script recipes and 70 event recipes per quality**:
16 standard emitters and 18 direct mesh recipes. No native event argument is
invented for the initializer, which does not read its event. Gameplay remains pending.

Previous decoding: [Underworld Glyphs: all four members](../docs/research/particle-definitions/glyph-fx.md)
adds one constructor and four direct mesh recipes. Original model dispatch,
constructor strip vertices, forty random initialization operands, wave geometry,
two scrolling texture matrices and flare materials are decoded from native code.
State/event layouts map the 0x924-byte serialized payload and UInt32 selector;
inline arrays preserve saved points, rates and radii. 1,187,592 checks compare
1,200 original updates and 1,212 renders. That checkpoint embedded **32 script recipes
and 70 event recipes per quality**: 16 standard emitters and 16 direct mesh recipes.
The model helper's blend argument also corrects Forge's model to alpha blending.

Previous decoding: [Forge Elements: all five members](../docs/research/particle-definitions/forge-element-fx.md)
adds one constructor and five direct-mesh script recipes. Model/material arrays,
62 constructor vertices, both ground flare helpers, growth/compression/fade
coefficients and Air's later frame/type requests are recovered from native code.
Two layouts map serialized state and event selection; 323,004 checks compare
3,000 original updates and full renders. `ForgeElement` metadata has no standard
particle draw or parameter sets. That checkpoint embedded **28 script recipes and
70 event recipes per quality**: 16 standard emitters and 12 direct mesh recipes.

Previous decoding: [Element Stone: all seven members](../docs/research/particle-definitions/element-stone-fx.md)
adds one constructor and all seven direct-mesh script recipes. The reader executes
native vertex construction and decodes the initializer's model references,
material overrides, transforms, flicker constants and flare helper selectors.
Three layouts map serialized state, event selection and surface vertices;
157,584 playback checks compare 3,150 original updates/renders. These definitions
have `ElementStone` metadata with no standard particle `Draw` or parameter sets.
The catalogue embedded 23 script recipes and 70 event recipes per quality at that checkpoint,
including 16 standard script particle presets and seven Element Stone meshes.

Previous decoding: [Cloud, auras and casting FX](../docs/research/particle-definitions/aura-and-cast-fx.md)
adds eight constructors and eleven event recipes. Sixteen layouts map recovered
state/event bytes. Original-code checks verify 108 complete byte blocks and 3,405
updates/renders, including full ground lensflare geometry and Burning Light's
inclusive one-second update limit. Constructor palettes, duration helper inputs,
delayed starts, random burst scheduling and glow envelopes come from native code.
The catalogue embedded 16 script recipes and 70 event recipes per quality at that checkpoint.
Ownerless durations/radii and owner visual modes are explicit standalone inputs;
the linked notes describe remaining event and attachment branches.

Previous decoding: [Bleed, Battle Fog, Gladiator hit and High Speed](../docs/research/particle-definitions/sustained-and-burst-fx.md)
adds four constructors and seven event recipes. Eight layouts map native state/event
bytes; original-code checks verify 63 complete byte blocks and 2,163 updates/renders.
The duration helper and High Speed's birth-size accumulator are decoded from native
instructions. The catalogue embedded 16 script recipes and 59 event recipes per quality at that checkpoint.

Previous decoding: [Seraphim hit and Blood Puddle](../docs/research/particle-definitions/hit-and-ground-fx.md)
adds two constructors, both Seraphim combat-art branches, ground-plane draw
metadata and three explicit state/event layouts. The catalogue embedded
16 script recipes and 52 event recipes at that checkpoint. Original-code checks
verify 27 complete byte blocks, 1,125 updates/renders and 16 original quad corners.

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

The [preceding continuation](../docs/research/particle-definitions/layered-fx.md) adds
three constructors and all member rows, five event recipes, complete layered
draws and quality branches, native flare animation and four byte layouts.

The embedded catalogue contains **210 `TYPE_FX_` entries**, with **16 decoded
script presets** in the smoke, dwarf-magic and Magic Prison families, plus
**75 decoded native event samples** of PuzzleSolved, PuzzleSolved_v, Generic,
Changeling, Sparks, Dustcloud, Windstrike, Burning Bone, Arac Attack, Laser Beam
Impact, Dwarf Shoot Impact, Elve SM Hit, Dragon Ground Fire, Wall of Fire Line,
Bat Signal, Shaddar Magic, Torch Smoke, Changeling Cast, Magic Fire, Magic Gift
and Fireball (all five palettes), Teleport (both IN/OUT members) and Reanimate, Nature Healing, Time Control and all three Small Fire subtypes,
including the confirmed level-up burst, both Seraphim SM Hit branches and Blood
Puddle, Bleed, Battle Fog (three palettes), ordinary Gladiator SM Hit and
High Speed (both selectors), Cloud, Fire Skin (both owner modes), Ground Poison,
terminal Freeze Impact, Gladiator cast (1002/1012/1015), Soulcatcher, Burning Light
and Songs from Hell. `EventDefinitions` and
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
after movement has populated the free-slot list, then advances by count Ã— interval.
Both preserve the crossing update before stopping emission. Windstrike's draw reads
per-particle RGB; Burning Bone requires actor context and uses a 4Ã—4 fire atlas.
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
