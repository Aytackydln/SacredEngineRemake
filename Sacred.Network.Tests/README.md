# Packet tests

13 November [post-startup campaign callback](../docs/game-server/host-startup-campaign-callback.md):
`PostStartupCallback` adds **10,768 assertions** for original caller gates, name
selection and actor arguments; `ScriptQuestObjectCompass` adds **3,169** for
missing-journal deferral, preserving all five scratch words and journal bytes.
The fresh null-actor continuation completes nine commands; non-null execution
and existing-journal refresh stay guarded. **1,660 full / 4,851 baseline installed /
4,857 continuation checks** and Engine/terminal builds pass. No new wire producer
or full host/Engine acceptance is claimed.

13 November [equipment-pool lifecycle](../docs/game-server/host-startup-equipment-pool-lifecycle.md):
`EquipmentPoolLifecycle` adds **11,467 assertions** against 16 original clear/load/
file-mode StartCode/reload cases, including intermediate state and file-only pool
255. The 252 native append controls also pass. **1,658 full tests / 4,849 installed
checks** and Engine/terminal builds pass; installed RNG/references are unchanged.
No new wire producer/format is added. Next: post-startup campaign callback and
region initialization before complete host upload/readiness.

13 November [regional replacement block](../docs/game-server/host-startup-region-replacement-bank.md):
`RegionReplacementBank 0x01B1` adds **144 native-result assertions** for original
reset/wea load, mutation parser/helper, opaque-byte preservation and bank writers,
including the adjacent 256-row region tail. A host-block/native-reader-envelope
example extends shared packet/TCP coverage. **1,657 full / 537 focused 0x1B1 /
4,336 installed checks** and Engine/terminal builds pass. Final region flags,
equipment-pool initialization order and complete host upload remain unfinished.

13 November [deferred-state ownership and replay](../docs/game-server/host-startup-deferred-state-replay.md):
`DeferredObjectReplay` adds **4,700 assertions** over original lookup, UInt16 state
override, swap removal, private save and independent bank-2 callback source.
`TriggerGapProvenance` adds **252 assertions**, retaining incomplete gap References.
**1,651 full tests / 4,331 installed checks** and Engine/terminal builds pass.
Target effects, full banks/readiness and Engine/live acceptance remain unfinished.

13 November [normalized dialog section](../docs/game-server/host-startup-dialog-normalization.md): 128 rows now produce an explicit
**11,264-byte bank-3 section-5 body**, normalizing only 74 proven nonsemantic
caller bytes per row. Raw output remains guarded. **108 constructor / 24 lookup /
four authored-batch controls; 11,805 assertions**. **1,649 full / 529 focused
0x1B3 / 4,331 installed checks** and Engine/terminal builds pass. Complete banks,
readiness and Engine/live acceptance remain unfinished.

13 November [established global values](../docs/game-server/host-startup-global-convergence.md): all **150 final values** and the
original **600-byte interpreter save block** now compare. Complete NPC evidence
closes 670; three unresolved destinations remain in strict replay, while final
values provably converge. **192 NPC/variable + 1,050 alternative sequences /
30,876 assertions**, **1,643 full tests / 4,326 installed checks** and Engine/terminal
builds pass. Next: 128 incomplete dialog rows. Full banks/readiness and Engine/live
acceptance remain unfinished.

13 November [mouseover terminator provenance](../docs/game-server/host-startup-mouseover-scratch.md): RNr resolves using a
known terminating first byte; numeric-zero prefixes remain insufficient.
**Four destinations remain guarded** (48 of 52 closed). **432 native sequences /
66,528 assertions**, **1,641 full tests / 4,324 installed checks** and Engine/terminal
builds pass. Real loaded terrain, complete banks, readiness and host-driven
Engine acceptance remain unfinished.

