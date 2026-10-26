# Packet tests

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
runner regenerates these native fixtures. See the
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
