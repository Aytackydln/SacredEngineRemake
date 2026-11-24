# Sacred.GameServer

Headless re-creation of Sacred's `GameServer.exe` (dedicated server) as a .NET 10 console app, intended to run on a cheap
Linux VPS. Native AOT + full trimming (`dotnet publish -c Release -r linux-x64` gives a ~2.3 MB single binary, no IL warnings).

New project only: no existing file of the repository is modified. It reuses the verified layouts/codecs of `Sacred.Network`
(FIRSTCONTACT layout, data fingerprints, build reader). The `Sacred.Network` *client* classes allocate per frame, so the wire
path here is separate and allocation free.

## Status (stage 4: session flow, routing, lobby, object world)

| Area | State |
| --- | --- |
| TinCat login / keep-alive / logoff | done, verified against `Sacred.Network` client |
| Gold envelope + messages (raw + zlib) | done, byte-compatible with `Sacred.Network.Protocol`; own zero-alloc inflate/deflate |
| FIRSTCONTACT admission | done: version check, lobby identity duplicate check, 25 data fingerprints with mismatch mask, slot 1..16, host role to first player, 0x11 reply `[flags][slot][token]` (flag 1 host, 4 host world available), 0x1E game flags |
| Rejection 0x3F reasons | 1 version, 2 duplicate identity (only while the lobby is connected, like `FUN_004eb0a0`), 3 refused/locked/full, 4 bad sender, 5 sequence, 6 checksum **or** data mismatch (from `FUN_004eb0a0`) |
| Game password (0x12 -> 0x13/0x14) | done; `MaxPasswordTries` (default 3) is **not** verified against the original |
| Chat 0x003 private / 0x005 public | done (`0x4CA8AA` / `0x4CA771`), public chat honours nothing but sender exclusion; team chat 0x004 **not yet** |
| Host commands 0x019/0x01A lock, 0x01E flags, 0x023 max players, 0x156/0x157 kick | done, host only (others get 0x009 value 0x64); flags merge `(new & 0xFFFE8F72) \| (cur & 0x1708D)` |
| World start 0x017/0x018, world banks 0x1B1..0x1B4 -> 0x1B5 | done as relay: host uploads are stored and served in the original order 0x1B2, 0x1B4, 0x1B3, 0x1B1 |
| Time probes 0x041/0x043/0x044/0x045, pause 0x134/0x135 | done |
| Leave / kick (`FUN_004d6ac0`) | done: 0x157 to the victim, 0x150 to others, host hand-over 0x0D to the first in-game player (`FUN_004cdc50`), otherwise unlock and end the session (reason 0x10) |
| Generic forwarding (`FUN_004d3ea0`) | done for ~140 opcodes, table in `Session/Routing.cs`: pure forwards, forwards after a server-world update (not modelled yet) including cases that share a jump tail (e.g. 0x4C81D1, 0x4C9907); ~20 opcodes the original only applies to its own state (inventories, NW_ITEM cheat protection, references) are consumed; the original's internal menu commands (0x109..0x10C, 0x152..0x15B) are never accepted from clients. `tools`-style classification of every case is reproducible from the disassembly |
| Transport abstraction | sessions talk to `Net/IClientLink` (frame in/out, flush, close) and the host takes any number of `IClientListener`s (`GameServerHost.AddListener`); TCP (`ClientConnection`/`TcpAcceptor`) is the stock client's stream, and reliable UDP (KCP, as Sacred Bild uses for LAN) can plug in beside it |
| Bind address | `--bind=127.0.0.1` (key `BIND_ADDRESS`/`NETWORK_BIND`): the TCP acceptor and the SBT UDP listener bind that one address (a plain socket of its family, no dual-stack wildcard) and the LAN announce is skipped with a log line when it is loopback. Default as in the original: every interface. Tests set `BindPolicy.Default` to loopback (`LoopbackOnly.cs`), so no test opens a wildcard socket; the web UI is loopback by default |
| Sacred Bild UDP transport (SBT/KCP) | `Net/Sbt`: host side of Sacred Bild's LAN transport (sacred-bild docs/UDP_PROTOCOL.md) on UDP `--udp-port` (default 2105, 0 = off): HELLO/CHALLENGE cookie handshake (no amplification), WELCOME with random session id/token, REFUSED 2/3/4, DATA (KCP, conv = session id), KEEPALIVE (1 s, 200 ms while the peer is silent), CLOSE, 45 s time-out, roaming to the last valid source address, immediate resend after 300 ms silence. `Net/Kcp` is a managed port of ikcp.c (MIT) with pooled segments, tested by our own KCP tests (lossy link, header layout). Sessions are `IClientLink`s, so everything above works unchanged over it |
| LAN announcement (UDP broadcast, 0xAE payload) | done, format from `GoldLanAnnouncementCodec` |
| Player roster 0x1B (`FUN_004d3030`) | done: 17 x 834-byte records (session name, then each player's FIRSTCONTACT data), sent after admission (internal 0x101), the right password (0x12), ready (0x0F), connection speed (0x21) and client record uploads (0x1B, `FUN_004d2ee0`); the admission also sends the first time probe 0x43 (internal 0x41). The host may set a password with 0x12 while none is configured (case 0x4CA33E) |
| Object references, triggers, sectors, environment | done from the disassembly and checked against a captured GameServer.exe session: 0x153 reference grants (`FUN_004d6110`, 3000 for the host and 1000 per joiner at admission, 1000 to the others on the host's 0x18, 1000 when a sector entry reports fewer than 500, client requests; a leaver's refs go back to the pool), 0x154 trigger ranges, 0x148 sector entries (`FUN_004cf0d0`: script/init flags, ownership 0x020/0x01F and hand-over; first the objects of every entered sector go to the player, 0x169 if it has them, else 0x16F, `FUN_004cd4e0`), 0x14C to the players in the sender's sector, 0x00B world environment (calendar from 1370-02-14 08:00 at 24x, right after 0x011 and again after the refs at admission, every 30 s and after in-game sector entries; weather not simulated), 0x151 to the others on admission. No 0x01E at admission (the original only echoes the host's) |
| `Gameserver.cfg` keys | `NETWORK_PORT_TCP`, `NETWORK_PORT_LISTEN`, `NETWORK_PASSWORD`, `NETWORK_PRECONFIG_MODE/LEVEL/MAXPLAYERS`, `LADDER_EXPORT` parsed (defaults TCP 2006 / listen 2005 from the exe (0x4F5BAC/0x4F59FC); the file uses KEY:VALUE, and it is also read from --game-dir, mode->flag mapping is best effort) |
| Lobby registration (Open Internet) | done, see "Lobby" below; verified against `sacred-lobby` (Go) |
| Closed Net characters | done against sacred-lobby (run 13b, real client reaches the world as a Closed Net hero): once a player with a lobby identity and a character block 1..8 is admitted on a lobby channel other than 5 (GameServer.exe itself always registers in channel 5, so it cannot host Closed Net at all with that lobby; ours takes `--lobby-channel=N`), the server sends message 45 and then message 19 `[perm i32][block u16][offset 0][length 0]` to the lobby; the lobby answers with PublicData 11 holding the whole stored block (556-byte preview, u32 zlib size, u32 size, zlib hero file), which goes to the player as 0x132 (internal 0x29, case 0x4CB287; checked against block, perm id and the preview size 0x230). `LobbyClient.UploadCharacter` sends message 18 (the same layout, at most 0x20000 bytes) for the save-back, but nothing calls it yet: the original loads the hero into its world (strings "CHARACTER_SAVEGAME", "Character savegame successfull loaded.", `FUN_0046fad0` LoadHero per the lobby notes) and writes it back from there, which needs the real object world (roadmap). There is no hero save path in the stock GameServer.exe, checked twice: (1) decompile: the only function that sends to the lobby is `FUN_0048d380`, and its callers send exactly types 12 login (`FUN_0048c650`), 13 update (`FUN_0048c8d0`), 14 logout (`FUN_0048ca70`), 30 and 32 chat text (`FUN_0048cd80`, `FUN_0048cee0`, 0x166-byte body, from the server console), 45/46 player joined/left (`FUN_0048d130`, `FUN_0048d1c0`) and 55 (`FUN_0048d310`, called from `FUN_004ED7E0`: the request after the lobby sent the public address); there is no 10/18/19. (2) capture (runs 11b/11c against sacred-lobby `closed_net: true`, 2 minutes in the world, then the in-game menu Beenden): the lobby sees only 12, 45 (answered by the lobby's push of the block as PublicData 11, which the stock server forwards as 0x132 = the raw 4986-byte block), 13 updates and on leave 46, 13, 14; never 10, 18 or 19. The in-game menu's "Exportieren" opens the local character export screen (it does not touch the net). On leaving, the client sends 0x14F (9940 bytes: the player's statistics block) to the server, which copies it into its `cStatsManager` slot (case 0x4C6030: `FUN_00477500(slot)` then `FUN_00477490`, 0x26D4 bytes = 0x99C + 25 dwords) and nothing else; this is why 0x14F is in the consumed list. So the hero is not persisted at the lobby by the original server (the client keeps its own copy: `Save\HeroNN.pax` is rewritten by the client). A capture of the stock server in Closed Net is possible only with sacred-lobby's `closed_net` option and its push |
| Object world (`Session/ObjectMirror.cs`, `GameServerHost.World.cs`) | done as a mirror: 0x16E/0x16F are absorbed and streamed once to each in-game player lacking the object (`FUN_004cd040`), the whole world goes to a participant before its 0x017 answer (by reference order), 0x169/0x16C/0x11A/0x11B absorbed + forwarded, 0x16B/0x16D/0x14B absorbed only, 0x166/0x167 from the owner delete and notify holders, from others only clear their bit (case 0x4C4D86). The mirror keeps the latest serialized state; it does not apply later field updates (0x165 position etc.) like the original's real objects do |

Opcodes not covered by the table or a case are logged once per opcode (`unhandled opcode 0x...`) and dropped, like the
original's default case (`0x4CC598`). Real client traffic against this server shows what is still missing.

### How the dispatcher was mapped

`FUN_004c3f20` (34 KB, Ghidra fails on it) was disassembled with capstone: the switch is on `opcode - 3` (3..0x1D5) through the
byte index table at `0x4CC9C4` and the jump table at `0x4CC6E0`. Every case that only calls the forwarder `FUN_004d3ea0(except,
data, size, opcode, origin, mode, object)` went into `Routing.cs` with its mode; cases with own logic are in
`GameServerHost.Gameplay.cs`, each commented with its case address.

### Approximations (no server world yet)

The forwarder modes 3 (sector visible), 4 (not visible), 5 (party), 6 (object mask), 7 (same sector) and 8 (sector + object)
filter by the server's own sector/object state. Without that world they are sent to every other in-game player, which is a
superset of what the original sends: clients receive messages for objects they may not know (they ignore them), so it is
correct but chattier. Mode 1 (connected) and 2 (in game) are exact.

## Run

```
SacredGameServer --game-dir=/srv/sacred --network-port-tcp=2006 --network-password=secret
SacredGameServer --accept-any-data      # development: skip version/fingerprint checks
```

`--game-dir` needs `Sacred.exe`, `tincat2.dll` and the Pak/Bin data of the supported English Gold install (same files the
client hashes); clients with different data are rejected exactly like the original (reason 6 + mask).
All `Gameserver.cfg` keys can also be passed as `--key=value`. The command line is strict: an unknown option or a bad value is refused with exit code 2 (and a "did you mean" hint) before any port is bound; `quiet` (the original's no-dialog switch) is accepted. `--help` is generated from the same option table (`Config/CommandLine.cs`). Gameserver.cfg lines, `webui.json` and environment variables stay lenient.

## Lobby

`--network-lobby=<host>` (`NETWORK_LOBBY`, port `NETWORK_LOBBYPORT`, default 7066) registers the game the way GameServer.exe's
lobby module does (`FUN_0048b880`, vtable 0x533E6C, receive switch 0x48ED40). It runs on its own thread
(`Lobby/LobbyClient.cs`); the game loop only hands it snapshots and reads events (`Session/GameServerHost.Lobby.cs`).

| Message | When | Source |
| --- | --- | --- |
| TinCat logon `user`/`passwor` | connect, reconnect with backoff 2..60 s, keep-alive 15 s, timeout 70 s | TinCat values set in `FUN_0048b880` |
| 12 game record (116 bytes) | after logon: name (cp1252), LAN IP, public IP, port, players, max, flags (server word 0x5848 incl. 0x80), network version 10, client version, channel 5 | `FUN_0048c650` |
| 12 (4 bytes) or 38 from the lobby | public IP; then all admitted players are announced | internal 0x25 (0x4CB1C1) |
| 13 update | players, max players, flags or name changed | `FUN_0048c8d0` via `FUN_004e99a0` |
| 45 player joined `[perm][block][first]` | admission of a player with a lobby identity (not 0/-1); re-sent with first=0 after a reconnect | `FUN_0048d130` / `FUN_004ed690` |
| 46 player left `[perm]` | an announced player leaves | `FUN_0048d1c0` via `FUN_004ea3e0` |
| 47 kick | player removed with reason 8 | internal 0x34 (0x4CBBC4) |
| 50 shutdown | 0x009 0x68 + 0x008 to all, then everyone removed (reason 8) | internal 0x3E, `FUN_004d6c50` |
| 11 data | Closed Net block (> 0x230 bytes, block != 10, perm+block match) sent as 0x132 | internal 0x29 (0x4CB287) |
| 14 logout + TinCat log-off | orderly stop (SIGINT/SIGTERM) | `FUN_0048ca70` |

GameServer.exe never sends 18/19 (its data request/upload slots in the vtable are `xor al, al` stubs), so neither does this server.
`LobbyTests` run against a fake lobby; the same flow was checked against a locally built `sacred-lobby`.

## Allocation rules

* Receive: one `SocketAsyncEventArgs` per connection over an `ArrayPool<byte>` buffer; frames are reassembled in place,
  payloads are rented and returned by the game loop.
* Send: the loop appends frames into a pooled fill buffer; `Flush()` swaps it with the in-flight buffer and reuses one send
  `SocketAsyncEventArgs`.
* Game loop: one thread, fixed step (`TICK_MS`, default 20), preallocated scratch buffers (decode 4 MB, build/envelope 256 KB),
  `ref struct` message views, `stackalloc` for tiny replies. `LoopbackTests.PacketLoopDoesNotAllocate` measures
  `GC.GetAllocatedBytesForCurrentThread` over 250 ticks of relayed chat (compressed and raw envelopes) and requires < 2 KB.
* Allocations remain only at connect/disconnect (connection object, SAEA), in log lines for rare events and on the lobby
  thread (a few small messages per minute).

## Tests

`dotnet test Sacred.GameServer.Tests` (needs no game files): wire interop with `Sacred.Network` and the BCL zlib, loopback
tests with the remake's own `GoldGameConnection` (admission, host flag, duplicate identity, version and data mismatch,
password, chat, allocation), and session-flow tests with a small raw client (`RawClient.cs`): host start and participant
approval, routing table, in-game filtering, host-only refusal and lock, flag merge, kick, host hand-over, session end, world
bank order, time probes; and lobby tests with a fake lobby (game record, announce/leave/update, duplicate identity while
online, lobby kick, Closed Net character, lobby shutdown).

## Roadmap (what the original GameServer does that this does not yet)

1. Real server objects instead of the serialized mirror: apply field updates (position 0x165, values, inventory) so
   streamed objects are current, serialize the banks from current state instead of replaying the host's upload, load
   Closed Net heroes (`CHARACTER_SAVEGAME`) and upload them on save/leave. `Sacred.Network`'s object codecs and
   `Sacred.GameplayHost` are the building blocks.
2. Authoritative mode (optional): the server owns sector state (balance, enemy and game difficulty) from its config instead
   of the first client entering a sector, and checks client reports (hero stats, items, gold, damage, kills) against the
   data tables; later a full simulation (monsters, loot, combat, scripts). John's C++ re-creation of Sacred.exe
   (`sacred-decomp`) is the logic reference.
3. Exact party / sector / visibility filtering for forwarder modes 3..8 (needs 1).
4. Remaining custom cases: 0x004 team chat, 0x00F, 0x01D savegame, 0x029 character savegame, 0x104..0x107, 0x118, 0x139,
   0x13F (NW_ITEM_SETITEM re-sent as 0x16A), 0x148, 0x14C, 0x168 (delete with equipment), 0x17D, 0x17F, 0x180, 0x198.
   The server-side inventory and NW_ITEM cheat protection of the original (0x122..0x131, 0x13E/0x13F, 0x144) are the
   starting point of the authoritative mode.
5. Server-side Lua API for Sacred Gear server mods (events, per-player storage, messages/items; enough for e.g. achievements,
   not full parity with the client API).
6. Ladder export (`LADDER_EXPORT`).

## Server scripts (Lua)

Lua scripts run on the server itself, with [LuaCSharp](https://github.com/nuskey8/Lua-CSharp) 0.5.8 (pure managed Lua 5.2:
no native library, Native AOT clean, one binary). Every `*.lua` file of the scripts folder (`--scripts-dir`, default
`scripts`) is loaded in name order when the server starts; a changed, added or removed file reloads only that mod half a
second later (see "Hot reload" below); no restart is needed. Without the folder (or with `--scripts=0`) nothing runs. `scripts-examples/kill_milestones.lua` is a
complete example.

```lua
server.on("join", function(player) end)          -- admitted
server.on("enter", function(player) end)         -- in the world (0x017/0x018 answered)
server.on("leave", function(player) end)
server.on("chat", function(player, text) return true end)   -- public chat; true = not relayed (commands like "!kills")
server.on("kill", function(player, monster) end) -- monster = { ref, type, level, name, sector, x, y }
server.on("death", function(player, by) end)     -- a hero died; by = { player } or { ref, type, level }
server.on("sector", function(player, sector) end) -- the hero moved into another sector (player.sector is the same number)
server.on("drop", function(item, reporter) end)  -- a new item lies on the ground; item = { ref, type, level, rarity 0..15, sockets, price, name, sector, x, y }
server.on("start", function() end)               -- after (re)loading (a reloaded mod's own start handlers run again)
server.on("unload", function(reason) end)        -- before this mod is replaced ("reload") or the server stops ("shutdown"): save state here
server.every(seconds, fn)  server.after(seconds, fn)
server.broadcast(text)  server.message(player or slot, text)  server.kick(player or slot, reason)
server.players()  server.player(slot)  server.time()  server.log(text)  print(...)
server.save(key, value)  server.load(key [, default])  server.delete(key)  server.keys([prefix])   -- the mod's own storage
server.flush([fn])                     -- write this mod's storage now, durably; fn(ok, err) afterwards on the game loop
server.authoritative(player)  server.save_hero(player, fn)  server.hero_upload_time(player)   -- Closed Net hero saves (see "Hero custody")
server.hero_item(player, ref)  server.hero_items(player)  server.claim_item(player, ref)     -- items in, leaving and arriving in a hero
server.get(player or nil, key [, default])  server.set(player or nil, key, value)  server.add(player or nil, key [, n])
server.drop_item(player, { quality = 0..14, category = "ring", level = n, class = n, seed = n })  -- item or nil, error
server.drop_gold(player, amount)       -- { ref, type, amount } or nil, error: a gold pile next to the hero
server.random()  server.random(n)  server.random(m, n)
server.creature_name(type or monster)  server.item_name(type or item)   -- the game's name, or nil
server.hero(player or slot)            -- table, or nil + reason: the server's view of a hero (see "Hero data for scripts")
server.sector_cell(id)  server.sector_at(cellX, cellY)   -- the sector grid { x, y } / id (needs --game-dir), or nil
server.top(key, n)                     -- { { name, value }, ... }: this mod's per-hero numbers, also offline heroes
server.endpoint(path, fn [, { public = true, description = "..." }])   -- GET /mods/{script}/{path}
server.page(path, html or fn [, { public = true, title = "..." }])     -- a single-file HTML page at the same address
server.describe(text)                  -- short description for the Mods tab (default: first sentence of the leading comment)
server.file(name)  server.html(text)  server.render(template, values)  server.csv(rows [, columns])  server.json(table)
```

**Mod endpoints.** `server.endpoint` gives a script its own HTTP routes on the web UI: `GET /mods/{script file name without
.lua}/{path}` (admin password like the rest of the API) and, for endpoints marked `public` while `WebUi:PublicApi` is on, `GET
/public/mods/{script}/{path}` for anybody (CORS `*`, may be framed anywhere). The handler gets `{ path, query = { name = value } }`
and returns a string with an optional content type and status (`return server.csv(rows, columns), "text/csv"`), or a table that
is sent as JSON (a sequence or an empty table becomes an array). `GET /api/mods` and `/public/mods` list them. The web thread
queues the request and the game loop runs the handler (budget and error handling as for events; at most 64 waiting, 5 s
time-out, 503/504 otherwise). Answers carry `Content-Security-Policy: default-src 'none'`, so script output cannot run code on
the server's site. Example: `ladders.lua` serves `/mods/ladders/top?board=kills&n=10` (JSON) and `/mods/ladders/by-creature`
(CSV: creature type, best hunter, kills).

**Mod pages.** `server.page(path, html_or_function, { public = true })` serves single-file HTML at the endpoint address: a fixed
string (often `server.file("dashboard.html")`, a plain file name from the scripts folder: .html .htm .css .js .txt .json .csv
.svg, 2 MB) or a function that builds the page on each request. The web UI sends pages with
`Content-Security-Policy: sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline';
img-src data: https:; connect-src 'self'; form-action 'none'; base-uri 'none'` and `X-Content-Type-Options: nosniff`: the
page's own inline script and style run, but in a sandbox (opaque origin), so a page cannot use the admin's browser credentials
against the server's API, cannot load outside scripts, post forms, change the base URL or open plugins; it may fetch public
endpoints (CORS `*`). Text from players goes through `server.html(text)` (HTML escape) or `server.render(template, values)`:
`{{name}}` is HTML-escaped, `{{json name}}` is JSON that cannot close a script element; there is no raw insertion. The admin
page lists every mod page and endpoint in its "Mods" card. Example: `ladders.lua` serves a dashboard at `/mods/ladders/` with
cards for the top heroes (kills, level, time online), the best hunter per creature and recently died heroes.

**Mod storage.** Each mod (script file) has its own file `{--scripts-data folder}/{mod}.json` (default folder `script-data`):
`server.save(key, value)` keeps any Lua value, tables included (lists and maps, nested; numbers, strings, booleans), `load`,
`delete` and `keys` read it back. `server.get/set/add(player, key ...)` keep per-hero values in the same file (with the hero's
character name for `server.top`); with `nil` instead of a player they are plain keys. Written through a temporary file and a
move; the game loop only serializes changed mods, the file is written on a worker thread (flushed every 10 s, on reload, and
awaited at shutdown). Limits: one value 4 MB, one mod 8 MB (`--scripts-store-max`), all mods 32 MB (`--scripts-store-total-max`),
counted in UTF-8 bytes of the stored JSON (endpoint and page answers are capped in characters); a save, set or add past them fails with a script error and changes nothing, shrinking is always
allowed. `--scripts-data` names a folder now: a value ending in `.json` (the earlier single-file setting) stops the server
with an error instead of silently becoming a folder of that name. The game server itself keeps no state for
mods: ladders, recent deaths, statistics live in the mod that needs them, so mods can grow without growing the server. (JSON,
not SQLite: SQLite needs a native library, which would end the single-file Native AOT binary.)

**User documentation of the example mods** (install, chat commands, settings, pages and endpoints, Sacred Gear channels,
limits, screenshots of the web pages): [`docs/mods/README.md`](../docs/mods/README.md).

**Durable writes (`server.flush([fn])`).** For a mod that must not answer a player before its change is on disk (the auction
house: a delivery that was answered and then lost in a crash before the 10 s flush could be collected twice). The store is
serialized at once on the game loop thread and written on a worker thread after any write still running: temporary file,
flushed to the disk (`FileStream.Flush(true)`, write-through), moved over the old file. `fn(ok, err)` runs on the game loop at the
next pump after the write finished, in the order the writes were started; the game loop never waits. While a durable write is out
(until its callback ran) the regular 10 s flush skips this mod's file, so after a failed write the file still holds the state from
before it and the script can undo its change in memory first (the next flush writes the undo). A reload first drains the writes in
flight (their callbacks run, the old script is still intact); callbacks of a `server.flush` started by an unload hook are dropped
after a reload (the write still finishes before the mod is replaced) but answered at shutdown. Code: `Scripting/ScriptHost.Durable.cs`, `ModStore.Durable.cs`; tests
`ScriptTests.Durable.cs`.

**Hot reload and the `unload` hook.** `server.on("unload", function(reason) ... end)` runs while the old script is still intact
(same instruction budget as any handler): `reason` is `"reload"` (the mod is replaced) or `"shutdown"` (the server stops; the
hooks run on the game loop before the stores are flushed, and the durable writes they start are awaited). Save state with
`server.save(...)` and `server.flush()` there; then everything the script registered (handlers, timers, endpoints and pages, net
handlers) is dropped and the file runs again. The mod's storage lives in the host and survives a reload, so the new version reads
back what the old one saved, exactly as after a restart. Order: pending durable writes are drained (callbacks answered), the
`unload` handlers of the mod run (all mods on a full reload, the last loaded first), the writes they started finish, the
registrations are dropped, the file(s) load, then `start` fires (for a per-mod reload only that mod's handlers). Reloading:
the folder watcher reloads only the file that changed (a new file loads, a deleted one unloads); the Mods tab of the web UI has a
Reload button per mod and "Reload all mods" (`POST /api/reload-mods` with `{"text": "mod"}`, empty text = all); `ScriptHost.Reload(mod|null)`
from code. A full reload creates a fresh Lua state (global variables reset); a per-mod reload runs the file in the shared state, so
globals it set earlier stay until overwritten, a reloaded mod's handlers run after the others', and mods that share globals should be
reloaded together. Code: `Scripting/ScriptHost.Reload.cs`; tests `ScriptTests.Reload.cs`. The example mods are built on it: each saves its in-memory state in an unload handler and keeps its tunables as data in its storage key `config` (defaults in the script seed it, `POST /api/mod-store` edits it live, `/mods/{mod}/config` is the admin overview); see [`docs/mods/README.md`](../docs/mods/README.md#configuration-live-edits-and-hot-reload), tests `ScriptTests.ModConfig.cs`.

**Hero custody (Closed Net, `--authoritative`).** For mods that move items or gold between a hero and their own storage (auction
house, shared stash) so that killing the client never restores a save with the item twice: the server is the one that writes the
hero, so a mod accepts a transfer only after the server saw it in its own hero state and the lobby stored the hero.
`server.authoritative(player)` is true when the server writes this hero (authoritative mode, a Closed Net hero, the lobby online).
`server.save_hero(player, fn)` saves the hero now from the server's view (as the anti-dupe saves) and calls `fn(ok, err)` on the
game loop once sacred-lobby answered the upload (message 15 code 12 = stored in one SQLite statement; anything else, a lost
lobby connection or 30 s without an answer is `ok = false`). `server.hero_upload_time(player)` is the unix time of the last
hero upload sent for that hero (any reason, also earlier sessions while the server runs), or nil: no save went out since a moment
means the lobby's hero is still older than it. `server.hero_item(player, ref)` returns `{ ref, type, where, records, age }` for one
item object of the player: `where` = `backpack` (anchored in the hero's own inventory, 0x122/0x13E/0x13F), `stash` (inventory 17),
`equipped`, `world` (an object of the player in no inventory) or `gone` (destroyed by its owner with 0x167: the server keeps the
last 64 per player for 10 minutes, with their records); `records` is the item's value records as hex (one 130-byte block per
socketed rune/gem, then its own), the same bytes Sacred Gear's `gear.inv.export` carries after its u32 type and u16 count, so a
mod can check that exported data belongs to that object. An anchored item without a server state has `type` 0 and empty
`records`. `server.hero_items(player)` lists the items in the inventories and equipment; `server.claim_item(player, ref)` takes a
`gone` item off the list (true once), so one destroyed item backs one transfer. Hero gold is `server.hero(player).gold`. Code:
`Session/GameServerHost.ScriptHeroes.cs`, `Scripting/ScriptHost.Heroes.cs`, `Lobby/LobbyClient.Uploads.cs` (upload answers); hooks
in existing code: one call in `RemoveObject`, three lines in `LobbyClient`. **Unverified with a real client:** that Gear's
`inv.remove` reaches the server as 0x13E + 0x167 and `inv.add` as 0x16F/0x16C + 0x13F, that the export's records equal the
server's in order, and how soon a client reports a gold change in its hero state.

* `player` = `{ slot, name, character, uid, account, class, level, in_game, host, online, sector, x, y }`. `account` is an opaque per-server key of the player's lobby account (all heroes of one account share it; absent without a lobby identity), made like `uid`. **Privacy:** scripts never see lobby ids,
  owner keys or account names. `name` and `character` are the character name (the server strips the `account.` prefix of
  lobby names, and only of lobby names: a LAN name like `Mr.X` stays), `uid` is an opaque per-server identity of the hero
  (HMAC-SHA256 of its lobby identity with a secret kept in `{scripts-data}/.uid-secret`): stable across sessions on this
  server, meaningless anywhere else. The data folder gets a `.gitignore` of `*` when the server creates it, and the secret is
  created owner-only (0600) on Unix, so neither ends up in a commit or readable by other users. Per-hero storage is keyed by `uid`, so the mod files hold no lobby ids either.
* Hero deaths ("death"): 0x17D whose reference is a hero (1..16); the killer word is another player's slot or the killing
  object (type and level from the object world); several reports within 2 s count once. Unverified with a real client that
  hero deaths arrive as 0x17D.
* Kills come from 0x17D `[creature ref][killer]` (GameServer.exe case 0x4C647E: native death event 0x208 on its creature). The
  killer word is a player slot, or an object of a player (credited to its owner), else the sender. Type and level come from
  the creature's last 0x16E/0x16F state in the server's object world (0 when the server never saw it). Each creature counts once.
  `name` is the game's text (see *Names* below): the creature's own name word when it has one, else its type's ("Orc Warrior",
  551 "Horse"); nil without `--game-dir`.
* Messages from scripts are server chat (origin 0), like the web UI's broadcast and private messages.
* Sandbox: basic (no `dofile`, `loadfile`, `load`, `collectgarbage`), `string`, `table`, `math`, `bit32`; no `io`, `os`,
  `require` or `debug`. Each handler or timer call may run `--scripts-budget` Lua instructions (default 1,000,000, a few ms);
  more stops it with an error (an endless loop cannot freeze the game, `pcall` cannot swallow the stop), and a handler that
  fails 20 times in a row is switched off. Any exception from a script call (Lua errors, the budget, or a .NET exception thrown by a `server.*`
  function or the interpreter) is logged and contained: a broken script never takes the server down; the hooks around script
  calls catch everything except out-of-memory. Errors are logged with the file name. Scripts are the server owner's code: the
  sandbox protects against mistakes, not against a hostile author.
* `Sacred.GameServer` references `Sacred.GameplayHost` for the drop code, which brings `Sacred.Assets` and with it `Sacred.Granny`
  (the Windows Granny DLL loader). The server never calls it and Native AOT trims it away (no ILC warnings, the native library
  is never loaded); dropping the reference would need changes in the shared projects.
* Runs on the game loop thread; calls into Lua do not allocate, a kick requested by a script runs after the script returns.
* `drop_item` generates equipment with the remake's port of the original drop code (`Sacred.GameplayHost`
  `WorldEquipmentDropSimulator`, checked against native replays): category for the class, type for the level, rarity
  (game weights, or `quality` 0..14), values and name; categories `weapon armor ring shield helmet boots belt amulet shoulders
  arms legs gloves`. It needs `--game-dir`. The item is a new object with a server reference, owned by the receiving player,
  placed at its hero's position and sent to every in-game player as 0x16F through the object world. The object's type is the
  value block's resource (Weapon.pak row +0x80), not the row index. `scripts-examples/loot_rewards.lua` rolls rarities from a
  weight table on kills. **Unverified with a real client:** that the owner can pick it up (its client handles pick-ups of its own
  objects) and how `class` maps to the game's 0..9 selector (the player's FIRSTCONTACT class byte is passed as is).
* Ladders: `scripts-examples/ladders.lua` keeps most kills, highest level, time online (+60 s per connected hero each minute),
  kills per creature and the last 20 deaths in its own storage and serves them as a page and as JSON/CSV endpoints; `!top`
  shows the kill ladder in chat. `player.online` is the current session in seconds. `player.sector` is the sector number the hero stands in (first word of its last 0x148 sector entry; 0 until the server has seen one); the `"sector"` event fires when it changes. `player.x`/`y` are the hero's world position from its last state the server saw (0 when none; it can lag behind a moving hero: position updates are not applied yet). A kill's `monster.sector`, `x`, `y` come the same way from the victim's last state in the object world (absent when the server never saw it). **Sector ids are slots of the game's sector table, not coordinates**: `server.sector_cell(id)` gives the id's cell of the 128 x 128 sector grid and `server.sector_at(cx, cy)` the id at a cell, from `World/sectors.keyx` of `--game-dir` (nil without it). One tile is 53.67 world units and a sector 64 tiles, about 3435 units.
* World events: `scripts-examples/world_events.lua` runs server-wide timed goals ("slay 1000 orcs in 2 hours", optionally only in some sectors, filtered by creature name or type) from a config table at the top of the file: a rotation with a cooldown, 25/50/75 % and result broadcasts, a reward shared by contribution (`drop_gold`, `drop_item`; heroes who are offline get theirs when they enter), `!event` and `!sector` in chat, a public dashboard page and `/mods/world_events/state` (JSON, character names only), and the state pushed over the net channel `worldevents:state` to the Sacred Gear mod `world-events` (a movable progress bar). Everything, including an event that ran out while the server was down, lives in `script-data/world_events.json`. Uses `player.sector` and the `"sector"` event. Events can be tied to named **areas** (sector lists and ranges, a grid rectangle, `near` neighbours, a world-unit circle; a kill counts where the creature died, `where = victim|player|either`); admins record areas by walking with `!area start/stop` and read the numbers off with `!sector`/`!here`; recorded areas live in the mod's storage (`areas`). See the comments at the top of the script. **Reward odds:** an event can have rarity milestones (`reward.milestones = { { at = 1, weights = { common = 100 } }, { at = 10, weights = { common = 75, magic = 25 } }, ... }`): tier chances are weight / sum of weights, the best tier can never be 100 % (a row that breaks that, has a negative or no weight, or an unknown tier is refused with a log line), every hero with the minimum kills gets one item rolled with `server.random` when the event ends using the odds of the highest milestone reached (a failed event rolls too, unless `on_fail = false`; gold and the top prize only for a completed one), and nothing else (level, class, kills above the minimum) changes the roll. All odds are public: dashboard table, `/mods/world_events/state` (`reward_odds`, `reward_rules`, `rewards` = the recent rolls with character names only), `!event rewards`, the announcement at each milestone and the tooltip of the Sacred Gear mod.
* Bounty board: `scripts-examples/bounty_board.lua` posts a few bounties at a time ("kill N creatures whose name contains ...", optionally only in some sectors, `min_level`), each hero works on them separately, finishing one pays gold (`drop_gold`) and/or an item (`drop_item`) once; bounties expire and are replaced from a pool in the file. `!bounty` lists them, `!where` tells the sector number; a public dashboard at `/mods/bounty_board/` and JSON at `/mods/bounty_board/state`; the net channel `bounty:state` pushes each hero's own progress to Sacred Gear clients (client mod `gear/examples/bounty`, panel on F8). Everything is in the mod's own storage; no host API was added.
  shows the kill ladder in chat. `player.online` is the current session in seconds.
* Achievements and login streak: `scripts-examples/achievements.lua` unlocks 31 achievements from kills, distinct creature types,
  "giants" (a creature 10+ levels above the hero), deaths, players defeated, hero level (read from the player on kills, joins and a
  20 s timer: there is no level event), time online, days visited and the streak, and pays rewards with `drop_gold` / `drop_item`
  (held until the hero is in the world; no `--game-dir`: the item becomes 1000 gold). The first entry into the world on a new UTC
  day extends the streak (a missed day resets it) and pays 100 gold per streak day up to 1000, plus an item every 7th day. Page
  `/mods/achievements/` (public with `WebUi:PublicApi`), JSON `players` and `achievements`, chat `!ach` and `!streak`, and the
  net channel `achievements:sync` for the Sacred Gear mod `gear/examples/achievements` (toast on unlock and streak, a panel;
  messages `{op = "state" | "unlock" | "streak"}` to the client, `{op = "get"}` from it). Gold held/earned and exploration are not
  achievements: the script API has no data for them. Tests: `ScriptTests` (fake server) and `NetOpsTests` (live channel).
* Ground items ("drop"): the game has no drop message. Creature loot, chest contents and items a player throws down all show up
  as a 0x16F state with a position, sent by the client that owns the sector; the first such state of an item object the server's
  world did not have yet fires `drop` (`item.rarity` is the tier 0..15 of the value block: Sacred Gear shows 0-4 normal, 5-11
  magic, 12-14 rare, 15 unique; `reporter` is the player whose client reported it, which is not necessarily the killer; items
  without a position, updates of known objects and the server's own `drop_item`/`drop_gold` objects do not fire it). Items already
  lying in a sector when its owner enters also arrive this way, so a mod that wants creature loot ties a drop to a recent
  `kill`, as `rare_drops.lua` does. **Unverified with a real client:** the rarity mapping and that all loot arrives as 0x16F.
* Statistics mods (`scripts-examples`, each with a public page and JSON endpoints, own storage, tests in `ScriptTests.Mods.cs`):
  `server_stats.lua` (online, peak, uptime, totals, 24 h charts as inline SVG), `hall_of_fame.lua` (records, first to reach a level
  or defeat a creature, `!hof`), `seasons.lua` (ladders per season: timer or `/mods/seasons/end-season` (admin), archive of past
  seasons, `!season`), `rare_drops.lua` (live feed of rare/unique drops from the `drop` event, chat line, Gear client channel
  `rare_drops:feed`) and `graveyard.lua` (recent deaths with killer names, deadliest killers, `!rip`, channel `graveyard:feed`).
  The Sacred Gear mod `gear/examples/serverfeed` (sacred-bild) shows the two channels on screen.
* Mods built on `server.hero` and `net`: `scripts-examples/build_sharer.lua` (players publish their build, stored per mod, a searchable page, JSON endpoints and a channel for the Sacred Gear panel `buildsharer`) and `scripts-examples/quest_helper.lua` (a hero's quest records with an owner-written catalog of names, areas and hints, change notices, a page; Gear panel `questhelper`). Client halves: sacred-bild `gear/examples/buildsharer` and `gear/examples/questhelper`; docs there in `docs/GearServerMods.md`. `ModScriptTests` cover them.
* `drop_gold` drops a gold pile the same way (no `--game-dir` needed). Money is an ordinary item object: resources
  0x140C..0x140F (Gold client table 0x949920 / test 0x43AF50, GameServer.exe 0x5B0274 / 0x409D30), pile size by amount as the
  game picks it when gold is dropped (0x4AEC1E: below 50, 350, 1000, else). The amount is the value block's price (+8, cItemBase
  +0x120, set by 0x5C7D10); the rest is the Weapon.pak "Gold" row's instance values (flags 0x00C01D3F). Pricing keeps a nonzero
  money price (client 0x5D3AB0, GameServer.exe 0x426D80). Picking it up (client 0x558304) reads the price, removes the object and
  adds it to the hero's gold (values +0x46, AddGold 0x57FD80, at most 0x7FFFFFFE, the cap `drop_gold` applies). Amounts are
  whole numbers of at least 1. **Unverified with a real client:** as for `drop_item`, that the owner's pick-up of a
  server-referenced object runs that path, and that the new gold reaches the server in the hero's values (saves).

### Hero data for scripts (`server.hero`)

`server.hero(player or slot)` returns a fresh read-only table of what the server knows about that hero, or `nil` and a reason. It is the
data of the web UI's `/api/heroes` (the same code, `HeroOf`), so it exists only while the server keeps hero state (the web UI runs, or
`--authoritative`) and once the player's client has sent its hero. Fields: `character`, `class`, `level`, `experience`, `gold`,
`skill_points`, `combos`, `in_game`; `skills` (`id`, `level`, `bonus`, `name`), `attribute_points` (the six spent points), `attributes`
(six totals, only after a full 0x16B state), `arts` (`kind`, `id`, `rank`, `name`), `equipment` (`slot` 0..18, `type`, `level`, `rarity`
0..15, `sockets`, `name`), `inventory_items` (a count), `quest_pairs` (`key`, `value`: the `[key][word04]` records of the inventory
stream, see "Quest pairs"). Names are the game's text and exist only with `--game-dir`. No account, lobby id or owner key. What it does not
have: the quest journal, active quests and their progress (world state of the game's interpreter, not in a hero file and on no wire message).
In the five hero saves we have, the quest pairs are empty, so a script cannot rely on them being filled.

### Custom net messages for scripts (`net`)

Sacred Gear clients (the mod kit, `gear.net`) talk to scripts through named channels over two message types above the
original's range (default 0x1E0 control / 0x1E1 data; the original GameServer.exe only logs and drops them). A client
says HELLO after joining; the server answers with its limits and the list of players that can use the messages. Only
players that said HELLO ever get them, so plain clients are not affected.

```lua
local ch = net.channel("auction:bid" [, { server_only = true }])  -- the channel, or nil, message (bad name, id collision)
ch:on(function(player, payload) end)    -- a message for the server; payload is a table (JSON) or a byte string
ch:send(player | slot | "all", payload) -- string (one byte per character; above 255: UTF-8) or table (JSON) -> true | nil, message
net.supports(player | slot)             -- true when that player's computer said HELLO
```

`server_only` keeps clients from relaying through the channel (clients can still address the server, never each other). A
channel id is the FNV-1a hash of its name; two names with the same id are refused at registration. The server stamps the
real sender slot into the message header and ignores any claim in the payload. Handlers get their own instruction budget
(`NETOPS_HANDLER_BUDGET`, default 200000), and the usual error accounting (20 errors in a row switch a handler off).

Options (`Gameserver.cfg` / `--key=value`, ids also from the environment variables `NETOPS_CONTROL` / `NETOPS_DATA`; clients learn
the data id in the handshake, the control id is configured on their side with `gear.net.configure` or `GEAR_NETOPS`):

| Key | Default | |
|---|---|---|
| `NETOPS` | 1 | 0: the opcodes stay unhandled like in the original |
| `NETOPS_CONTROL`, `NETOPS_DATA` | `0x1E0`, `0x1E1` | above 0x1D5, below 0x400, different |
| `NETOPS_MAX_FRAGMENT` | 4000 | data bytes per wire message (64 to 4000) |
| `NETOPS_MAX_MESSAGE` | 262144 | bytes of one reassembled or relayed message |
| `NETOPS_MAX_IN_FLIGHT` | 8 | unfinished messages per sender |
| `NETOPS_RATE`, `NETOPS_BURST` | 65536, 262144 | per session token bucket (bytes per second, capacity), on top of the flood guard |
| `NETOPS_MAX_CHANNELS` | 64 | different channels one client may use |
| `NETOPS_MAX_NAME` | 64 | characters of a channel name |
| `NETOPS_REASSEMBLY_SESSION`, `NETOPS_REASSEMBLY_TOTAL` | 1 MB, 8 MB | bytes of unfinished messages held |
| `NETOPS_FRAGMENT_TIMEOUT` | 10 | seconds until an unfinished message is dropped |
| `NETOPS_HANDLER_BUDGET` | 200000 | Lua instructions of one handler call |

Everything dropped is counted (`GameServerHost.NetStats`): unknown channels (a message for the server on a channel no script
registered), rate limit, too many messages in flight, too many channels, too large, reassembly memory, timeouts. Wire layouts:
`docs/protocol.md` ("Custom net opcodes"); the code is `Wire/NetOpsWire.cs`, `Session/GameServerHost.Net.cs`,
`Scripting/ScriptHost.Net.cs`. Example script: `scripts-examples/net_echo.lua`.
## Player data (web UI)

With the web UI running the server keeps every player's hero state, equipment changes and inventories (also without
`--authoritative`) and refreshes a snapshot once a second: `GET /api/heroes`, `/api/heroes/{slot}` (admin, with the lobby
account).

`WebUi:PublicApi=true` (`--webui-public-api`) also serves `GET /public/heroes`, `/public/heroes/{character}` and the public mod
pages and endpoints (`/public/mods/...`) to anybody, with `Access-Control-Allow-Origin: *` and a 2 s cache, for embeds on other sites,
stream overlays and the like. Read-only (GET), no password; the account part of lobby names and the slot are left out. Off by
default.

A hero: character, class, level, experience, gold, remaining skill points, skills (game skill id, level, bonus), the six spent
attribute points, the six attributes (only after the client sent a full 0x16B state, else null), combat arts (kind, art id,
rank byte), number of combos, equipment (slot 0..18 and item), inventory and shared stash (anchor cell, width, height, item),
online seconds. An item: reference, type (Items.pak resource), level, rarity tier, filled sockets, price. Skills, arts and
items also carry a `name` when the server read the game's texts (`--game-dir`, see *Names*); without them the field is left
out and only the game's numbers remain. Source: the hero's own object
states (Gold 0x585C60 values at +445: compact fields, or the full sCharValHero block from 0x16B), 0x11D/0x11E, 0x122, 0x13E/0x13F
and the items in the object world.

### Area editor (`/areas`, admin)

A page of the web UI to draw the sectors of an area on the game's own world map. It needs `--game-dir`: the server reads
`World/sectors.keyx` (id, cell of the 128 x 128 sector grid, authored name of every sector) and the Ancaria overview (the 64 textures
`UI_WORLDMAPX<row><col>.TGA` of Texture.pak, one 2048 x 2048 PNG, about 10 MB, built once on a worker thread and cached by the browser).
`GET /api/areas/map` answers the sectors and the projection, `GET /api/areas/map.png` the image (`Data/AreaMap.cs`; the projection is
the game's own world-map one, tiles to map pixels: `x = ox + (tx - ty) * kx`, `y = oy + (tx + ty) * ky`; a sector cell covers 64 x 64
tiles; the underworld and caves are not on the overview). Sector ids are slots of the sector table, not coordinates: the page works on
cells and sends ids.
Tools: move (drag, wheel, pinch), paint and erase (drag), rectangle add / remove, a circle (centre, drag the radius, world units), a list
of areas (create, rename, delete, zoom to it), "plus neighbours within n cells", a live count, the heroes online as dots (from the
`world_events` script's `whereabouts` endpoint), hover shows the sector id, its name, the cell and the world position. Mouse and touch
(pointer events; two fingers pan and zoom), light and dark like the rest of the UI, no outside resources.
**Saving**: the page posts to `POST /api/mod-store` (admin, JSON only, not from another site, at most 512 KB):
`{"mod":"world_events","set":{"areas":{...}},"delete":[...]}` writes keys of that mod's storage on the game loop (storage limits apply,
413 beyond them) and bumps `{key}_rev`; `GET /api/mod-store?mod=...&key=...` reads one key. Keys are `a-z 0-9 _ . -`, per-hero data is out
of reach. `world_events.lua` reads the key `areas` (the same format as the `AREAS` table in the script file, ranges as `[a, b]`) every few
seconds and lets it win over the file; events use areas by id (`area = "silver_creek"`). The page also shows the config line to paste
into the script file ("Copy snippet"). The commands and forms of the web UI keep their 16 KB limit; only this endpoint takes more.

### Names (`Data/NameService.cs`)

Read once at startup from `--game-dir` with the Sacred.Assets readers (never in the game loop; lookups are dictionary reads):
`scripts/us/global.res` (else the first `scripts/<language>/global.res`) for the texts, `PAK/Items.pak` with its
`Items00..15.pak` overlays (by word +0x76, like GameServer.exe) and `PAK/Weapon.pak` (`SacredObjectTemplateArchive`) for the
type descriptors.

| id | source | example |
| --- | --- | --- |
| object type (creatures and items share the Items.pak index) | descriptor +0x20 `ResourceId`, resolved like Gold 0x44A200 (bit 31 clear: decimal key, set: hash) | 38 "Orc Warrior", 5171 "Lesser Healing Potion", 2501 "Longbow" |
| an object's own name | its name word (native +0x3C; base state +37 on the wire, +51 in saves) before the type's | generated items: "Decaying Bow of Oblivion" (type 2501) |
| skill slot id | text 9399 + id (as in the item requirement texts) | 2 "Weapon Lore", 20 "Vampirism", 29 "Weapon Technology" |
| combat art id (u16 at +4 of the 22-byte record) | cMagicEntry/cSMoveEntry name resources (`SacredCombatArtDefinition.GetNameResource`): spells 1.., special moves 1000.. | 60 "Blazing Disc", 1022 "Turn into Vampire" |

Checked against five real saves: every skill, art and item (252) resolves, and the names fit the heroes' classes. Items always
carry their own name word in the saves (a type's base name can be empty, e.g. 1966 Daemon greaves). **Unverified:** the name
word of creatures on the wire (named bosses and NPCs should carry it; the saves only show the horse, 551 with 0x227 = its type),
the language when the install has several (`us` is preferred), and that item 0x16F drops of `server.drop_item` show their
generated name (the server creates them with name word 0, so clients see the type's name).

**Item tokens and the strict item gate (`--auth-strict-items=off|log|on`, Closed Net, `--authoritative`).** `server.item_token(player, { data = hex export }
| { type = n, records = hex }, ttl_seconds = 120)` returns a token id (nil, err when the server does not keep this hero): a one-time permission for the
player's client to create exactly that item (type and value records), bound to the player's session, lobby account and character, expiring after
the ttl (1..600 s). The client attaches nothing: its creation (0x16F/0x16E) is matched by type and records. `server.claim_item(player, ref)` now also
consumes the matching token once the item is in the hero (true once); `server.revoke_item_token(id)` removes an unused one. Tokens live in the C# host
(`GameServerHost.ItemTokens.cs`), so they survive script reloads but not a server restart. With the gate on, an item the client creates that no token
and no baseline item of the hero covers is refused: no state in the server's world, so it is never saved. `log` counts and logs only (default off).
Not known yet, so the gate is opt-in: loot of creatures the client simulates, merchant purchases, crafting/socketing results, quest rewards and trades.

**Shared stash (Closed Net).** `scripts-examples/shared_stash.lua` keeps the stash of the Sacred Gear mod shared-stash per `player.account` on channel `shared-stash`, with the hero custody checks above (deposits count after the item left the hero and the hero was saved; withdrawals are removed after the item arrived and the hero was saved). Outside `--authoritative` it offers nothing. Hot reload: everything is stored at every change; the join times go through the unload hook (reason reload), tokens live in the C# host, in-flight checks are resumed by the clients' resends (same rid). Tunables (`max_items`, `max_data`, `verify_seconds`, `retry_seconds`, `token_seconds`, `rate_per_minute`, ...) are data: the script writes its defaults to the key `config` on the first run and the storage wins from then on (edit on the page `/mods/shared_stash/` or `POST /api/mod-store`; `config_rev` is polled every second, no reload needed). The page and `/mods/shared_stash/state` show totals (never account keys). Tests: `SharedStashScriptTests.cs`.

**Auction house in Closed Net.** `scripts-examples/auction_house.lua` follows the shared stash's gate. *Tokens:* before the market tells a client to create an item (a mailbox `collect` of a bought, expired or cancelled item) it mints a token for exactly that item (`server.item_token`, label `auction:<rid>`, `token_seconds`, default 120); a repeated rid reuses the token while it lives, a delivery that is released or abandoned (the player left) revokes it, and the arrival's `claim_item` consumes it. Where no token can be minted the delivery works as before. *Gold:* the client adds withdrawn gold itself (`add_gold`), and the server has no gold hook in the item gate, so this check lives in the script and is **not a general gold gate**: it notes the hero's gold (the server's own copy) at the withdrawal and the lowest value since (so gold spent before the report does not hide the increase) and counts the withdrawal as done once the gold rose by the amount (or stands at the old value plus the amount) and the hero save is confirmed. After `gold_timeout` (default 120 s) without it the hero is saved and the saved copy decides: an increase confirms, none credits the wallet back (the rid then answers `refunded`). Not covered: gold that is picked up or earned in the same moment looks like the withdrawal, and a client that adds the gold after the refund keeps both. *Rules as data:* the script's `config` only seeds the storage on the first run; durations, `fee_per_day`, `fee_min`, `sale_cut`, `max_per_seller`, `max_listings` (total cap), `allow_special`, `sweep_seconds` (expiry interval), `verify_seconds`, `token_seconds`, `gold_timeout` and the price limits live under the storage key `config`, polled through `config_rev` (applies within 2 s, no reload), edited on the web UI's **Auction** page (`/auction`) and shown on `/mods/auction_house/` and the JSON `/mods/auction_house/rules`. *Hot reload:* the market is stored at every change; the unload hook stores the gold watches (always) and, for a reload, the join times, token ids and prepared deposits; clients resend in-flight checks with the same rid; the item sale is claimed only after the hero save so a reload in between loses nothing. The info reply says `closed = true` when `server.authoritative(player)`. Tests: `AuctionHouseScriptTests.cs`.

## Flood protection (`--flood-protection`, on with `--authoritative`)

GameServer.exe has none, so plain relay games stay unlimited unless `FLOOD_PROTECTION=1` is set; `--authoritative` turns it on
(`FLOOD_PROTECTION=0` turns it off again). Code: `Session/FloodGuard.cs` (pure checks) and `Session/GameServerHost.Flood.cs`;
the hooks are one line each in connection set-up, `HandleCustomData` and `Dispatch`. No allocations per message.

| Limit | Default | Key |
| --- | --- | --- |
| received bytes (TinCat payloads as received) | 2 MB/s, burst 8 MB | `FLOOD_BYTES_PER_SECOND`, `FLOOD_BYTE_BURST` |
| messages | 600/s, burst 3000 | `FLOOD_MESSAGES_PER_SECOND`, `FLOOD_MESSAGE_BURST` |
| chat lines 0x003/0x004/0x005 (also count as messages) | 3/s, burst 8; excess is muted, never kicked | `FLOOD_CHAT_PER_SECOND`, `FLOOD_CHAT_BURST` |
| kick (reason 8) after dropped messages/frames | 500 within 10 s (0 = never) | `FLOOD_KICK_AFTER_DROPS`, `FLOOD_STRIKE_WINDOW` |
| free upload of world/hero states, banks, inventories before entering the world | 60 s after admission | `FLOOD_UPLOAD_GRACE` |
| open connections per IP address (players behind one NAT share it) | 8 (0 = unlimited) | `MAX_CONNECTIONS_PER_ADDRESS` |

All limits (and the on/off switch) can be changed while the server runs, from the web UI's "Flood protection" card or
`POST /api/flood {"text":"FLOOD_MESSAGES_PER_SECOND=150;FLOOD_CHAT_PER_SECOND=1"}` (`GET /api/flood` shows the limits and the
dropped/removed/refused counters). The change runs on the game loop like every admin command and is logged and shown in the live
log; nobody is kicked by it: lower rates apply to the next messages, a lower burst caps each player's remaining allowance at once,
a lower connection limit only refuses new connections. Changes are not written back to the config file.

`FloodTests` cover the buckets, mute vs. kick, the upload grace, the connection limit, relay mode staying unlimited and a zero
allocation check. The defaults are generous guesses; real sessions (sector owners stream many creature updates) should be
measured before tightening them.

## Authoritative mode (optional, `--authoritative`)

Off by default: normal relay games behave like the original. Code lives in `Auth/` and `Session/GameServerHost.Authoritative.cs`; the only
hooks in existing files are one filter call in `Route`, one save call in `RemovePlayer` and one pump call in the tick.

| Stage 1 | Behaviour |
| --- | --- |
| Difficulty | the difficulty bits (0x100..0x800) stay what the server was configured with; a host 0x01E cannot change them (`AUTH_LOCK_DIFFICULTY`) |
| Damage 0x17A | attacker must be the sender's own hero or an object it owns in the server's object world, damage is clamped to `AUTH_MAX_DAMAGE` (default 0xFFFF) and forwarded; `AUTH_STRICT_ATTACKERS` also rejects unknown attackers |
| Trade 0x198 | sender word must be the sender's slot (the original ignores a mismatch) |
| Items | item states (0x169/0x16B/0x16C/0x16E/0x16F with an item body) no legitimate item has are dropped (not stored, not relayed, never saved) and counted as violations (`AUTH_CHECK_ITEMS`, `Auth/ItemGuard.cs`): more socket records than the item's own value block has open sockets (slot types 1..4, at most 8); with `--game-dir` also an object type that is not an item type, an item whose own value block names another type than its type or its Weapon.pak base type, and socket records of non-item types. Every rule holds for all 1845 items of the 40 real hero files we checked (sockets included) |
| Hero level | a hero state above `AUTH_MAX_HERO_LEVEL` (default 216, the Sacred Gold cap; 0 = off) removes the player (reason 8) |
| Violations | counted per player, kick (reason 8) at `AUTH_MAX_VIOLATIONS` (default 25, 0 = never) |
| Anti-dupe saves | Closed Net hero uploaded to the lobby (message 18) when the player leaves (hard disconnects too), every `AUTH_SAVE_INTERVAL` s (default 120) and `AUTH_SAVE_DEBOUNCE_MS` after trades, pick-ups, equipment and inventory changes. The block is written by the server from its own view (below), never taken from the client |

### Hero saves (`Auth/Hero`, `Session/GameServerHost.HeroState.cs`)

The block the lobby delivered (0x132) is the base; `HeroWriter` replaces what the server can see and keeps every other byte:

| Chunk | Source |
| --- | --- |
| C7 hero | equipment from the hero's object states and 0x11E/0x11D; values: hit-point block, +0x08 block (0x16B full, or the compact fields of 0x169/0x16E/0x16F laid over it: experience, gold, level, skill points, attributes), combat arts (22-byte records), **combos (38-byte records, verbatim and in order)**, 9-byte records. Status modifiers, the 68-byte list and the hero attachment are file-only and stay as saved |
| C8 objects | one record per item named by the inventories, item bars or equipment, built from the server's item state: **all value records = item + one per socketed rune/gem, in socket order** (u16 count on the wire, u8 in the file: more than 255 is refused, never truncated), item data, weapon extension. File-only fields come from the saved record of the same item. Only items the object world says the player owns are saved. Saved creatures and items attached to the hero model stay unchanged |
| CA/CB inventories | latest 0x122 per inventory (id = slot, 17 = shared), then 0x13E pick / 0x13F place `[item ref][cell][inventory id]`. Cells hold only the item's anchor (word +4 = `01 \| w<<8 \| h<<16`); an item never seen in an inventory gets its footprint from the game's item tables (`--game-dir`: Weapon.pak row +0x1C/+0x1D via the Items.pak descriptor, see docs/protocol.md; matches every anchor cell of the test saves), 1x1 without them |
| C3 / preview | level word; the block preview is this chunk |
| CE | statistics extension: the 100 bytes after the 0x2670-byte primary block of 0x14F |
| Quest pairs (in CA) | the inventory stream's 8-byte `[key][word04]` records come with 0x122 and are kept current from 0x145 `[key][word04]` (`Auth/Hero/QuestRecords.cs`, `Session/GameServerHost.Quests.cs`): applied to the sender's and to every recipient's stream by the client's own rule (Sacred.exe 0x5D8280: refused when a pair has the same key or any pair has word04 0, else appended). These pairs are the only quest state in a hero file; quest journal, script variables and actual quests (0x1B8..0x1BE, 0x1C6/0x1C7, 0x1C9/0x1CA, 0x1D3..0x1D5) are world state and are not part of it |
| C4, CD | unchanged. CD (decoded CC) is not scripts but the **explored map**: `[0xB00BB00B][area count][0][0]`, per area `[i][0]` + 128-byte 32x32 reveal mask or `[i][0xDEADB00B]` (6051 areas in Gold). The client sets the mask bits from its own hero's tile steps (0x5997C0 -> 0x638E00 -> 0x622B10) and no message carries them, so the server cannot know them (`Auth/Hero/ExploredMap.cs`). C4 (start info) is client-only too |

`HeroWriterTests` round-trip the real saves under `SACRED_HERO_SAVES` (decoded chunks identical when nothing changed) and check
sockets, gold and combos; `QuestStateTests` check the CD layout, an identical decoded CD after a round trip and that a quest
pair changes exactly the expected inventory bytes; `AuthoritativeTests.ClosedNetHeroIsSavedFromTheServersOwnView` and
`QuestPairFromThePlayerIsSaved` run the whole flow against a fake lobby. Saves allocate a few buffers per save (rare event), not per packet.

Open: server-owned creature values (they live in the sector owner's 0x16E/0x16F object bodies, see docs/protocol.md), item/stat/gold plausibility checks, kill validation, Lua API.
