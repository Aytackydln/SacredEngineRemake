# Plan: a server that simulates the Sacred world (authoritative mode, stage 2+)

Status (2026-11-17, branch `feature/gs-sim`): rounds 1 and 2 implemented, see "Progress" at the end. Plan written on `feature/headless-game-server`.

## 1. Goal

In Closed Net with `--authoritative`, the C# GameServer decides the outcomes that matter for a hero's progress: what drops, what
a merchant sells and for how much, what crafting and socketing produce, which quest rewards are paid, how much damage is dealt
and which values (experience, level, points, gold) a hero has. A modified client can still render, move and click as it likes,
but it cannot create value that the server did not create or witness.

Hard constraints (from the project and this repo):

* **Stock clients only.** No patched Sacred.exe and no required Sacred Gear. Everything has to work with what a stock Gold
  client already sends and accepts.
* **Headless Linux, Native AOT + full trimming**, one binary. No reflection-driven serializers in new code, no native libraries.
* **No needless allocations in the game loop.** The current rule (`LoopbackTests.PacketLoopDoesNotAllocate`, < 2 KB over
  250 ticks) extends to every new tick path. Tables are loaded at startup into flat arrays; per-tick state is structs in
  preallocated arrays.
* **Guest on the repo.** New code lives in its own area (see section 7) and touches existing files only through small, named
  hooks. Never merged to master. Relay mode (no `--authoritative`) stays byte-for-byte like GameServer.exe.

## 2. Where we are

| Area | What exists today | Where |
|---|---|---|
| Object world | A mirror: the latest serialized 0x16E/0x16F state per object, streamed to players lacking it. Field updates (0x165 position, 0x128 HP, values) are not applied | `Session/ObjectMirror.cs`, `GameServerHost.World.cs` |
| Sector ownership | Exactly like the original: the first client entering a sector gets 0x020 and simulates its creatures; the server hands it over on leave | `GameServerHost.Sectors.cs` (FUN_004cf0d0) |
| Validation | 0x17A attacker ownership + global damage clamp (`AUTH_MAX_DAMAGE`), impossible item states (`ItemGuard`), hero level cap (`HeroGuard`), locked difficulty bits, trade sender check | `Auth/*`, `GameServerHost.Authoritative.cs` |
| Hero custody | The server keeps each hero's state (0x16B full block, compact values, 0x11D/0x11E, 0x122, 0x13E/0x13F, quest pairs 0x145) and writes the hero to sacred-lobby on leave, periodically and after changes | `Auth/Hero/*`, `GameServerHost.HeroState.cs` |
| Item tokens | One-time tokens for mod-created items, `--auth-strict-items=off|log|on` refusing client-created items without a token or baseline match. **Opt-in** because loot, merchants, crafting, quests and trades are not tracked | branches `mod/shared-stash`, `mod/auction` (commit 933430c, `GameServerHost.ItemTokens.cs`) |
| Server-made items | `server.drop_item` (port of the drop code, checked against native replays) and `server.drop_gold` | `Scripting/LootService.cs`, `Sacred.GameplayHost/Equipment/WorldEquipmentDropSimulator.cs` |

Reusable logic that already exists (and was checked against the original):

* **This repo's shared projects** (upstream code): `Sacred.Core/Loot/*` (creature loot chance, attempts, rarity, sets, uniques,
  money, potions, runes), `Sacred.Core/GameBin/Balance/*` (creature and creature-level balance), `Sacred.GameplayHost`
  (`WorldEquipmentDropSimulator`, `WorldCreatureLootDecisionSession`, `WorldParticipantSectorExecutor`, ambient population,
  creature constructor, world script executor and quest journal), `Sacred.Network/Gameplay/*` (codecs for creature state,
  movement, combat, container activation 0x18C, quests 0x1B8..0x1CA, script variables, statistics), with native replay
  checks in `Sacred.Network.Tests/NativeReplay`. These allocate freely (records, lists, LINQ), so they are good for startup
  and for reference, not for the tick.
* **John's C++ conversion (`sacred-decomp`)**, 481/482 gates verified: combat (`getHitVal` 0x568D70, the four-tier MSVC6
  `_rand` damage roll), spells (all executor and init bodies), quests and the script VM (`scriptinterpret`, `scriptworkloop`,
  GATE-0026, GATE-0064), merchant/trade/chest/blacksmith state (GATE-0028: buy order, `0x57FD80` gold clamp, forge/upgrade
  `0x6A4380`), hero saves (GATE-0031), movement and pathfinding (GATE-0018). It has no networking and no creature AI gate of
  its own, so the owner side of the net protocol is still RE work.