13 November [SetTrigger/TriggerPatch scratch](../docs/game-server/host-startup-trigger-scratch.md): two more installed writes
resolve through full native construction/name registration and bounded patch
preservation; **five remain guarded** (47 of 52 closed). **192 native sequences /
30,528 assertions**, **1,639 full tests / 4,324 installed checks** and Engine/terminal
builds pass. Mouseover padding still requires provenance; full banks, readiness
and Engine/live acceptance remain unfinished.

13 November [registration and OnMoveOver scratch](../docs/game-server/host-startup-registration-scratch.md): native capacity tracking
and bounded name clearing resolve seven more destinations; **seven remain guarded**
(45 of 52 closed). Growth stays pointer-dependent. **162 native sequences + four
constructor cases / 24,952 assertions**, **1,638 full tests / 4,324 installed checks**
and Engine/terminal builds pass. Full banks, initial sectors, readiness and
Engine/live acceptance remain unfinished.

13 November [conservative caller history](../docs/game-server/host-startup-caller-history.md): **38 of 52** short-name global
indices now resolve from compared consecutive variable calls, bit clears and
literal DefPos overlap. **14 remain guarded**. **68 native sequences / 520 callers /
10,676 assertions**, **1,636 full tests / 4,324 installed checks** and Engine/terminal
builds pass. Optional managed command tracing includes predicates and call paths;
complete native startup, banks, readiness and Engine/live acceptance remain open.

13 November [variable caller scratch](../docs/game-server/host-startup-variable-scratch.md): **32 native dispatcher sequences /
320 variable callers / 5,024 assertions** prove history-dependent short-name
indices. Added per-occurrence name-storage application and rejection guards.
**1,633 full tests / 4,324 installed checks** and Engine/terminal builds pass.
The **52 installed destinations remain unresolved** pending complete native
caller history; full banks, upload/readiness and Engine/live acceptance stay open.

12 November [initial globals](../host-startup-initial-globals.md):
closed bank 1's **128-byte parameter block** with original reset, authored caller
and writer parity; all **32 words** are initialized. The separate 150-word global
baseline and initial regional clear compare in **40 native cases / 383,604 assertions**.
The **52 short-name global-write indices remain guarded**. **1,632 full / 517 focused
bank / 4,324 installed tests** and Engine/terminal builds pass (existing NU1903
warnings). Startup remains complete at **16,457 commands / 466 NPCs**; full
banks/globals, initial sectors and host entry/readiness remain unfinished.

12 November [SetRG and bank sources](../host-startup-set-rg-and-bank-sources.md):
selected NetScriptCamp startup bytecode **completes 16,457 commands / 466 NPCs**.
SetRG/UnSetRG and their private table compare **282 native cases / 290 calls /
1,418 assertions**. Four keys/eight records serialize to a **288-byte region
section matching the original bank writer**; a generated 0x1C5 example extends
wire/TCP tests. **1,626 full / 521 focused region / 4,321 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). Full banks/globals,
initial sectors, upload/readiness and Engine/live acceptance remain unfinished;
resume with original constructor/reset provenance and the documented source gaps.

12 November [Armor hand lifecycle](../host-startup-armor-hand-lifecycle.md):
implemented true-predicate Weapon detachment, result recalculation and ordered
0x11D publication; native does not restore the hand. Wings/Helmet also complete.
**72 complete native callers + 128 factories / 19,464 assertions**; a generated
removal extends packet/TCP tests. Startup advances to **16,454 commands / 466 NPCs**,
stopped at **StartCode +0x8D2D7, opcode 37 (SetRG)**. **1,620 full tests / 575 item /
610 creature / 515 removal focused / 4,319 installed checks** and Engine/terminal
builds pass (existing NU1903 warnings). Complete bank/readiness and Engine/live
acceptance remain open. Native SetRG producer/storage is the next dependency.

