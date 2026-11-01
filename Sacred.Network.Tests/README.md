# Packet tests

30 October [Horse dependency continuation](../docs/game-server/host-startup-horse-continuation.md): loaded indoor creation,
level-only state changes and implicit unnamed Horses now compare with complete
native commands. Startup reaches **1,755 commands, 92 NPCs and 290 private messages**,
stopping in `grabsteindialogvars` at FunkCode +0x828D (condition tag 0x6E). The next
closure also needs native +Var name expansion and opcode 111 loop control. All
1,417 network tests and 4,279 installed checks pass; Engine/terminal builds are
clean. Full host upload/readiness and Engine acceptance remain unfinished.


The startup continuation adds native name/duplicate/binding comparisons (222
assertions) and complete SetMapIcon producer comparisons (204 assertions), plus
a 0x1CB wire example using shared envelope/ownership/atomic-dispatch/TCP checks.
Fixed Effects add 24 complete native factory/command cases, nine complete native
update/render backing comparisons, six projections and three complete reads.
The three replay suites contain 354 assertions, including the shared producer's
complete state, RNG, reference/counter/scheduler lifetime and private publication.
Unknown received bytes remain lossless; only the matched family's independently
proven unused ranges are canonicalized when producing its initial state. Renderer
matrices, GPU submissions/queries, loaded-sector and disabled sound are explicit
native boundaries. Regenerate with `verify-startup-effect-factory.py`,
`verify-startup-effect-consumers.py` and `verify-startup-effect-receive.py` (included
in `test-host-construction.ps1 -Native`).

The [item/service/level continuation](../docs/game-server/host-startup-items-and-levels.md)
adds 66 original item factories/commands and 60 real item reads/position updates,
including all ten signposts and a valid-cache positive control. Its real placed
`0x16E` example reuses the shared wire/TCP contracts. HorseMerchant operand
ordering, twelve service/dialog cases, seven name-flag cases and eighteen distinct
assigned/requested level-phase cases add four suites with 10,411 comparisons.
The original caller produces level 6 from the first requested 5; the coupled
equipment/result/stream evidence was regenerated for that correction. All 1,382
network tests pass. Installed/live startup reaches the Horse constructor at
+0xAF32 after nine NPCs, ten Effects and ten QuestItems. Complete host upload
and Engine readiness remain unfinished.

Run from a checkout, without starting a game, installing Sacred, or emulating input:

```powershell
./docs/game-server/tools/test-packets.ps1
./docs/game-server/tools/test-packets.ps1 -Packet 0x1CB
./docs/game-server/tools/test-packets.ps1 -Filter 'TestCategory=Replay'
```

Or use Rider's test runner / `dotnet test Sacred.Network.Tests`.
The script uses the configured local dotnet path, falling back to `dotnet` on PATH.
It prints a short summary on success and the last 80 diagnostic lines on failure.
Full logs and TRX reports go to the ignored `Sacred.Network.Tests/TestResults` directory.
Missing evidence fails the affected test; it is not silently skipped.

The suite includes packet examples and native state-replay suites. A test count
is not an assertion count: each native replay suite contains many existing
comparisons against original results.
Only the portable checks have been promoted; this does not replace every older
scratch, Engine/render, installation or live-native scenario.

`CreatureFactoryIdentity` compares 1,593 original NPC identity/heading and
unpublished factory continuations (27,086 assertions). Heading vectors also
have an independent original x86 hardware comparison. `CreaturePlacement` adds
12,032 assertions across 256 ground/sector-deactivation cases and 256 distinct
direct dormant script placements, including tile chains, sector swap removal,
node flags, queue tombstones and exact retained times. Allocation/caller branch
selection, world lookup, elevated/owned/loaded state, later equipment and placed
serialization remain outside those replay boundaries. `CreatureAllocation`
adds 11,520 assertions over 768 original reserved allocations, including shared
counter wrap/controlled reentry, failed construction and registration before
activation. Its constructor/storage are supplied boundaries; reference selection
and occupied slots remain separate. Containers expose their counter for shared
reuse. Regenerate with `verify-creature-allocation.py`,
`verify-creature-factory-identity.py` / `verify-creature-flat-placement.py`;
`test-host-construction.ps1 -Native` includes both without resuming loot work.