* **Static decompiles** of Sacred.exe (`B:\DevDrive\sources\decomp-sacred`) and GameServer.exe
  (`B:\DevDrive\sources\decomp-gameserver`).

## 3. The one architectural fact everything depends on

In Sacred's multiplayer, **the client that owns a sector is the world for that sector**: it spawns the creatures, runs their AI,
rolls their loot with its local `_rand`, and publishes everything as object states. The GameServer is a relay plus a little
bookkeeping. So there are only two ways to make the server authoritative without patching clients:

* **Witness (verify):** clients keep simulating; the server keeps a *provenance ledger* for every item, every gold change and every
  experience gain, and refuses what it cannot explain from things it saw. Cheap, works with ownership as it is, but can only
  bound outcomes (a client can still pick its own luck inside the legal range, e.g. reroll loot by never reporting bad drops).
* **Owner (simulate):** the server keeps sector ownership itself (no client ever gets 0x020) and runs the sector: spawns,
  AI, combat resolution, loot with the server's RNG. Clients only own their own hero. This is the end goal, and it is a large RE
  project because the server must speak the *owner's* side of the protocol.

The plan does **Witness first, per subsystem, then Owner per subsystem**, so every milestone ships something usable, and the
Witness validators stay as the safety net in Owner mode (an Owner-mode sector still has heroes that act on the client).

## 4. Milestones

Each milestone ends with tests in `Sacred.GameServer.Tests` (fake clients and replayed captures), a real-client check where it says
so, and an update of `docs/protocol.md` with what was learned. "Exit" is the condition to call it done.

### M0. Evidence first: capture, replay and the provenance log (prerequisite for all)

* A capture recorder in the server (`--capture=<dir>`, off by default): every decoded message per session with tick and slot, in
  a compact binary file, written on a worker thread from a pooled ring buffer. Real sessions against stock clients become
  regression inputs.
* A replay test harness that feeds a capture through `GameServerHost` with a fake clock and asserts on ledger decisions.
* `--auth-strict-items=log` gets a structured "unexplained creation" log (opcode window around the event: last 0x17D, 0x18C,
  0x198, 0x13E/0x13F, 0x167, merchant/smith dialog traffic) instead of the current single line.
* Scripted real-client test sessions (in Closed Net with sacred-lobby, loopback only): kill creatures, open chests, buy, sell,
  socket, use the blacksmith, finish two quests, trade between two clients, drop and pick up, split stacks.

Exit: captures for each scenario above, and the log names the opcode sequence of every item-creating path. **This answers most of
the RE questions in section 6 cheaply, before any simulation code is written.**

### M1. Item provenance ledger (Witness) — unlocks `--auth-strict-items=on`

A per-object provenance record in the server (`Kill`, `Container`, `Merchant`, `Craft`, `Quest`, `Trade`, `Baseline`, `Token`,
`ServerDrop`, `Split`) assigned when an item object first appears. Each path gets a validator:

