# Packet tests

15 November [text update-open and CRT error mapping](../docs/game-server/host-startup-settings-update.md):
**41,124 native update lifetimes /164,496 assertions; 5,160 mapper calls /10,320**.
Exact seek/read/shrink/reset/failure behavior and executable-derived error tables
compare. Win32 SetEndOfFile binding passes six test rows/60 workspace cycles.
**1,938 full /788 focused0x11 /793 focused0x1B tests**, clean builds, installed readers
and guarded startup pass. Next: caller-specific errno/doserrno effects and shared
FILE/descriptor/startup ownership before actual host-setup composition.
Installed settings remain untouched; real0x154 and registered actor guards remain.


15 November [native fopen/lazy buffering and Windows binding](../docs/game-server/host-startup-settings-open.md):
**SettingsOpenModes: 8,085 / 16,170 assertions; SettingsOpen: 1,440 / 4,320;
SettingsFileBuffer: 24 allocation profiles +16 open/I/O/close lifetimes / 176**.
Original parser/table allocation and Win32 request policy compare. Lazy native
buffering and file-derived initial CRT values feed a concrete Win32 settings port;
two real workspace fixture cases cover normal/fallback allocation and reload.
**1,930 full / 780 focused0x11 / 785 focused0x1B tests**, clean builds, installed
definitions and guarded startup pass. Read-write text preparation and shared/current
CRT/error-state integration remain separate. Installed settings are not modified.

15 November [settings save/CRT text I/O and stream port](../docs/game-server/host-startup-settings-io.md):
**SettingsSave: 1,872 / 5,616 assertions**; **SettingsCrtRead: 8,574 lifetimes /
54,667 calls / 218,668 assertions**; **SettingsCrtWrite: 2,960 / 11,840**;
**SettingsBufferedWrite: 81 lifetimes / 486 calls / 1,944**; **SettingsFileRoundtrip:
45 / 180**. Actual CRT translation/formatting/buffering and native fclose run at
explicit Win32/established-descriptor boundaries. A concrete stream port compares
physical calls and lossy reload. **1,925 full / 775 focused0x11 / 780 focused0x1B
tests**, clean builds, installed save data and guarded startup pass. Next: original
fopen/_fsopen allocation and concrete path/classification/OS error binding.

15 November [settings loader and duplicate-preserving storage](../docs/game-server/host-startup-settings-loader.md):
**SettingsLoader: 4,932 original calls / 14,796 assertions**; **SettingsStorage:
2,496 original operations / 2,998 assertions** with actual tree operations and
CRT signed formatting. File loading retains duplicates; read/write/remove use
only the first equal entry. Six complete loader/tree/getter/conversion/importer
compositions expand **SessionSettingsImport to444 / 3,114**. Installed pureHD
operands decode without settings I/O. **1,920 full / 770 focused0x11 / 775
focused0x1B tests**, clean builds and guarded startup pass. Next: original CRT
text/OS file behavior and saver0x662AD0 before concrete file/Engine bindings.

15 November [actual string getters and ANSI conversion](../docs/game-server/host-startup-session-strings.md):
**SessionStringSettings: 27,930 complete calls / 55,860 assertions** over seven
variants; **SessionAnsiConversion: 3,780 original calls/bounded-overread profiles /
15,120 assertions**. Whole-importer evidence expands to **438 / 3,066**, including
99 profiles enabling real reached string/integer getters and16 also executing
both conversion wrappers. Seven supported installed pureHD definitions decode.
**1,918 full / 768 focused0x11 / 773 focused0x1B tests**, clean builds and guarded
terminal exit3 pass. Windows/code-page/allocation and settings-map bodies remain
explicit boundaries. Next: actual loader0x662710 (caller0x817D6A), logical read/
write/remove bodies and Settings.cfg format/storage before concrete Engine bindings.

15 November [complete settings importer and integer getters](../docs/game-server/host-startup-session-settings.md):
**SessionSettingsImport: 339 original calls / 2,373 assertions**; **SessionIntegerSettings:
5,625 calls / 11,250 assertions** across five variants. Eighteen complete imports
execute all four real numeric getters, including lookup/default callback mutations.
Independent key strlen/end-base/start operands are mapped; expanded **ListenPort:
10,855 assertions** includes two additional pointer variants. Installed pureHD
transient/mask/lobby-port/speed data decode; its different importer has no ASE branch.
**1,916 full / 766 focused0x11 / 771 focused0x1B tests**, clean builds and guarded
terminal exit3 pass. Actual string getters/conversion, Settings.cfg file loading
and concrete platform/resource/registered actor inputs remain separate boundaries.

