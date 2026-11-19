# Packet tests

Permanent network contracts, native replay and production TCP tests live here.
Coverage includes decoding, lengths, ownership, atomic dispatch and envelopes;
passing these checks is separate from live original-server approval or rendering.
Use the [host checkpoint](../docs/game-server/host-development-plan.md#resume-checkpoint)
for active work. [Native replay delivery history](native-replay-delivery-history-2026-11-15.md)
retains earlier suites/counts; its research continuations are historical.

HostRestoreSource compares original vector construction, save marker/count/row
footprint, Engine-mode loader/reset selection and empty/positive restore-loop
branches. Installed HostSectorEntry checks bind the same explicit fresh-world
source to completed ambient work, then apply final patches on shared terrain and
the actual pending lists/active registry. Prerequisite, unrelated-operation,
retry, cancellation and incomplete-queue guards remain covered. NativeReplay's
existing StartupSectorPatches retains nonempty indoor/outdoor comparisons.
The latest original-server first-sector patch lists are empty; those native
controls must not be presented as nonempty live patching or world approval.
[Proof, latest live trace and next host tail](../docs/game-server/host-sector-restoration-and-patches.md).

HostActiveHeroBinding compares the original getter on the current entered graph,
including null controller and removed-slot controls. Installed TCP checks verify
the same factory actor/node, actual current values, model/pose, terrain, counter
and equipment dictionary, retry and invalidation. Live original-server execution
now reaches WaitingForStartupInputs with a bound local hero. Startup composition,
network actor registration, world approval and readiness remain open.
[Current proof and live boundary](../docs/game-server/host-active-hero-binding.md).

HostCurrentPublication compares original snapshot masking/value validation,
clock/UI defaults and hero/item/both-inventory writers. Installed TCP checks
compare all seventeen current messages against native bytes and verify exactly
one send, stale-value rejection before publication and lifetime/disposal guards.
Native PacketCatalog examples cover current 0x169/0x16E/0x122. The real original
server publication remains separate from StartCode, network actor registration,
approval and readiness.
[Current proof and live boundary](../docs/game-server/host-current-character-publication.md).

HostCharacterWorldEntry compares actual native saved-sector loading/reinsertion,
fresh-world flag, primary quest reset and campaign entry/membership. Installed TCP
checks compare both complete outdoor grids (including actual dynamic chains),
current actor/item lifetime, retry and disposal. Read the [current proof and live
boundary](../docs/game-server/host-character-world-entry.md). Active-hero
startup composition and approval/readiness remain separate. Earlier stages below
retain their original component scope.

HostHeroPhysicalImport compares 192 original physical reader/activation/direction/
animation-wrapper controls. HostHeroModel compares original body/motion resources,
24 installed-library bind/zero-animation bone samples and new-control defaults.
The persistent host now commits that physical stage to the same factory node and
owns a current shared initial pose. HostHeroValueImport compares 19 complete
original reader cases and integrates scalar/art normalization on that root.
HostSavedItems compares 14 original factories/file readers including price side
effects; installed TCP checks restore those real objects using already consumed
grants and the shared counter/activation queue. HostHeroPostLoad adds 17 C0
controls; HostItemPostLoad compares 42 current-item normalization controls;
HostEquipmentReferences compares three native reference orders and ten original
inventory-mode predicate inputs. Installed tests bind actual factory objects and
inventory members, with stable grants, atomic guards and disposal. Loaded model
attachment/equipped results, advancing animation and entry placement retain
their separate verification guards.
HostModelReactivation compares two original body reopen/unloaded-slot controls.
HostSavedLoaderTail compares five native final equipment/inventory/heading cases,
including dirty coordinates/caches/removal flags and successful original return.
Installed checks compare actual bind-pose bones, selector zero and old-control
retirement on the same factory lifetime. The saved-position world prelude, entry,
publication and live acceptance remain separate; loaded equipment binding is not
observed in this tail. [Exact boundaries](../docs/game-server/host-model-reactivation-and-loader-tail.md).
The installed TCP checks cover retry, no extra grants and disposal invalidation;
original-server evidence is in [C0/C2 stages](../docs/game-server/host-c2-equipment-inventory-binding.md).

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

Entered-hero startup coverage includes actual shared factory/terrain/counter/RNG
ownership, received roster player count, native fresh-world mode, bounded initial
automatic levels, Container/QuestItem/Weapon values and shared-node cleanup.
Native quest checkpoint/journal/OnEnter, symbolic IsHero and pending POPUP effects
now compare. Installed TCP completes StartCode, actual actor-bound MultiCampaign
and initial-sector reload/region work, checking pending AI/model retention and
idempotent region retry. These checks grant no world upload/approval/readiness.
[Current proof and live evidence](../docs/game-server/host-entered-startup-completion.md).

TriggerPatchBankSource compares native AVL flattening/count and the complete
bank-3 section-2 writer against the existing host packed-cell table, including
unsigned keys and a 2,453-pair profile. PacketCatalog reuses the actual producer
for a focused bank-section fixture. Other sections are fixture header zeros only;
production must establish all missing sources before building a full world bank.
The independent runtime trigger-vector gap remains guarded.

HostSectorInitialization compares the complete native Init selector/scope,
the actual first requested neighbor, and ordinary/startup trigger notifications.
The explicit normalized 0x1CE producer changes only unused coordinate triples;
native raw/zero/A5 receiver controls preserve full map/queue/tile state. PacketCatalog
includes both directions of 0x148 and the actual 0x1AE/0x1CE producers.

Installed TCP tests provide an explicit center-first server fixture to compare
the full center callback, then retain its real queue token as Executing. They
also test missing-instruction rejection, one interest send, unchanged retry
grants/effects/region temporary and incomplete queue/readiness. Original-server
acceptance uses its real neighbor-first order and is separately recorded in
[the current checkpoint](../docs/game-server/host-initial-sector-callback.md).

HostRegionEntry compares 48 original complete EnterRegion lifecycle profiles,
packed high/low selection, the original current-region sentinel and pending-body
retry without premature alive mutation. HostRegionEntryBody compares all variable
bytes and indexed 0x1C7 notifications across 108 complete original Region1Enter
bodies. Mixed predicates keep scalar 0x48 equality separate from 0x49 set-bit.
PacketCatalog reuses the actual indexed producer. Installed TCP checks completed
entry, already-alive retry and unchanged queue/grants/readiness. No callback or
replay result establishes full-bank approval or persistent hosting.

HostSectorEntry compares 48 whole native EnterSector selectors and 12 complete
Sector51038Enter bodies: all mutable environment bytes, the 160-byte spawn rule,
three facing draws and empty-list FillRegion timer/schedule behavior. Replay
reconstructs original executable fragments and Balance.bin threshold bytes from
evidence, without installed files. Installed TCP separately covers actual setting-1
FIRSTCONTACT propagation into the shared creature world, completed callback retry,
no actor/allocation/grant changes and the still-Executing instruction. The existing
center-first fixture retains its unsupported-body guard before RNG/effects.
[Latest live evidence and post-callback blocker](../docs/game-server/host-sector-enter-callback.md).

HostAmbientScan compares original shoreline scanning on the first requested
sector's actual KEYX/WLDX border, including interleaved constructor/orientation
draws. HostAmbientFactory compares 24 complete original positioned factories,
all results/health and wire bytes (only the previously proved unused bone cache
normalizes). Actual runtime 0x800 decides sector membership; owned actors remain
scheduled in both profiles. Installed TCP separately creates two real shared
factory actors under a declared test seed, checks exact submitted bytes and
unchanged retry. The live driver preserves actual RNG. Native/fixture/transport
completion remains separate from full-world approval and readiness.
[Current native proof, live evidence and next restore-source blocker](../docs/game-server/host-ambient-population.md).