12 November [Weapon hand-refresh dependency](../host-startup-hand-refresh.md):
implemented original usage/resource extraction and exact predicate; **37,779 native
results / 37,785 assertions**, including all installed profiles and changed
instruction constants. **1,613 full tests**, **573 item / 608 creature focused**,
**4,318 installed checks** and Engine/terminal builds pass (existing NU1903 warnings).
The complete true-predicate detach/restore command remains guarded at
**StartCode +0x8BD9A / 16,311 commands / 454 NPCs**. The supplied-actor diagnostic
reaches detachment and 0x11D but its placement/result graph still needs validation;
no full caller or host-driven Engine acceptance is claimed.

7 November [elevated effects and creature values/health](../docs/game-server/host-startup-elevated-effects-and-health.md):
192 full native elevated BUBBLEPRISON callers, nine update/render controls, six
floor-aware readers, 300 SetCV and 192 SetHP callers add **452,066 assertions**
in four suites. A host-produced elevated 0x16F example extends packet/TCP tests.
Startup reaches **16,311 commands / 454 NPCs**, stopped at **StartCode +0x8BD9A**,
Armor EquipNPC needing native true-predicate hand refresh for held Weapon usage 4.
Exact actor/hand inputs are retained for the next comparison. **1,612 full /
607 creature-TCP / 4,318 installed checks**, native generators and Engine/terminal
builds pass (existing NU1903 warnings). Complete banks/readiness, local GUI
presentation and host-driven Engine/live acceptance remain open.

7 November [compass, dialog reassignment and OnSee continuation](../docs/game-server/host-startup-compass-and-dialog.md):
literal QuestKompassPos, dialog-only SetNPCState, ordered OnSee callback bytes,
multiplayer InfoPlayer(5) and absent-hero POPUP compare against original execution.
Five permanent suites add **431,050 assertions**; new host-produced journal and
0x11A examples extend packet/TCP contracts. Tutorial OnEnter and its outer
TriggerQuest now complete. Startup reaches **15,994 commands / 436 NPCs**, stopped
at **StartCode +0x88B3A**: elevated BUBBLEPRISON, posWillbur **2870/3182/surface 1**.
Its native placement/projection remains guarded; exact resume inputs are retained.
**1,603 full / 523 journal-TCP / 598 creature-TCP / 4,315 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). One local compass GUI
submission is retained; presentation delivery, complete banks/readiness and
host-driven Engine/live acceptance remain open.

6 November [quest entry, journal and Tutorial callback](../docs/game-server/host-startup-quest-entry.md):
TriggerQuest's actual empty-function return and journal/OnEnter path now compare
with native execution, along with absent-hero predicates and QuestBook updates.
Core distinguishes **triggered flag 4** from **solved flag 2**. Three suites add
**7,155 assertions** and two generated journal examples extend common wire/TCP
checks. Startup reaches **15,793 commands / 395 NPCs**, guarding
**FunkCode +0x1A06A4**, QuestKompassPos in QIS_OnEnter1. The partial journal and
flags remain private, with no completed outer TriggerQuest credit.
**1,588 full network/host / 513 journal-TCP / 4,312 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). A scratch compass probe
and exact pending effects are recorded for continuation; complete banks/readiness
and Engine/live acceptance remain open.

6 November [BUBBLEPRISON, Armor chain and late startup](../docs/game-server/host-startup-bubble-and-armor.md):
96 complete native FX commands, update/render/reader controls, the five-piece
Shoulder/Chest/Foot/Arm/Belt chain, DoNotClean behavior and multiplayer
StartPosition now complete. Eleven permanent suites add **82,696 assertions**;
six generated examples extend wire/TCP coverage. Startup reaches **15,788 commands /
395 NPCs**, stopping at **StartCode +0x87243**, opcode 20 `TriggerQuest(1)`.
Its Tutorial callback 11564 has zero byte length; native return/completion logic
still needs comparison. **1,575 full network/host / 590 creature-TCP / 560 item-TCP /
4,310 installed checks** and Engine/terminal builds pass (existing NU1903 warnings).
Supplied actor snapshots, split-interpreter generation and native service boundaries
are documented. Complete banks/readiness and Engine/live acceptance remain open.