15 November [listen-port and selected-address settings](../docs/game-server/host-startup-listen-port.md):
**ListenPort: 7,855 assertions** over 3,750 getter/parser calls, 234 IPv4 parses and
121 key comparisons; **NetworkAddressSelection: 576 / 192 profiles**. Five
executable variants and installed definitions prove key/default/class data follows
the files. Roster record+0 now maps selected IPv4 with compatibility aliases.
Twelve settings/controller/open cases extend coverage. **1,914 full / 764 focused
0x11 / 769 focused 0x1B tests**, clean builds and guarded terminal exit3 pass.
Remaining importer branches, Settings.cfg file loading, actual locale/services and
registered actor inputs stay separate from logical map/parser/service completion.

15 November [connection open/close and lobby registration](../docs/game-server/host-startup-connection-platform.md):
**ConnectionOpen: 156 native profiles / 780 assertions; ConnectionClose: 96 / 480;
LobbyRegistration: 224 / 224**, including seven executable-data variants.
Core operand mappings/definitions and an Assets PE reader retain ANSI bytes and
signed operands. The installed reader decodes eight values without registry writes.
Twelve open/close compositions and two null-transport guards extend coverage.
**1,900 full / 750 focused 0x11 / 755 focused 0x1B tests**, clean Engine/terminal
builds and expected terminal exit3 pass. Existing wire cases are reused; no new
packet or real platform adapter is introduced. Next: actual settings importer/
listen-port getter and concrete SDK/resource/presentation inputs before actor readiness.

15 November [network menu configuration provenance](../docs/game-server/host-startup-network-menu-mode.md): **6,030 menu
profiles / 30,150 assertions**, plus **252 complete window-bit operations / 504**,
compare entry/update/visibility, conversion, selector and page configuration with
reentrant services. Ten initializer-composition/current-capture cases retain raw
connection results and unknown/missing state. **1,883 full / 733 focused 0x11 /
738 focused 0x1B tests**, clean builds and expected guarded terminal exit3 pass.
Local menu functions reuse existing wire cases. Actual credential/configuration,
backend-open and presentation/resource bindings precede actor/Engine acceptance.

15 November [live accepted staging](../docs/game-server/host-startup-staging-live.md): **64 complete native branches /
512 assertions** compare logging, identifier and alternate-identity mutations,
submitted strings/table and final state. Existing-receipt staging now invokes
the actual live roster implementation and outer continuation. Twenty-five owner
cases cover lifetime/failure/reentry, and a flags9 response extends shared wire/
TCP checks. **1,871 full / 721 focused 0x11 / 726 focused 0x1B tests**, clean
Engine/terminal builds and expected guarded terminal exit 3 pass. Next: actual
configuration/service provenance before registered actor binding or Engine oracle.

15 November [live roster consumer](../docs/game-server/host-startup-roster-live.md):
**RosterLive: 6,528 assertions / 1,088 complete original consumers** compare
current names/identifiers, bandwidth/profile gates and refresh/notice mutations.
Actual derived UI getters establish family 5 / subtype 0x10 independently of
header +4 command 0x38, checked on each UI callback. Forty-one nested-owner cases
cover lifetime, partial/swallowed failure,
reentry and profile argument ownership. A native 0x1B profile/bandwidth example
extends shared contracts/TCP checks. **1,840 full / 690 focused 0x11 / 700 focused
0x1B tests**, clean builds and expected terminal exit 3 pass. Accepted prefix/
staging, actual service/setup bindings and actor readiness remain separate.

15 November [live outer admission continuation](../docs/game-server/host-startup-admission-live.md):
**AdmissionLive: 55,726 assertions / 3,808 complete original branches** compare
callback field mutations and service order through the actual roster consumer.
Thirty-five receipt cases cover reset/replacement, partial failure, reentry and
current connection replacement/absence. A flags=6 wire example extends shared
contracts/TCP tests. **1,793 full / 648 focused 0x11 / 653 focused 0x1B tests**,
clean Engine/terminal builds and expected guarded terminal exit 3 pass. Accepted
prefix/staging and live per-row roster delivery remain explicit services; the
fixture's eager roster projections are not an Engine oracle or actor readiness.