`CreatureDeathConsumerTests` covers `17D` HP/combat transitions, active corpse
lifetime, authoritative revival, reference reuse, copied posed-mesh disappearance
seeds and FX expiry. `NativeReplay/CreatureDeathChecks` compares event `208`, death
motion selection and detail-level cleanup delay against 45 original instruction
cases. See [death delivery](../docs/game-server/consumer-creature-death.md) for the
separate Engine TCP/render and AssetViewer checks and their acceptance boundaries.

`CombatVisualConsumerTests` exercises ordinary blood, optional `17A` flash textures,
root/neck attachment, repeated hits, expiry, HP corrections, creature ground symbols,
fight-mode visibility, zero-damage deflection streaks, level colours and
reference/session cleanup. `NativeReplay/CombatVisualChecks` adds 936 original
instruction comparisons for flash clocks, relative-level colours, blood palettes
and deflection initialization, trail ordering and position/velocity/lifetime updates.
`CombatStartupChecks` verifies initial advertised difficulty and native suppression
of zero-damage publication. Explicit zero-damage playback does not establish an
ordinary native miss trigger. `SessionDiscoveryConsumerTests` covers the missing
announcement-to-session initialization, endpoint matching and later `01E` precedence.
Native checksum/login examples also check independent original bytes so matching
encoder/decoder mistakes cannot hide behind a successful round trip.

The [hover/health correction](../docs/game-server/consumer-health-hover.md) supersedes
the older blanket combat visibility: `CreatureHealthVisibilityTests` exercises
enemy hover and local combat independently. Native health/relationship replays
cover the one-second clock, exact fade alpha, class matrix, species exceptions,
animal links, rider and PvP/team ownership. Controlled Engine screenshots remain
separate from original-host pointer/visual acceptance.

## Adding a packet

1. Add an independent valid wire example to
   [PacketCatalog.cs](Contracts/PacketCatalog.cs). Use literal bytes/words or an
   existing native fixture, rather than the production producer being tested.
   Supply its expected event type and meaningful field assertions. Declare whether
   payload length is exact, permits a retained suffix, or has a structured tail.
2. Add native-result state comparisons under `NativeReplay` when the packet changes
   world state. Register the suite in `NativeReplayTests.cs`; use the existing
   `docs/game-server/evidence` artifacts and record the original-instruction boundary.
   Codec round trips alone do not establish native behavior.
3. Run `test-packets.ps1 -Packet 0xNNN`. This narrows the shared packet cases while
   retaining all promoted native state regressions and the core event coverage check.

For example, the existing map-icon entry supplies the opcode, 24 independent bytes,
`GoldMapIconEvent`, its suffix rule, signed-curvature assertion and ownership rule.
It automatically participates in all shared contract and TCP tests.

Those checks cover:

- Expected decoded type and fields.
- Every shorter payload, excluding explicitly valid alternate forms.
- Whole-batch rejection without partial event delivery.
- Extra-byte rejection or suffix preservation, according to the declared rule.
- Payload ownership where the codec promises it.
- Sacred message/envelope bytes, routing identities and sequence endpoints.
- Real `GoldGameConnection` receipt over fragmented TinCat TCP on an ephemeral port.

`GoldLoopbackPeer` owns its listener, connection, cancellation and cleanup. Reuse
it for further transport cases; do not create another packet-specific fake server.
Tests have no scratch-project dependency or hardcoded installation path.

`CreatureLootEvent` and `CreatureLootDecisionSession` compare source counts,
death caller inputs, rune thresholds, retry loops, source choices, potion
resources and money amounts against original instructions. The composed host
checks replace prepared selection/construction with recorded callback outcomes
and known RNG consumption. They do not establish full monster deaths, special
item construction, world placement or an Engine loot oracle. See the
[exact native boundary](../docs/game-server/host-creature-loot-decisions.md).

`CreatureLootEntry`, `CreatureDeathRewards`, `LootSpecialResources` and
`CreatureDeathPrefix` add 59,922 assertions for death eligibility/attribution,
boosted-source recipes, authored RNG reseeding, progression forcing, candidate
gates, signed inclusive ranges, descriptor/shared lookup, ordered modifier
families and unique filtering. Composed native-prefix checks preserve callback
order/RNG and unresolved stops. Runtime relation/stat/clock inputs, detach,
equipment generation and post-entry invocation bodies remain supplied dependencies.
These are not complete death events or an Engine loot oracle; see the
[continuation and substitutions](../docs/game-server/host-creature-death-loot.md).