6 November [elevated placement, pending object states and sector zero](../docs/game-server/host-startup-elevated-and-state.md):
10 native Container reloads, 18 complete elevated blocker NPC callers, 244
unresolved SetObjState sequences and six complete no-equipment sector-zero
callers add **26,402 assertions**. Two generated host examples extend creature
wire/TCP checks. Startup now reaches **14,265 commands / 382 NPCs**, guarding
named **FX_BUBBLEPRISON** at **StartCode +0x75C4F**. **1,534 full network/host /
569 focused creature-TCP / 4,303 installed checks** and Engine/terminal builds
pass (existing NU1903 warnings). Pending-state replay/short-name scratch and
explicit sector-zero level inputs remain open; native bounds and constructor/
lookup substitutions are documented. Complete banks/readiness and Engine/live
acceptance are not claimed.


6 November [Armor command and ground-object reload](../docs/game-server/host-startup-ground-equipment.md#armor-command-and-ground-object-reload-delivery-6-november):
Helmet/ChestArmor EquipItem completes after full caller/hand/publication comparison
with explicit split-interpreter generation. Sixteen native mixed QuestItem/
Container sector cases support ordered ground chains. The two permanent suites
add **140 assertions**, and a generated Armor example adds common wire/TCP checks.
Startup reaches **14,038 commands / 271 NPCs**, stopping at **StartCode +0x737C4**,
elevated Container 1801/type 5204/surface 2 in sector 2664. **1,520 full network /
525 item-TCP / 4,300 installed checks** and Engine/terminal builds pass (existing
NU1903 warnings). Supplied host actor input is not independent initialization
proof; complete banks/readiness and Engine/live acceptance remain unfinished.


6 November [Armor constructor/installation continuation](../docs/game-server/host-startup-ground-equipment.md#armor-constructor-and-installation-continuation-6-november):
128 native Helmet/ChestArmor factories and three target-empty installations add
**18,532 assertions** in two permanent replay suites. Generic construction now
admits these categories. Full EquipItem remains guarded at **StartCode +0x72CA5**;
pre-install publication/current-hand refresh and the full actor graph still need
comparison. **1,513 full network/host tests**, Engine/terminal builds and the two
native generators pass (existing NU1903 warning). The provisional scratch caller
suffix is unfinished and excluded from evidence/acceptance.


6 November [ground equipment and EquipItem generation](../docs/game-server/host-startup-ground-equipment.md): 72 complete
native Weapon/Shield commands and 144 actual cache consumers support production;
144 EquipItem generation cases and 1,004 reverse-level controls compare the next
helper without admitting its full caller. Startup reaches **13,977 commands /
256 NPCs**, then guards humanoid Helmet EquipItem at **StartCode +0x72CA5**.
**1,511 full network / 516 item / 551 creature / 4,299 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). Complete banks, readiness
and Engine/live acceptance remain unfinished.

6 November [named MAGICPRISON producer](../docs/game-server/host-startup-magic-prison-production.md): executable-derived initialization,
336 native association routes, 36 complete named commands and 27 update/full-render
controls now support production. Startup reaches **13,924 commands / 240 NPCs**,
then guards ordinary Weapon CreateObj at **StartCode +0x72310**. **1,499 full
network / 504 item / 549 creature / 4,298 installed checks** and Engine/terminal
builds pass (existing NU1903 warnings). Complete banks, readiness, deferred queue
execution and Engine/live acceptance remain unfinished.

5 November [MAGICPRISON foundations](../docs/game-server/host-startup-magic-prison.md): 20 native name-parser controls,
72 native factory/unnamed snapshots and nine actual update/full-render comparisons
now have permanent coverage. Both operand readers preserve the secondary name;
network decoding retains native size-only particle framing and caller backing.
**1,491 full network / 501 item / 541 creature / 4,297 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). Named association/host
production remain guarded; startup stays at **13,158 commands / 152 NPCs**.