14 November [complete local controller 0x117](../docs/game-server/host-startup-controller117.md):
**LocalController117: 21,905 assertions / 2,077 complete original profiles** compose
actual dispatcher, connection clear, process polling, backend pump/command and
immediate UI. Supplied OS/backend/storage/listener boundaries remain explicit.
Four receipt cases cover actual post-service capture, reset/replacement reentry
and partial failure. A zero-window 0x11 example extends the shared catalog/TCP
checks; response +2 is mapped as a hosted-window token. Local 0x117 is not a wire
example or producer. **1,752 full / 607 focused 0x11 / 617 focused 0x1B / 4,852
installed / 4,858 continuation checks**, clean builds and expected terminal exit 3
pass. Live admission timing, actual service/setup inputs and actor binding remain
separate from the prepared projection and compared local service bodies.

14 November [controller setup and immediate services](../docs/game-server/host-startup-controller-services.md):
**AdmissionControllerSetup: 936 assertions / 48 full constructors / 108 initializers**;
**AdmissionResetService: 608 / 144 lifetimes / four full constructors**;
**ImmediateMessageDelivery: 47,630 / 9,072 full calls / four actual UI constructors**.
These native field/ownership/routing checks preserve supplied service boundaries.
**AdmissionComplete: 19,544 / 1,408 branches** now captures per-receipt state and
executes reset/UI adapters with supplied factories/listeners. Ten additional host
cases cover capture/multiplayer separation and reset/UI completion, failures and
reentry. Local event 0x117 is not added as a wire packet; existing packet examples
are reused. **1,742 full / 597 focused 0x11 / 612 focused 0x1B / 4,852 installed /
4,858 continuation checks**, clean builds and expected terminal exit 3 pass.
Local controller dispatch, live continuation timing, remaining services and actual
actor binding are separate unfinished dependencies; no runtime acceptance is claimed.

14 November [executable-derived roster identifiers](../docs/game-server/host-startup-roster-identifier.md):
**RosterIdentifier: 3,813 assertions / 3,675 complete native calls / 32 complete
roster consumers** compare raw UTF16, ordered passes/gates, altered operands,
installed definition extraction and actual consumer composition. Existing full
admission profiles use the configured file-derived provider; two additional host
contracts cover missing rules and configuration lifetime (25 session cases total).
A formatted native 0x1B example extends shared packet/TCP coverage.
**1,729 full / 584 focused 0x11 / 599 focused 0x1B / 4,852 installed / 4,858
continuation checks**, clean builds and expected terminal exit 3 pass.
Controller setup and actual service/actor binding remain pending; no producer or
wire format changes. Executable-derived conversion does not establish delivery.

14 November [admission session ownership and backend transfer](../docs/game-server/host-startup-admission-session.md):
**AdmissionComplete: 18,136 assertions / 1,408 native branches** now pass through
the existing gameplay session and compare native outer effect order.
**ConnectionBackendPump: 6,001 / 800 controls**, half composed with actual native
finalization, compare captured extents, linked insertion and backend replacement.
**23 host lifetime/delivery cases** cover ownership, canonical input, stale and
duplicate receipts, deferral/rejection and identifier/finalizer reentry. These
host contracts are separate from native service implementation/delivery proof.
**1,721 full / 581 focused 0x11 / 591 focused 0x1B / 4,851 installed / 4,857
continuation checks**, clean builds and expected terminal exit 3 pass.
Existing PacketCatalog wire examples are reused; no format/producer is added.
Controller setup, actual service adapters and real actor binding remain pending.

14 November [complete admission and roster consumer](../docs/game-server/host-startup-admission-plan.md):
**RosterConsumer: 5,248 assertions / 192 original calls**;
**AdmissionComplete: 15,320 / 1,408 full branches** with actual roster/reset;
**AdmissionFinalizer: 228 / 32 calls**. Raw consumer gates are also observed.
Presence +6 differs from character type; positive 0x1B wire coverage retains
the valid shorter player record. **1,697 full / 567 focused 0x1B / 557 focused
0x11 / 4,851 installed / 4,857 continuation checks**, clean Engine/terminal builds
and expected terminal exit 3 pass. Supplied service delivery, session lifetime
and real actor binding remain pending; no wire format/producer is added.