| Path | Accept when (Witness) | Source of the rule |
|---|---|---|
| Creature loot | a 0x17D kill of a creature the server saw, within N s and M world units; item level, category, rarity tier and count within what `Sacred.Core/Loot` allows for that creature's type, level and the game difficulty; at most the creature's attempt count | `SacredCreatureLootAttempts`, `SacredCreatureLootGeneration`, `SacredLootRarity` |
| Chest / container | a 0x18C activation of a container the server saw, once per container per reset | `GoldContainerActivationCodec`, chest flow in GATE-0028 |
| Merchant buy | hero gold drops by exactly the price formula of that item; a trader NPC (service flag 0x2000) within range; item legal for the merchant's level band | GATE-0028 buy order, `0x57FD80` |
| Craft / socket / smith | input items destroyed (0x167) in the same short window; output equals the inputs combined (records = item + socketed records) | blacksmith `0x6A4380`/`0x6A3080`, item record layout in protocol.md |
| Quest reward | quest state change (0x1B8..0x1BE progress/journal) for this hero in the window; item matches the quest's authored reward | `SacredScriptQuestReader`, quest codecs |
| Trade | completed 0x198 exchange (both locked in, server's offer list): the item moves, provenance carries over | `GameServerHost.Trade.cs` |
| Split / stack | same type and records as an existing stack whose count shrank | to be confirmed in M0 |

Unknown paths stay in `log` mode individually (`AUTH_STRICT_ITEMS_PATHS=loot,container,...`), so the gate can become **on by
default** path by path. A refused item is never stored, relayed or saved (existing behaviour of the gate).

Exit: a full M0 scenario run shows zero unexplained creations with every path on; `--auth-strict-items` defaults to `on` in
Closed Net `--authoritative`.

### M2. Gold ledger (Witness)

Every change of hero gold must be explained: pick-up of a known gold pile (its price), merchant sell (sell formula) and buy (price),
trade gold (0x198 sub 0x27, completed), quest reward, smith costs, server `drop_gold`. Unexplained increases are clamped back to the
server's figure on the next save and count as violations. Depends on M1 (merchant/smith detection) and on knowing how the hero's
gold reaches the server (open question Q7).

Exit: gold in saved heroes equals the server's ledger in every M0 scenario.

### M3. Hero values (Witness)

* Experience ledger from kills credited by the server (formula from the decomp: XP of a creature by level difference and party
  share), level from the experience table, skill and attribute points per level, skill levels within points spent (plus item
  bonuses), combat art ranks only up through consumed tomes, equipment requirements met.
* The hero file is already written from the server's view (`HeroWriter`); M3 replaces client-reported numbers with server-derived
  ones where they disagree, and violations escalate.

Exit: an edited hero value (memory-edited XP, attribute points) does not survive a save; a legit hero round-trips unchanged.

### M4. A real server object world (prerequisite for M5 onwards; roadmap item 1)

* Replace the serialized mirror with real objects: struct arrays keyed by reference (position, sector, HP, values block, owner,
  inventory membership), decoders that apply 0x165, 0x128, 0x16B/0x16C compact values, equipment and inventory messages in place.
* Serialize object states from current state instead of replaying uploads; world banks from state.
* Exact forwarding filters for modes 3..8 (sector, visibility, party), which also cuts traffic.
* AOT/alloc: no per-message objects; a struct pool with free lists; spans over pooled buffers.

Exit: the mirror's tests pass on the new world; positions in `server.hero`/`player.x/y` are current; mode 3..8 traffic matches a
GameServer.exe capture.

### M5. Combat validation from server stats (Witness, needs M3 + M4)

* Port `getHitVal` (sacred-decomp `combat.cpp`, 0x568D70) and the defence side into a pure, allocation free C# module; compute each
  hero's damage range from the server's view of equipment, skills and buffs. 0x17A damage above that hero's maximum (plus a margin
  for buffs the server does not model yet) is clamped per hero instead of the global `AUTH_MAX_DAMAGE`.
* Attack rate bounds per weapon speed; HP (0x128) plausibility against max HP and regeneration.
* A kill 0x17D is credited (XP, loot provenance) only when accepted damage plausibly brought the creature down.

Exit: a damage-edited client is clamped to its legal maximum; no false positives in M0 captures across all hero classes.

### M6. Server-owned sectors (Owner) — the big step

The server keeps 0x020 to itself and becomes the simulating owner. Staged so that each sub-stage is useful and can be switched per
sector (`AUTH_OWNED_SECTORS=all|list|none`, default none until proven):

* **M6a. Ownership switch only.** The server withholds 0x020 for a few test sectors (nobody simulates them) and observes how a
  stock client behaves in a sector nobody owns: rendering, its own spawning, interactions, what it sends. Pure RE milestone (Q1,
  Q2) with a kill switch; every other sector keeps client ownership as today.
* **M6b. Containers and static objects.** The server owns chests, barrels, doors, shrines: contents rolled by the server
  (`Sacred.Core` loot code driven by `SacredRandomSequence` seeded by the server), objects with server references from the
  server's reference pool.
* **M6c. Creatures without fighting.** Spawn the sector population (`WorldParticipantSectorExecutor`, ambient population, creature
  and level balance, the server's difficulty), stream them as 0x16F, idle and patrol movement (0x165), NPCs with their dialogs.
* **M6d. Fighting creatures.** AI think loop (`GameplayCreatureThinkClock` as the clock reference), targeting, pathfinding (sacred-
  decomp GATE-0018), attacks sent as 0x17A to the hero's client, creature HP applied from hero 0x17A (computed by M5 rules, not the
  client's number), deaths 0x17D issued by the server, loot rolled by the server and created as server objects (provenance
  `ServerDrop`, so the strict gate needs no kill heuristics there).