5 November [owner-provided editor adaptation](../docs/game-server/host-armalion-script-reference.md):
`ScriptDisassemblerTests` covers readable host failures, Gold trigger-scope width,
escaped/symbolic operands and raw suffix preservation. The installation check
traverses 40 original code files / 2,203,256 instructions. All 1,483 tests pass;
this is inspection coverage, with no additional host startup command credited.

5 November [random-variable continuation](../docs/game-server/host-startup-random-variables.md): opcode 105 and variable equality
now compare **1,452 native commands, 420 predicates and three combined routes**.
Startup reaches **13,158 commands / 152 NPCs**, stopping at **FunkCode +0x2CDD1,
QIS_OnSetUp9512**, two-name FX_MAGICPRISON creation. **1,476 full network /
491 item / 526 creature / 4,297 installed checks** and Engine/terminal builds pass
(existing NU1903 warnings). Complete banks, host readiness and Engine acceptance
remain unfinished.

5 November [literal-1000 NPC continuation](../docs/game-server/host-startup-special-level.md): special health factors now
integrate at the native stage, with **64 complete NPC commands** and **256 actual
class queries** compared. Startup reaches **13,142 commands / 140 NPCs**, stopping
at **FunkCode +0x2CA6A, opcode 105, QIS_OnSetUp9512** (random variable).
**1,474 full network / 489 item / 524 creature / 4,295 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). State remains private and
invalidated; complete host readiness and Engine acceptance remain unfinished.

5 November [special-health dependency](../docs/game-server/host-startup-health-factors.md): original factor selection and
startup arithmetic now compare **576 native controls**. Balance fields and
executable multiplier definitions are decoded. Full health/level-1000 integration
remains pending; startup stays at **13,095 commands / 109 NPCs**. **1,468 full
network / 488 item / 518 creature / 4,293 installed checks** and Engine/terminal
builds pass (existing NU1903 warnings). Host readiness remains unfinished.

5 November [Book/resource/flag-state continuation](../docs/game-server/host-startup-books.md): native-compared Book
construction, local alias reservation and flag-only SetNPCState now complete.
Startup reaches **13,095 commands / 109 NPCs**, stopping at **FunkCode +0x2BFBD,
QIS_OnSetUp9511**, requested level 1000 and special health factors. **1,467 full
network / 487 item / 517 creature / 4,293 installed checks** and Engine/terminal
builds pass (existing NU1903 warnings). Partial state remains private/invalidated;
complete banks, host readiness and Engine acceptance remain unfinished.

5 November [NPC callback/AI continuation](../docs/game-server/host-startup-npc-ai.md): ordered
callback registration and initial no-hero AI now compare 432 initializer controls,
16 append controls and 12 complete NPC commands. Startup reaches **12,987 commands /
102 NPCs**, stopping at **named Book construction, QIS_OnSetUp10254, FunkCode
+0x2869B**. **1,459 network / 514 focused / 4,291 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). Full host readiness and
Engine acceptance remain unfinished.

5 November [quest continuation](../docs/game-server/host-startup-quests.md): original quest table
loading, activation order/reentry and quest-number function gates now compare
native behavior (955 assertions). Startup reaches **12,959 commands / 99 NPCs**,
stopping in **QIS_OnSetUp10251 at FunkCode +0x27016**, CreateNPC callback tag 0x05;
behavior-bit-1 AI is also pending. **1,452 network / 4,289 installed checks** and
Engine/terminal builds pass (existing NU1903 warnings). Partial quest state stays
invalidated/private; full host readiness and Engine acceptance remain unfinished.

5 November [merchant continuation](../docs/game-server/host-startup-merchants.md): combined
trader/smith/combo services and native unowned implicit-NPC admission now compare
96 service cases and 30 complete commands. Startup reaches **12,947 commands /
96 NPCs**, stopping at **quest activation, opcode 21, StartCode +0x678DF**.
**1,451 network / 506 focused / 4,288 installed checks** pass. Engine/terminal
builds pass with existing NU1903 warnings; full host readiness remains unfinished.

