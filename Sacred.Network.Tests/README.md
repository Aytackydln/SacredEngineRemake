# Packet tests

Permanent network contracts, native replay and production TCP tests live here.
Coverage includes decoding, lengths, ownership, atomic dispatch and envelopes;
passing these checks is separate from live original-server approval or rendering.
Use the [host checkpoint](../docs/game-server/host-development-plan.md#resume-checkpoint)
for active work. [Native replay delivery history](native-replay-delivery-history-2026-11-15.md)
retains earlier suites/counts; its research continuations are historical.

HostHeroConstructor compares 124 original logical constructor controls, including
intermediate values/health and early physical-resource ordering. HostHeroFactory
covers 27 actual local-slot seed/factory controls: supported no-default paths and
atomic guards for additional initial-item bodies. HostHeroAnimationRequest compares
the selected physical reader's activation/animation arguments; model bodies are
excluded. Installed TCP checks cover root lifetime/retry, actual grant independence
and no premature network-world readiness. [Current proof](../docs/game-server/host-hero-factory.md).

HostStartTable NativeReplay compares 256 original constructor-load/selector profiles
(258 assertions); source data comes from original MultiStart.bin. Actor placement is
excluded. [Current proof](../docs/game-server/host-start-table.md).

HostCharacterInitializationTests uses the installed original selected PAX with
controlled TCP grants. It checks native pre-script actor guarding, atomic shared
reference consumption/replenishment, idempotence and no fabricated replica/approval.
HostInitializationSessionTests also covers admission-state/queued-event timing.
Original constructor/position/script-gate segments run separately through
`verify-host-character-entry.py`; full actor restoration is excluded.
[Current proof](../docs/game-server/host-character-entry.md).

RegionInitialization/RegionFunctionBindings compare original selection and packing;
RegionWrapper and RegionQuestActivation compare the reached body/call/hash rules.
StartupNpcSeeCommand also covers normal-region NPC construction and pre-service
0x11A publication. Native0x11A/0x1BA examples use shared packet/TCP contracts.
The source-free installed diagnostic checks both completed callbacks, no repeated effects,
changed quest bank bytes and no admission-readiness credit. Native quest source
replay covers all603 rows and preserves interpreter indices.
[Exact scope and final checks](../docs/game-server/host-region-initialization.md).

Current source producer: TriggerBankSource NativeReplay (48 original traversals/129
assertions), strict unknown-slot refusal and installed host source composition.
[Source boundaries](../docs/game-server/host-trigger-bank-source.md).

Current host integration checks: HostInitializationSessionTests (portable TCP
lifetimes), HostInitializationInstalledTests (original files + fixture grants), and
LoadedBlockedNpc native replay/ground NPC PacketCatalog coverage. See the
[delivery and exact boundaries](../docs/game-server/host-initialization-session.md).

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
Portable packet/replay tests have no scratch-project dependency.
HostInitializationInstalledTests uses original installed files (SACRED_GAME_DIRECTORY
or the local Gold installation) with controlled TCP grants. It is separate from
original-server acceptance and does not write installed data.


Coverage checks require a wire example for each concrete main GoldGameEvent record,
plus declared additional event cases; this is not exhaustive branch coverage.
An unknown packet selection fails rather than passing a zero-test run.

Native emulation, installed-file composition, rendering and original-server checks
are distinct. Reuse recorded evidence for regressions; rerun native/live checks
when the changed behavior needs them. Host construction's batched runner is
`docs/game-server/tools/test-host-construction.ps1`; see its parameters for native
and installed-data checks. Per-delivery counts remain in the archived history.