`EquipmentReward`, `EquipmentLiveSelection`, `CreatureLootGeneration` and
`CreatureLootUnique` compare another 6,656 original-instruction cases: reward
attempts/money calls, final-selection fit retries and resource aliases, difficulty
reversal and generation/upgrade decisions, selector-zero replacement/fallback and
effect ordering/RNG. Concrete fit, creation/destruction, upgrade and publication
bodies remain supplied. Reward caller scratch context is explicit. See the
[wrapper boundaries](../docs/game-server/host-loot-construction-wrappers.md).

`CreatureLootSets` and `EquipmentFit` add 33,847 native-result assertions across
1,544 source/set decisions and 6,144 actual class/requirement fits. These preserve
byte row indexing, equipped-member RNG, strict level gates, class flag mutations
and skill/variant short circuits. Runtime object/type/value/skill probes and
variant results remain supplied; see the [set/fit boundary](../docs/game-server/host-loot-sets-and-fit.md).

The coverage check currently requires an example for every concrete record in
`GoldGameEvent.cs`, with additional examples for recent packets in other files.
It does **not** claim every branch of every gameplay decoder is covered.
An unknown `-Packet` selection fails instead of reporting a green zero-test run.

Native-instruction emulation, rendering and live-original-server acceptance remain
separate checks. Run them when a change affects those boundaries; the fast suite
reuses their recorded evidence rather than reopening executables or rerunning all
research for each packet.

Host construction adds `CreatureComponentSeed`, `CreatureLevelSelection` and
`EquipmentZeroQuality` native replay suites: 7,683 assertions against original
instruction results, with portable original-file prefixes retained in evidence.
These compare initialized component inputs, level/experience/skill selection,
and the zero-budget equipment socket/clearing/rarity stage. Full creature results,
equipment installation, startup execution and Engine/live acceptance remain
separate. Run `docs/game-server/tools/test-host-construction.ps1` for the packet
suite and affected builds; `-Native -Installation` also reruns the original
instruction fixtures and existing installed-data suites. See the
[dependency audit](../docs/game-server/host-creature-initialization.md).

The 27 October startup continuation adds `EquipmentInstallation`,
`EquipmentVariantNormalization`, `CreatureResultInitialization` and
`CreatureInnateBonuses`: 3,768,378 byte/state/order/guard comparisons after adding
PC=24 innate coverage. The existing
variant suite additionally checks 32,355 original source aliases against Gold
and pureHD. Installer effect bodies remain supplied. Result initialization is a
prefix; supported innates exclude active legacy power operations 23..36, which
fail before mutation. An isolated x86 hardware oracle compares the innate block
independently of Unicorn and retains its power discrepancies. See the
[exact boundaries and remaining first-creature work](../docs/game-server/host-startup-creature-results.md).

`CreatureBaseResults`, `CreaturePassiveSkills`, `CreatureEquipmentResults`,
`CreatureResultFinalization`, `CreatureScalarResults` and `CreatureHealthResults`
add 16,865,336 comparisons. These cover base profiles/Balance, ordered equipment,
empty-art skills, scalar finalization, 3,072 composed pre-health NPC calculations
and 9,216 ordinary health calls. Health power prefixes additionally run on x86
hardware in explicit 24/53/64-bit modes. `StartupFloatingPoint` proves
three original initialization/reset calls select **24-bit precision**: 61/64
retained result blocks differ from the wider-mode boundary. Gold/pureHD selection
extraction requires actual PC=24 startup arithmetic, which now matches the native
stages and composition. `CreatureComponentResults` adds 478,368 comparisons for
906 complete component constructions, including real SetLevel/CalcResults,
TP refill and required event delivery before the template overlay. Actor event
bodies remain supplied. Unsupported sets/level penalties, arts/effects, local/mounted
states and boss health remain guarded. Native-equivalent private scalar buffers
do not prove complete actor construction, attachment, publication or host readiness.
`test-host-construction.ps1 -Native -Installation` now regenerates startup
dependencies by default; `-IncludeLoot` explicitly adds the paused loot laboratory.

`CreatureConstructor` adds 1,867,588 comparisons for 922 complete original ordinary
outer constructors, including actual base/physical/component initialization,
SetLevel/CalcResults/health, both canonical level-event handlers and CRT RNG.
All 453 ordinary templates have two backing patterns; sixteen additional cases
cover clock/scratch/RNG boundaries. Pre-overlay intermediates are compared only
for zero backing; later stages/final initialized state must agree for both.
Native pointers/untouched storage, lazy graphics initialization, nonzero palette
events and later factory/model/publication work are excluded. `EquipmentBindings`
adds 142,848 original Gold/pureHD slot-kind and initial target-bone comparisons.
Both extend `NativeReplay`; neither supplies completed model bindings or a host oracle.