* **M6e. Spells and projectiles of creatures** (sacred-decomp spell engine, `GameplayBowProjectiles`).

Budget: 16 heroes, the sectors around them active, 20 ms tick, no GC in steady state. Per-sector fallback to client ownership when a
sector uses something the server cannot simulate yet (logged), so M6 can grow region by region (start with the starting area).

Exit per sub-stage: two stock clients play the starting region with server-owned sectors, nothing visible breaks, captures replay
deterministically.

### M7. Merchants and the blacksmith (Witness in M1/M2, Owner here)

* If merchant stock is generated locally by each client (likely, Q4), the Witness rules of M1/M2 are the ceiling: the server can
  check price and legality, not the roll. Owner mode needs the server to own trader NPCs (M6c) and, if the stock reaches clients
  over the net at all, to supply it; otherwise the server can at least make the roll's seed or the stock deterministic per NPC and
  day so a client cannot reroll by reopening.
* Blacksmith forge/upgrade results computed server side (GATE-0028 `0x6A4380` path) and compared with what the client reports.

Exit: buying an item the merchant cannot have, or for less gold, is refused; smith results equal the server's.

### M8. Quests and the script VM on the server (Owner)

* Run the global.res interpreter on the server (sacred-decomp `scriptinterpret`/`scriptworkloop` as the reference; the remake's
  `WorldScriptExecutor` and quest journal as building blocks) as the campaign host, own the interpreter state opcodes
  0x1B8..0x1D5 and the script variables, and decide quest completion.
* Quest rewards (items, gold, XP) are created by the server (server objects or item tokens), so quest provenance in the strict gate
  becomes exact.
* Quest pairs 0x145 checked against quest state.

Exit: two clients finish the first quest chain with the server's interpreter; a client that sends a forged quest completion gets no
reward.

### M9. Defaults and hardening

Strict items, gold, hero values and damage on by default in Closed Net `--authoritative`; soak tests (hours, 16 bots from replayed
captures), allocation and tick-time gates in CI, documentation for server owners (what is enforced, what is logged).

## 5. Order and dependencies

```
M0 ──> M1 ──> M2 ──> M3 ──> M5
        │                    │
        └──> (strict items   M4 ──> M5 ──> M6a ──> M6b ──> M6c ──> M6d ──> M6e
              on by default)               │                 │
                                           └──> M7           └──> M8 ──> M9
```

Recommended sequence: **M0, M1, M2** (the strict-items unlock and the economy, highest value per effort), then **M4** (foundation),
**M3, M5**, then **M6a** as an early RE probe (it can run in parallel with M3/M5 because it is mostly captures), then M6b..M6e, M7,
M8, M9. M4 can start in parallel with M1/M2 since it is internal.

| Milestone | Value | Size | RE risk |
|---|---|---|---|
| M0 | enables everything | S | low |
| M1 | strict items default-on | M | medium (merchant, craft, quest paths) |
| M2 | no gold creation | S | medium (Q7) |
| M3 | no edited heroes | M | low-medium (XP formula) |
| M4 | real world, exact routing | L | low (codecs exist) |
| M5 | damage per hero | M | low (combat is converted) |
| M6 | server simulates | XL | **high** (owner protocol, AI) |
| M7 | merchants | M | medium-high (Q4) |
| M8 | quests | L | high (interpreter sync) |

## 6. Open RE questions

Most of these are answered fastest by M0 captures, then confirmed in the static decompiles.

1. **Q1 Nobody-owns-a-sector behaviour.** What does a stock client do in a sector without an owner (no 0x020 to anyone)? Does it
   still render and accept creatures streamed by the server, stop its own spawning, and does it ever claim ownership itself?
   (Client receive side of 0x020/0x01F; GameServer.exe FUN_004cf0d0.)
2. **Q2 The owner's protocol.** Which messages does an owner send for creature spawn, movement, AI state, attacks, death and loot
   (and in which order), and which does a non-owner send *to* the owner (hits on remote creatures, interactions)? The remake's
   `WorldParticipant*` and `GoldCreature*` codecs cover parts; the AI side (think state, targets) is not converted in sacred-decomp.
3. **Q3 RNG.** Loot and damage use MSVC6 `_rand` (0x849B40) with a client-global seed. Is the seed ever synchronized, or is every
   roll local? (If synchronized, Witness could replay rolls exactly instead of bounding them.)