14 November [admission roster staging](../docs/game-server/host-startup-admission-roster.md):
**AdmissionRoster: 3,360 assertions / 560 original stages** compare complete table
hashes, assigned raw fields, UTF16 length rounding/override overlap and flags.
String assignment, identifier/backend ID and roster consumer are supplied services.
A native 0x1B table example extends shared contracts/TCP, retaining valid 834-byte
player-data payloads. **559 focused / 1,689 full / 4,851 installed / 4,857
continuation checks** and clean Engine/terminal builds pass. Complete admission
and real actor binding remain pending; no wire format/producer is changed.

14 November [admission controller](../docs/game-server/host-startup-admission-controller.md):
**AdmissionController: 2,690 assertions / 384 original prefixes** compare actual
0x11 dispatch and controller/query/field writes, with reset/UI/string/send services
explicit. A native 0x11 wire example extends shared contracts/atomic dispatch/
envelope/TCP coverage. **553 focused / 1,683 full / 4,851 installed / 4,857
continuation checks** and clean Engine/terminal builds pass. Accepted continuation
and real actor binding remain pending; no packet format/producer is changed.

14 November [reference maintenance/return](../docs/game-server/host-startup-reference-maintenance.md):
**ObjectReferenceMaintenance: 24,192 assertions / 3,456 native calls** and
**ObjectReferenceReturn: 3,456 assertions / 864 calls**, all with actual cleanup.
Coverage includes strict unsigned time boundaries/wrap, cancellation/swap order,
occupied slots, zero and reason gates. Documented storage, clock identity and
diagnostic boundaries remain explicit. **1,677 full / 4,851 installed / 4,857
continuation checks** and clean Engine/terminal builds pass. Grant ownership/
actual actor binding remain separate; no packet format or producer is changed.

14 November [reference cleanup](../docs/game-server/host-startup-reference-cleanup.md):
**ObjectReferenceCleanup: 16,956 assertions / 2,592 native controls**, including
repair, swap removal, duplicate diagnostics, outside-logical storage and 32
negative-extent guard controls. Twelve composed actual selection/cleanup calls
raise **ObjectReferenceSelection to 12,648 assertions**. Storage/diagnostic and
other documented service boundaries remain explicit. **1,675 full / 4,851
installed / 4,857 continuation checks** and clean Engine/terminal builds pass.
Timed return/accounting and real binding remain unproved; no wire change is added.

14 November [reference selection](../docs/game-server/host-startup-reference-selection.md):
**ObjectReferenceSelection: 12,552 assertions** against **1,152 original 0x600AA0
controls**, including offline 256-slot refill, multiplayer queue-only behavior,
inventory/chest gates, destruction/reentry, output byte, delayed pairs and native
retained-rejected-reference exhaustion. Storage, membership and cleanup remain
supplied boundaries. **1,674 full / 4,851 installed / 4,857 continuation checks**
and clean Engine/terminal builds pass. Incoming grants are unchanged; cleanup/
return and real binding remain unproved. No wire format/producer is added.

14 November [pending-free cleanup](../docs/game-server/host-startup-pending-free-cleanup.md):
**RegistryPendingFree: 2,880 assertions** against **432 original insertion/deque
controls**, covering actual ordered erase/iterators, duplicates/missing references,
block crossings and constructor/destructor source changes. Block release and
construction/destruction/activation remain explicit boundaries. Existing insertion
suite retains **5,549 assertions**. **1,673 full / 4,851 installed / 4,857
continuation checks** and clean Engine/terminal builds pass. Reference selection/
return and real binding remain unproved; no new wire producer/format is added.

14 November [registry insertion](../docs/game-server/host-startup-registry-insertion.md):
**RegistryInsertion: 5,549 assertions** against **576 original explicit-reference
allocations**, covering actual logical growth within capacity, occupied slots,
failure, constructor/destructor reentry and counter/activation order. Construction,
destruction and activation are explicit boundaries; ordinary activation retains
existing independent coverage. Nonempty pending-free deque cleanup and storage
reallocation remain guarded/unproved. **1,672 full / 4,851 installed / 4,857
continuation checks** and clean Engine/terminal builds pass. Real binding and
non-null campaign remain unfinished; no wire format/producer is added.