The 28 October `UnloadedModels` continuation compares 768 actual reset/attachment
bodies, 256 initial activations with duplicates and saved node headers, 114 ignored
equipment notifications, and two original unplaced creature streams/physical
receives. An isolated x86 helper calls the installed original Granny DLL with zero
handles: error 6 leaves the output untouched. Under base flag 2 the real physical
receiver discards that vector; zero-canonicalized construction matches the original
zero-stack stream. Its independent `0x16F` example participates in the shared
contract/envelope/atomic/TCP checks. Model loading is independent of node activation;
placed vectors, loaded models, future node ticks and complete factory/startup
execution remain separate. No Engine host oracle is granted by this evidence.
The suite contains 204,127 native-result comparisons.

`CreatureInitialFactory` compares 48 complete original unpublished factories and
their script placement/pose/assigned level/relation/behavior state, plus 512 extra
assigned level calls and the actual first opcode's private preparation. Shared
reservations/counter, registry lifetime, RNG and incomplete-opcode guards are
covered. `EquipmentInitialFactory` adds 18,432 comparisons against 128 complete
original weapon/shield factories, including item-specific node timing and shared
activation order. `StartupEquipmentGeneration` compares 195 complete named native
caller generations in both PC=24/64 modes, including consecutive startup RNG.
`StartupEquipmentCombat` compares 3,867 prefixes per precision.
`StartupEquipmentInstallation` compares individual and consecutive hand-item
installation, original signed damage ordering and measured zero-handle DLL behavior.
`StartupEquippedResults` compares 24 full final caller calculations, 324 actual
hand-query contexts and 2,048 armor factors independently matched on x86 hardware.
`StartupCreatureSerialization` adds 414 comparisons for complete native caller
streams, publication order and 128 placed receive/position cache variants, with a
non-creature read control. Its three independent 0x16F/0x16E catalog examples
exercise shared TCP/envelope/atomic contracts. `ModelMotionAvailability` adds
18,906 comparisons for original metadata availability and actual ranged caller
query order. Native map lookup, loose-file access and renderer/terrain boundaries
are stated in each fixture. Production now completes seven ordinary NPC commands,
retaining 15 private messages after 51 commands; the named caller at +0x6BC blocks.
`CreaturePlacementSelection` covers fresh sector initialization, KEYX
versus runtime flags and 536 actual branch selections. `StartupExperience`
retains all 65,535 x86 hardware outputs: arguments 1..100 compare numerically,
larger inputs remain explicit guards for unresolved legacy CRT rounding.
`ScriptGroups` also covers startup publication suppression and sixty original
CreateNPC group stages. These checks do not authorize placed NPC upload or host
finish. Named/special callers, bank/global provenance and complete campaign upload
remain required; prepared objects stay private.

`EquipmentNormalization`, `EquipmentPricing` and `EquipmentSlotSelection` add
21,811 assertions against 7,939 further original-instruction cases. Pricing checks
compare all item mutations as well as returned price; slot checks stop at dispatch
into the substituted installation service. They do not establish actual attachment,
ownership or full NPC construction. The same host-construction runner includes
their native generators and installed executable-definition checks. See the
[equipment continuation](../docs/game-server/host-equipment-values.md).

The item-drop selection suites (`EquipmentModifierLookup`, `EquipmentSelectionPool`,
`EquipmentSelection`, `EquipmentResourceNames`) add 12,881 assertions: installed
modifier association, static symbolic names, original pool membership and prepared
candidate/offline draw order and RNG. Shared initialization/name-class mutation,
live-hero retries and complete generation remain separate. The host-construction
runner regenerates these native fixtures with `-IncludeLoot`. See the
[simulator continuation](../docs/game-server/host-item-drop-simulator.md).

`EquipmentClassInference`, `EquipmentVariants` and `EquipmentFactory` add 37,981
assertions for original symbolic class rules, Rust.bin/canonical-source variants,
complete ordinary quality-zero values, persistent shared-state hashes and fresh
replay/random/no-result requests. `test-item-drop-simulator.ps1` separately runs
AssetViewer console commands and compares four displayed value blocks/RNG states
with native evidence, saving UI and direct-render preview screenshots without
activation. Nonzero magic, object naming, special item paths and enemy drop rates
remain explicit continuation work.