4. **Q4 Merchant stock.** Where is a trader's stock generated (on dialog open, on NPC spawn, by the sector owner, per client)? Does
   any stock or price cross the net? What does a purchase look like on the wire (0x16F creation + 0x13F place + gold in values)?
5. **Q5 Crafting / socketing / smith.** Which opcodes carry socketing a rune/gem and the smith's upgrade; is the result a new object
   or a changed one (0x16A-style correction)?
6. **Q6 Quest rewards and the interpreter.** Which client runs global.res scripts in multiplayer (the host, each client, the sector
   owner)? How are 0x1B8..0x1D5 produced and applied, and where is a reward item created?
7. **Q7 Gold on the wire.** How and when does a hero's gold reach the server (compact values field, full 0x16B, only on saves)? The
   README already lists this as unverified for `drop_gold`.
8. **Q8 XP.** The XP formula per kill (level difference, party share, difficulty) and whether XP is in the compact value stream.
9. **Q9 Original GameServer.exe world.** Case 0x17D "applies native event 0x208" and 0x13F runs CHEATPROTECTION_ProcessSetItem:
   how much of a world did GameServer.exe keep (cItemBase objects, statistics)? Its cheat protection rules are a free first set of
   validators.
10. **Q10 Unverified items from the README.** Gear `inv.remove`/`inv.add` opcodes, that the owner can pick up server-created
    objects, the class selector for `drop_item`, rarity mapping of `drop`. M0 captures settle them.
11. **Q11 Stacks and splits.** How potions/runes stack and split on the wire (new reference or the same one).
12. **Q12 Hero death and resurrection** costs and what crosses the net (0x17D for heroes is still unverified).

## 7. Architecture and code layout

* **New code in its own area:** a new project `Sacred.GameServer.Simulation` (namespace `Sacred.GameServer.Simulation`), referenced
  by `Sacred.GameServer`, plus tests in `Sacred.GameServer.Tests/Simulation`. Subfolders: `Ledger/` (provenance, gold, XP), `Rules/`
  (pure formulas: loot bounds, prices, hit values, XP), `World/` (M4 object store), `Owner/` (M6 sector simulation), `Script/` (M8).
  Upstream projects (`Sacred.Core`, `Sacred.GameplayHost`, `Sacred.Network`) are referenced, never edited.
* **Hooks into the host** stay as few as now: one `IWorldAuthority` called from `AuthoritativeFilter` (message in, verdict out), one
  per-tick pump, one on remove-object, one on sector entry. Everything stays behind `--authoritative`.
* **Data:** game tables (balance, loot, items, weapons, creature types, merchants, quests) loaded once from `--game-dir` with the
  upstream readers, then copied into flat struct arrays owned by the simulation, so the tick never touches the allocating models.
* **Determinism:** a server RNG per sector (`SacredRandomSequence`-compatible) with the seed in captures, so replays are exact.
* **Formulas:** each `Rules/` function is a pure static function with a test that pins it to the C++ conversion's output (sacred-
  decomp tests as the oracle where it has them, native replay data where the upstream has it).

## 8. Risks

| Risk | Effect | Mitigation |
|---|---|---|
| Stock clients misbehave in server-owned sectors (Q1/Q2) | M6 impossible or only partial | M6a as an early probe; per-sector fallback to client ownership; Witness (M1..M5) is complete protection-light without M6 |
| False positives in Witness rules | legit players lose items or get kicked | `log` mode per path first, M0 captures as regression, escalation (refuse, then violation, then kick) configurable |
| Witness cannot stop luck manipulation (rerolling, not reporting bad drops) | cheaters still pick their best legal outcome | accepted until M6 (loot) and M7 (merchants); documented as a known limit |
| Scope: M6/M8 is effectively a second game engine | years, not months | strict ordering by value; each milestone useful alone; reuse the C++ conversion and the upstream ports instead of fresh RE |
| Allocation and tick budget with full simulation | GC pauses, lag | struct arrays and pools from the start, alloc tests per milestone, active-sector limit |
| Licence of upstream code | the upstream repo has no licence; John is a guest | reference upstream projects as the server already does, do not copy their code into new files; the C++ conversion is John's own and the preferred source for ported formulas. **Decision for John** (section 9) |
| Drift between the C++ conversion (Sacred.exe build) and the Gold client data | wrong formulas | pin the client build (data fingerprints are already checked at admission) and test against Gold data |
| Upstream changes in shared projects | merge conflicts | no edits upstream; the simulation depends on a small surface, adapters in our project |