5 November [healer continuation](../docs/game-server/host-startup-healer.md): CreateNPC now reuses
shared ECS decoding and binds explicit dialog slots/attachments. Native comparisons
cover 125 bounded cases and six complete healer commands. Startup reaches **12,891
commands / 93 NPCs**, stopping at **trader mode 4, StartCode +0x66DED**. All **1,444
network tests**, **499 focused 0x16F tests** and **4,287 installed checks** pass.
Engine/terminal builds pass with existing NU1903 warnings. Complete host upload
and Engine acceptance remain unfinished.

4 November [dialog/pool continuation](../docs/game-server/host-startup-dialog-associations.md): opcode 106
associations and opcode 114 equipment pools now compare 540 / 252 native cases.
Startup reaches **12,888 commands**, stopping at **CreateNPC, StartCode +0x66D5A**,
operand **0x67 (`ECS_HEALING`)**. All **1,437 network tests** and **4,286 installed
checks** pass; Engine/terminal builds pass with existing NU1903 warnings. New
association scratch bytes remain unknown and serialization is guarded. Complete
banks/upload/readiness and Engine acceptance remain unfinished.

4 November [hideout continuation](../docs/game-server/host-startup-hideouts.md): native-compared opcode 140 now
consumes pool definitions and produces ordered mouseovers with shared CRT RNG.
Startup reaches **11,685 commands**, stopping at **opcode 106, StartCode +0x60CDA**
(dialog associations). All **1,435 network tests**, **460 focused 0x1D0 checks** and
**4,283 installed checks** pass. Builds pass with existing NU1903 package warnings;
complete banks/upload/readiness and Engine acceptance remain unfinished.

4 November [sector retention continuation](../docs/game-server/host-startup-sector-retention.md): native cache eviction,
compressed unload/reload with linked actors and stored tile-patch replay now pass.
Startup reaches **5,444 commands / 232 variables**, stopping at **opcode 140,
StartCode +0x280B2** (hideout/pool mouseover production). All **1,429 network tests**
and **4,282 installed checks** pass. Engine/terminal builds pass with existing
NU1903 package-audit warnings. Full banks/upload/readiness remain unfinished.

4 November [mouseover/terrain continuation](../docs/game-server/host-startup-mouseovers.md): opcode 94 production and
ordinary opcode 68 variable bits are native-compared. Dormant equipped ground
reload and unlinked IndoorExit placement also compare. Startup reaches **3,480
commands / 201 variables**, stopping at **StartCode +0x19A06** when the 96-sector
tile cache requires native eviction. Elevated/retired Horse reload also compares. All **1,426 network tests**, **450 focused
0x1D0 checks** and **4,282 installed checks** pass; Engine/terminal builds pass with
existing NU1903 package-audit warnings. Full upload/readiness and Engine acceptance remain unfinished.

1 November [grave-variable continuation](../docs/game-server/host-startup-grave-variables.md): native-compared literal 0x6E,
`+Var(...)` destination names and opcode 111 function restart now complete the
194-iteration grave loop. Installed startup reaches **2,534 commands / 197
variables**, stopping at **StartCode.bin +0xD419, opcode 94 mouseover registration**
(the former CreateObj label was incorrect). All **1,418 network tests** and
**4,280 installed checks** pass; Engine/terminal builds are clean. Full upload,
readiness and host-driven Engine acceptance remain unfinished.

30 October [Horse dependency continuation](../docs/game-server/host-startup-horse-continuation.md): loaded indoor creation,
level-only state changes and implicit unnamed Horses now compare with complete
native commands. Startup reaches **1,755 commands, 92 NPCs and 290 private messages**,
stopping in `grabsteindialogvars` at FunkCode +0x828D (condition tag 0x6E). The next
closure also needs native +Var name expansion and opcode 111 loop control. All
1,417 network tests and 4,279 installed checks pass; Engine/terminal builds pass with
existing NU1903 package-audit warnings. Full host upload/readiness and Engine acceptance remain unfinished.


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