`ConsumerDeliveryTests`, `FxConsumerTests` and `NativeReplay/ConsumerPresentationChecks`
cover ordered independent consumers, repeats/dependencies, visible overflow faults,
session/lifetime cleanup, shared text slots, mode-16 notices, native drag clearing,
FX reloads and replacement particle flags. The native fixture adds 341 comparisons.
Five wire cases promote HP/damage/attached-FX/notification consumers into the shared
contract/TCP checks. Use `docs/game-server/tools/test-consumer-presentation.ps1`
for retained per-opcode reports plus the full suite. Render/native user acceptance
is separate; see [consumer delivery](../docs/game-server/consumer-presentation.md).

Seraphim visual replication adds literal `183` ground/target/fallback casts and
`16F` HolyLight/HolyWall states to the shared contracts. Portable TCP/render-state
checks cover timed release, native caster fallback, fixed ground placement,
Items.pak radius propagation, simultaneous ground effects, refresh, expiry,
cancellation and reference reuse. The native replay suite compares against
`native-seraphim-visual-replication.json`. The Engine screenshot replay is separate;
see [status effects](../docs/game-server/status-effects.md).

Ten additional session/browser receives (`001`, `008`, `01F`, `020`, `02A`,
`02C`, `030`, `037`, `039`, `047`) have shared wire contracts, 72 original
instruction cases and TCP/state checks including consumer-independent teardown
and malformed terminal batch rejection. See [session/browser](../docs/game-server/session-browser.md).

Ten quest/object receives (`15D`, `170`, `18D`, `1B8`–`1BE`) add 60 original
instruction cases and 1,877 native state comparisons, plus nested-length,
receipt-time function resolution, unknown-baseline/padding and TCP state checks.
Model/trigger execution and full quest gameplay remain separate. See
[quest deltas](../docs/game-server/quest-deltas.md).

Ten counter/message and special FX receives (`19E`, `19F`, `1A0`–`1A6`, `1A9`)
add 118 original-instruction cases and 1,152 native-result comparisons. Portable
checks cover deferred state, function snapshots, message localization, FX field
preservation, late KEYX resolution, lifecycle and TCP mutation/atomic rejection.
Inventory/callback execution and special minigame playback remain separate. See
[counter and special FX receives](../docs/game-server/counters-and-special-fx.md).

Five script/creature/region receives (`110`, `13D`, `178`, `17E`, `1D2`) add
162 original-instruction cases and 912 native-result comparisons. Permanent
checks cover receipt-time functions, slot-zero quirks, native flags/route reset,
file-category marker operands, stale object lifetimes, countdown latches and
fragmented TCP atomic rejection. Script, marker and type replacement execution
remain separate consumers. See [receive actions](../docs/game-server/receive-actions.md).

Ten creature/link/dialog commands (`114`, `125`, `12E`, `12F`, `13C`, `15E`,
`160`, `177`, `180`, `1CD`) add 217 original-instruction cases and 1,348 native
comparisons. Wire/TCP checks cover command ownership/atomicity; state checks cover
link ordering/masks, mount guard differences, hit notifications, dialog callbacks,
late baselines, resource selection and stale lifetimes. Full command/model/
particle and registered-script execution remain separate consumers. See
[creature commands](../docs/game-server/creature-commands.md).

Ten buddy/creature/region receives (`033`, `036`, `143`, `145`, `14A`, `14D`,
`14E`, `17D`, `189`, `18B`) add 190 original-instruction cases and 722 native
comparisons. Tests cover buddy names/alerts, conditional strings/localization,
quest-pair append and snapshot/scroll preservation, FX operand conversions,
retirement/local protection, stale instances, region generation/order/bounds and
fragmented TCP atomicity. Full gameplay/UI/model/FX consumers remain separate.
See [creature interactions](../docs/game-server/creature-interactions.md).

Ten session/lobby/profile receives (`002`, `009`, `00C`, `01C`, `024`, `026`,
`029`, `02B`, `150`, `151`) add 387 original-instruction cases and 1,593 native
comparisons. Permanent checks cover teardown without a game consumer, malformed
terminal batches, roster/team/name lifetimes, service latches/results, settings
context and x87 conversion, inner raw/zlib/checksum bounds, legacy normalization
and optional player-profile patches. UI, outgoing follow-ups and full profile
merge/application remain separate. See [lobby lifecycle](../docs/game-server/lobby-lifecycle.md).