## 9. Decisions for John

1. **Licence/reuse:** may new simulation code reference upstream projects (as `Sacred.GameplayHost` is referenced today), or should
   ported formulas come only from `sacred-decomp`? The plan assumes "reference yes, copy no".
2. **Kick policy** for Witness violations in Closed Net: refuse silently, count, or kick (the current default kicks at 25).
3. **Start of M6:** is a per-sector hybrid (some sectors server-owned, others client-owned) acceptable as a long-lived state, or only
   as a stepping stone?
4. **Live captures:** M0 needs scripted sessions with real stock clients on loopback (and against GameServer.exe for comparison).
   Same rules as the existing test networking (loopback only, LAN announce off).

## Progress

### Round 1 (2026-11-17, `feature/gs-sim` = `feature/headless-game-server` + `mod/auction` for the item tokens)

Code lives in `Sacred.GameServer/Simulation/` (a folder of the server project, not a separate project: the server project is ours already, and a
new project would need an entry in the upstream solution file) plus `Session/GameServerHost.Witness.cs` for the hooks.

* **M0, partly:** `--capture=<folder>` (`Simulation/Capture.cs`: writer on a worker thread, reader), and the per-player opcode trail in the log
  lines of refused items and gold. Still open: the replay harness that feeds a capture through the host, and the scripted real-client sessions.
* **M1, Witness core:** `Simulation/ItemWitness.cs` explains creations by trade, rebuild (destroyed items coming back, socketing), kill loot,
  container content and sector take-over; every source can be switched off and every window, range and budget is a key
  (`Simulation/WitnessOptions.cs`, README "Witness"). Unexplained items that a kill or container *after* them would explain are counted, which
  answers part of Q2 from real sessions. Not done: merchant, quest and blacksmith paths (need Q4..Q6), and `--auth-strict-items` stays off by default
  until real sessions show zero unexplained creations.
* **M2, gold ledger:** `Simulation/GoldLedger.cs`, `AUTH_STRICT_GOLD=off|log|on`; credits from picked-up gold piles and trade gold. Not done:
  merchant sales, quest gold, the hero's gold in the baseline block as the starting figure (the first reported state starts the ledger).
* **Strictness:** each check has `off|log|on` (items, gold), the source list, the numbers, and `AUTH_STRICT_VIOLATIONS` for whether refusals count
  towards the kick limit.

Next round: M0 replay harness and real-client captures (answers Q1..Q8 and calibrates the windows), then M4 (real object world), then M3/M5.

### Round 2 (2026-11-17)

* **M0:** captures are now at the TinCat frame level (logins, admission, keep-alives, disconnects: format `SGSCAP02`), and `--replay=<file>`
  (`Simulation/CaptureReplay.cs`) runs a capture through a real `GameServerHost` with a manual clock (`Simulation/HostClock.cs`, injected through
  the host's constructor) and in-memory links, then prints the witness report under the given settings. Test: a live session and its replay make
  the same decisions, and a stricter source list on the same capture refuses more. Still open: real-client captures (stock client on loopback,
  Closed Net with the local test lobby), which are John's runs or need the game on this machine.
* **M4, first step:** 0x165 position updates applied to the server's world (authoritative mode only). Next: HP 0x128, compact values 0x169/0x16C
  merged instead of replaced, inventory membership, exact forwarding filters for modes 3..8.

### Round 3 (2026-11-17, with real clients)

* Real-client captures (stock Gold driven by Sacred Gear automation, Closed Net with the test lobby): the auction thread's sessions and a
  53-minute session of my own. With the world-start, purchase, quest and stock sources of the shared stash thread they replay with 0
  unexplained creations.
* **What a single real client sends** (53 minutes, by count): 0x16F 3263, 0x16E 2019, 0x11A 404, 0x15C 282, 0x11B 85, 0x174 84, 0x1C2 84,
  0x14F 59, 0x168 46, 0x18F 45, 0x169 24, ... and **no 0x165 positions, no 0x128 health, no 0x17A/0x17D** (no fight happened). Positions
  reach the server as repeated full 0x16F states, which the mirror already stores. So M4's next pieces are 0x11A and 0x15C (what they
  change) and a session with fights and two clients; the 0x165/0x128 handling stays as it is until a capture shows them.
* Open: the play automation walks but has not landed a hit yet (the isometric click mapping is off), so kill/loot captures are still missing.