14 November [registry removal](../docs/game-server/host-startup-registry-removal.md):
**RegistryRemoval: 1,176 assertions**, from **240 native slot destructions / 144
local-reference protection gates**, compare slot clearing before destructor,
logical/capacity boundaries, reentry replacement and protection outcomes.
Destructor, stable singleton/connection state and diagnostic sinks are supplied
boundaries. **1,671 full / 4,851 installed / 4,857 continuation checks** and clean
Engine/terminal builds pass. Full lifecycle/binding and non-null campaign remain
unfinished. No new wire format or producer is claimed.

14 November [active-hero lookup](../docs/game-server/host-startup-active-hero-lookup.md):
**ActiveHeroLookup: 17,136 assertions** against **3,360 original lookup controls**,
including reference repair before failed casts, logical bounds and diagnostic
slot replacement. Actual 0x603E30 now executes in the automatic-level and complete
NPC generators. Singleton, RTTI and real actor lifetime remain explicit inputs.
**1,670 full / 4,851 installed / 4,857 continuation checks** and clean Engine/
terminal builds pass. No packet producer/format or non-null campaign acceptance
is added. Next: registry/controller lifecycle and real current snapshot binding.

14 November [active-hero automatic requested levels](../docs/game-server/host-startup-automatic-level.md):
**2,552 original callers / 32 data-constant variants / 6 statistics getters**
produce **7,551 AutomaticCreatureLevel assertions**. The combined NPC suite has
**129,564 assertions**, including 48 complete active-hero host comparisons and
four required higher-XP guards from 52 native controls. A copied primary counter
at **+0x2668** extends shared **0x14F** contract/ownership/dispatch/envelope/TCP
coverage. **1,669 full / 539 focused 0x14F / 4,851 installed / 4,857 continuation
checks**, clean Engine/terminal builds and expected terminal exit 3 pass.
These tests supply current hero/team/sector snapshots; actual registry/class/
lifetime binding, equipped active-hero campaign NPCs and non-null startup remain
unfinished. No wire producer/format or complete Engine acceptance is claimed.

14 November [associated campaign handlers](../docs/game-server/host-startup-associated-handlers.md):
**192 native named-dialog / 1,344 activation / 224 VM controls** extend permanent
caller/quest/trigger coverage. Combined suites: dialog **591,616**, associated
activation **4,038**, scope **38**, trigger **32,448**, NPC **83,860 assertions**.
NPC getter observations identify the active-hero automatic-level dependency;
present/unknown hero profiles remain guarded. **1,663 full / 4,851 baseline
installed / 4,857 continuation checks**, Engine/terminal builds and expected
terminal startup exit 3 pass. No wire producer change or complete non-null
campaign/Engine acceptance is claimed. Resume at the documented hero/team/sector
level caller, not the completed named-dialog/activation comparisons.

14 November [associated creature scope and NPC construction](../docs/game-server/host-startup-associated-creature.md):
`StartupNpcSeeCommand` now includes **24 complete native associated-caller NPCs**,
**68 quest/ownership gate controls** and **17 operand parses**, totaling **83,858
assertions** in the combined suite. `AssociatedCreatureCaller` adds **15** identity,
nested native-argument, lifetime and guard assertions. Six source tags decode;
ownership/quest runtime modes and unverified associated handlers remain guarded.
**1,662 full / 4,851 baseline installed / 4,857 continuation checks** and both builds
pass. Non-null MultiCampaign and Engine/live acceptance remain unfinished.

14 November [function quest scope and actor forwarding](../docs/game-server/host-startup-function-scope.md):
`ScriptFunctionScope` compares **363 assertions** for native quest scope and
restart definition/gate reload, including its saved-quest quirk. Both compass
suites cover omitted quests in established scope (**4,225 object / 7,349 position
assertions**). Actor forwarding is independently recorded through actual VM and
CreateNPC entry; factory bodies are excluded and non-null continuation stays
guarded. **1,661 full / 4,851 baseline installed / 4,857 fresh-continuation checks**
and Engine/terminal builds pass. No wire producer or full Engine acceptance was
added. Logs/TRX remain in TestResults/post-startup-*.

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


## Further historical coverage notes

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
