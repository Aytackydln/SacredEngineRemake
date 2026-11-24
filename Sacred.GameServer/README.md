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
All `Gameserver.cfg` keys can also be passed as `--key=value`.

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
