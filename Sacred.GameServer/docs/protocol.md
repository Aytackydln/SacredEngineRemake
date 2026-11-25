# GameServer protocol notes

Verified facts about the original GameServer.exe (Sacred Gold) as reproduced by `Sacred.GameServer`. Addresses refer to GameServer.exe unless noted. Captures were made with the real client against the original and against this server.

## Admission (LAN, Open Net, Closed Net)

Server to joining client, in this order:

1. `0x011` welcome, 6 bytes. Byte 0 holds flags: `0x01` = host, `0x08` = lobby connected (set at 0x4EB93D when [0xA841FC] is set and FUN_0048d100 is true). Open Net host: `09 01 00 00 00 00`.
2. `0x01B` roster: 17 records of 834 bytes (session name, then each player's FIRSTCONTACT data).
3. `0x043` time probe.
4. `0x153` reference grant: 3000 for the host, 1000 per joiner.
5. Exactly one `0x00B` world environment. Further `0x00B` follow every 30 s and after in-game sector entries.
6. `0x151` to the other players.

There is no `0x01E` at admission. `0x153` and `0x154` are also client requests.

FIRSTCONTACT name field (+0x8A) is `<account>.<character>`; the roster copies it unchanged. Byte 6 is the hero block (Closed Net slot), byte 7 the class.

## Session name

Case `0x022` at 0x4CAF67 accepts the rename only if the sender is the host, flags bit 0x08 is clear, it is not in game (+0x75) and the password is accepted (+0x73). The name is stored at +0x3A58. Otherwise the server answers `0x009` with value `0x64`. With a lobby, the host's rename is therefore refused before the `0x023` echo.

## Sectors and objects

- `0x148` sector entry (FUN_004cf0d0, 62 bytes). Word +0 is the sector the player now stands in: if it has no owner, ownership goes to this player (`0x020`). Word +2 is the sector it came from: if the player owned it, it gets `0x01F`, and ownership moves to the first remaining player standing in it (`0x020`), else the sector has no owner.
- Before answering `0x148`, FUN_004cd4e0 (called at 0x4CF281) streams every object of the entered sector through FUN_004cda80: `0x169` if the player knows the object, else `0x16F`. The player's own hero (ref = slot) is skipped.
- `0x16E` bodies carry the position inside: `[ref][type][0xFEEDF00D][ref][type][flags u32 @20][sector u16 @24][x u32 @26][y u32 @30]`. Sent back as `0x16F` with a 24-byte prefix `[ref][type][x][y][0][sector][0]`; the flags change from `0x201` to `0x400001` (`(flags & ~0x200) | 0x400000`).
- The sector owner (the client holding `0x020`) spawns and simulates the sector's creatures. Their level, HP and stats live inside those object bodies, computed from the owner's local balance tables and difficulty. No other message carries sector balance values.
- `0x14C` (case 0x4C70D7, 12 bytes) goes to the other players whose hero sector (FUN_00495f60/495dc0) equals the sender's.
- `0x17F` NW_FIGHT_ENEMY_SPOTTED (case 0x4C61C7, FUN_004ce530): pairs of `[creature ref u32][value u32]`. Per pair the server toggles fight flag `0x8000` at object +0x200, sends `0x119 [ref][flags]` to in-game players if it changed (FUN_004d3ea0 mode 2) and hands the creature to the spotter (FUN_004d6950). Consumed here until the server has real object state.
- `0x14F` (case 0x4C6030): a 9940-byte statistics block, copied into the cStatsManager slot (FUN_00477500/FUN_00477490) and nothing else. Clients send it at upload, at world start and before leaving.

## Lobby (Open and Closed Net)

- The only lobby sender is FUN_0048d380. Its callers send 12 login, 13 update, 14 logout, 45 player joined, 46 player left, 30 chat and 32 private chat (0x166-byte text body, from the console), and 55 MOTD request (after the lobby's public IP arrived, FUN_004ED7E0).
- The original registers its game in channel 5 only. The lobby connection idles without a time-out.
- Closed Net: the original never requests or uploads heroes. After the server's 45 `[perm][block]` it expects the lobby to push PublicData (11) `[perm i32][block u16][size i32][offset i32][length i32][data]` with the whole block. It forwards the block unchanged to the client as `0x132` (internal 0x29, case 0x4CB287). This server also asks with 19 `[perm][block][offset 0][length 0x20000]` if no push came within 2 s, and delivers `0x132` once per player and admission.
- Hero block: 556-byte preview (0xC3 chunk) | u32 zlib size | u32 file size | zlib(hero file).
- Message 18 upload `[perm][block][size][offset 0][length][data]` exists in `LobbyClient.UploadCharacter` but the original has no save path (not on leave, not periodically, not from the menu). Server-side saving belongs to authoritative mode: save on every disconnect (hard ones too), periodically and after trades and drops, so killing the client cannot restore an older hero (anti-dupe).

## Inventories and items (authoritative-mode findings)

- `0x122` (case 0x4C5D3E) `[u32 inventory id][inventory stream]`: accepted only for id = sender slot or 17 (shared stash), otherwise logged. The stream is the file layout (writer 0x5DDA30): 256 key words, 256 cells of 12 bytes, item bar, spell bar, active slots, owner, quests, scrolls.
- A cell holds only the item's anchor: `[ref][01 | width << 8 | height << 16][cached item type u16, retained u16]` (checked on real saves).
- `0x13F` place (case 0x4C69EE, FUN_004d37f0) and `0x13E` pick (case 0x4C6A07, FUN_004d3740): `[item ref][cell][inventory id]`. Place runs CHEATPROTECTION_ProcessSetItem on the server's item (FUN_0040f840), re-sends a changed item as `0x16A` (0x169 layout, mode 6) and places it with the footprint from the item tables (FUN_00404470, re-created in Auth/Hero/ItemFootprints.cs):
  - resource 0xFA7 is 2x2;
  - else the Items.pak type descriptor (0x80 bytes, indexed by the item's resource = object type word, below 0x7E60) has word +0x18 = Weapon.pak row index; the row (Weapon.pak: 0x100-byte header, count at +4, version byte +3 >= 8, rows of 0x102 bytes) gives width/height in bytes +0x1C/+0x1D when both are nonzero (the case for nearly every item);
  - else the descriptor's category byte +0x2E decides (jump table 0x4046E0): 5 = 1x4 for resources 0x6B3/0x6B4/0x6B5/0x6C8/0x6E3/0x6E6, else 1x3; 6, 13 = 2x3; 16 = 1x2; 17, 18, 21, 22, 23 = 2x2; 19, 24 = 2x1; anything else 1x1.
  - The descriptors are the server's loaded ones, not the raw file: Items.pak entries (0x100-byte header, 12-byte entries `[u32][u32 offset][u32 size]`) are taken by index when their category is nonzero (FUN_004048c0); FUN_00404aa0 sets categories of 0xFEC/0xFF2 (0x16) and 0xFE9..0xFEB (0x18), loads the overlays Items00..Items15.pak (Gold ships Items03.pak) into the resource named by descriptor +0x76, then sets 0xCA8 to 0x16; FUN_00406130 writes each Weapon.pak row's index into word +0x18 of the type at row +0x80 (0xFD5 gets category 6); then every row with a base type at row +0x24 copies the base's descriptor except +0x18 (0x401479).
  - Checked against every anchor cell of the C1/C6 chunks of real saves (Sacred.GameServer.Tests/ItemFootprintTests.cs).
- Item states: file record = 247-byte physical | 0xBAADC0DE | **u8** count | count x 130-byte value records | 130-byte item data | 0, 0, 0xBAADC0DE | [0xBAADF00D + 4 words]; wire = 121-byte physical | 0xBAADC0DE | **u16** count | records | item data | [+0x1E0][0xBAADC0DE] | [weapon]. The records are the item plus one per socketed rune/gem.
- Hero values: combat arts are 22-byte records, combos 38-byte records (both on the wire), status modifiers 28-byte records (file only). The compact value stream (0x169/0x16C/0x16E/0x16F) carries only some +0x08 fields; 0x16B carries the full block.
- Saved object lists can hold items in no slot: items attached to the hero model (base flag 0x400, +0x2C parent = hero).

## Quest state and chunk CD (authoritative-mode findings, Sacred.exe Gold)

- The hero file's loader (0x6058A0) handles chunks C0..CE only (C5/C9 are no-ops). None of them holds the quest journal, script variables or actual quests: 0x1B8..0x1BE, 0x1C6/0x1C7, 0x1C9/0x1CA and 0x1D3..0x1D5 are world (interpreter) state, not hero state.
- Chunk CD (decoded type CC) is the explored map, not scripts. Writer 0x605396..0x60545E: `[0xB00BB00B][u32 area count][0][0]`, then per area index i `[u32 i][u32 0][128-byte mask from area +0x180]` (0x622B80), or `[u32 i][0xDEADB00B]` when there is no area i; area 0's first dword is written as 0x20202020. Reader 0x606901 (save version >= 0x1A) copies each mask back (0x622BA0), which sets area flag 0x1000 and counts the area in cStatsManager (0x4249C0) when the mask is not empty. Real Gold saves: 6051 areas, area 0 absent, 822824 bytes.
- The mask is 32x32 bits at 2-unit resolution: 0x622B10 sets bit ((x - x0) / 2, (y - y0) / 2), called through 0x638E00 from 0x5997C0, which acts only for the local player's hero whenever its tile position changes and reveals the radius tables at 0x8915CC / 0x891328. The copy-out 0x622B80 is called only by the save writer and the map renderer (0x6CE109); the merge 0x622C10 has no caller; none of these addresses is stored as a constant. No message carries the masks, so a server cannot reproduce them.
- The hero's quest pairs are the inventory stream's 8-byte `[key][word04]` records (count at +4436). `0x145` `[u32 key][u32 word04]`: Sacred.exe 0x5D8280(inventory, key, word04, send) posts 0x145 when send is set, the inventory id is below 0x11 and the game is networked, then appends locally; the append is refused when an existing pair has the same key or an existing pair's word04 is 0 (0x5D8402..0x5D8411). Callers (0x4AAEFB scripts: the local player's inventory; 0x558267/0x55923C pick-ups: the picking hero's; 0x4868F6) pass send = 1. The receiver (0x7DE5F3) applies it to its own local player's inventory with send = 0. GameServer.exe forwards 0x145 in party mode (5) and keeps nothing.

## Sacred Bild UDP transport (SBT)

Host side of Sacred Bild's LAN transport, `--udp-port` (default 2105). Little-endian, magic `SBT` + type, datagrams at most 1200 bytes.

| Type | Size | Layout |
|---|---|---|
| HELLO `H` | 64 | +4 version 1, +8 client nonce, +12 u16 game TCP port, +16 cookie[16] |
| CHALLENGE `C` | 32 | +4 version, +8 nonce, +16 cookie |
| WELCOME `W` | 32 | +4 version, +8 nonce, +12 session id, +16 u64 token |
| REFUSED `N` | 16 | +12 reason: 1 off, 2 not listening, 3 full, 4 version |
| DATA `D`, KEEPALIVE `K`, CLOSE `X`, PUNCH `P` | 16 + n | +4 session id, +8 u64 token, then the KCP packet for DATA |

KCP: conv = session id, stream mode, MTU 1184, windows 128/256, nodelay(1, 10, 1, 1), no dead-link limit. Keep-alive every 1 s, every 200 ms while the peer is silent; dead after 45 s; resend at once when the peer is heard again after 300 ms of silence. Roaming: answer to the address of the last valid datagram. At most 64 sessions. The cookie is any keyed MAC (here HMAC-SHA256 over address, port, nonce and a 30 s period). TinCat frames go into KCP unchanged.

## Sending

All sending goes through `Net/IClientLink` (frame in/out, flush, close) and `IClientListener`. Sessions have an identity independent of the socket, so TCP and KCP links are interchangeable.

## Web admin (remake only, `Sacred.GameServer.Web`)

- Server chat uses the relayed player chat layout with the server as origin: 0x005 `[u32 0][UTF-16 text, max 0xFE bytes]` to everybody, 0x003 `[u32 target slot][text]` to one player, both with sender 0 and origin 0 (slot 0 is the session record of the 0x1B roster). Kick uses 0x157 `[reason]` to the player and 0x150 to the others (reason 8 when none is given); lock, pause, max players and flags reuse 0x019/0x01A, 0x134/0x135, 0x023 and 0x01E from the server. A banned address is answered with the FIRSTCONTACT rejection 3 (`LobbyRefused`).
- Not checked against a real client: how the stock client displays a chat line with origin 0.

## Opcode table (original dispatcher FUN_004C3F20)

Index table `0x4CC9C4` (opcode - 3), jump table `0x4CC6E0`, 185 distinct cases; 229 opcodes between 0x003 and 0x1D5 have their own case, the remaining 238 reach the default case (it only logs). Source of truth for the code: `Session/OpcodeTable.cs`; `OpcodeTableTests` fails when a native opcode has no routing entry. Classes: **Forward** (pure forward), **ForwardAfterState** (forwards after a world-state update that is still a TODO here), **Consumed** (own state only), **Answered** (explicit handler), **Internal** (lobby/menu thread commands, dropped on purpose), **Admission**, **NoOp**, **Todo** (network effect needs unmodelled state; dropped silently).

| Opcode | Case address | Class | Our handling |
|---|---|---|---|
| 0x003 | 0x4CA8AA | Answered | PrivateChat |
| 0x004 | 0x4CA9A5 | Answered | OnGroupMessage: relayed unchanged to the named player (slot, or lobby id above 16), origin = sender; the group members of the original (statistics manager) are TODO |
| 0x005 | 0x4CA771 | Answered | PublicChat |
| 0x006 | 0x4CAB98 | Consumed | clears bit (payload-1) of the sender's flag mask (+0x94), UI refresh only |
| 0x007 | 0x4CAACD | Consumed | sets bit (payload-1) of the sender's flag mask (+0x94), UI refresh only |
| 0x009 | 0x4CBD9D | Answered | explicit handler |
| 0x00A | 0x4CBDAF | Answered | explicit handler |
| 0x00B | 0x4CC5BD | NoOp | no-op case (does nothing) |
| 0x00F | 0x4CA1E1 | Answered | OnPeerReady |
| 0x012 | 0x4CA22E | Admission | handled before routing (admission, password) |
| 0x015 | 0x4C9ABD | Answered | upload begin: state only, data follows |
| 0x016 | 0x4C9ABD | Answered | upload begin: state only, data follows |
| 0x017 | 0x4C9F72 | Answered | WorldStart |
| 0x018 | 0x4C9F72 | Answered | WorldStart |
| 0x019 | 0x4CA469 | Answered | SetLock |
| 0x01A | 0x4CA3D9 | Answered | SetLock |
| 0x01B | 0x4CAC65 | Answered | OnPlayerData |
| 0x01C | 0x4CA4F9 | Answered | OnQuit |
| 0x01D | 0x4CAD99 | Answered | OnGameStart: host, not in game, campaign flag: sets flag 0x8000 and broadcasts 0x01E |
| 0x01E | 0x4CAC9F | Answered | SetGameFlags |
| 0x021 | 0x4CAE40 | Answered | OnConnectionSpeed |
| 0x022 | 0x4CAF67 | Answered | OnSessionName |
| 0x023 | 0x4CAE76 | Answered | SetMaxPlayers |
| 0x024 | 0x4CB06E | Consumed | lobby/menu bookkeeping, no network effect |
| 0x025 | 0x4CB1C1 | Consumed | lobby/menu bookkeeping, no network effect |
| 0x029 | 0x4CB287 | Consumed | FUN_004D3030, no network effect |
| 0x031 | 0x4CBC44 | Consumed | lobby/menu bookkeeping, no network effect |
| 0x032 | 0x4CB64A | Internal | menu/lobby thread: sends 0x32 (0x208 bytes) to the player with lobby id payload[0] |
| 0x034 | 0x4CBBC4 | Internal | lobby thread: removes the player with lobby id payload[0] (FUN_004D6AC0); a client must not kick others |
| 0x035 | 0x4CC53B | Internal | menu/lobby thread: sends to the player with lobby id payload[0] |
| 0x036 | 0x4CC53B | Internal | menu/lobby thread: sends to the player with lobby id payload[0] |
| 0x037 | 0x4CB901 | Internal | lobby thread: broadcasts service code 0x15 |
| 0x038 | 0x4CB78D | Consumed | lobby/menu bookkeeping, no network effect |
| 0x039 | 0x4CB901 | Internal | lobby thread: broadcasts service code 0x15 |
| 0x03A | 0x4CB9C6 | Consumed | lobby/menu bookkeeping, no network effect |
| 0x03B | 0x4CB899 | Internal | lobby thread: broadcasts service code 0x16 (0x410 bytes) |
| 0x03C | 0x4CBB69 | Consumed | lobby/menu bookkeeping, no network effect |
| 0x03D | 0x4CBA27 | Internal | menu/lobby thread: send to a player + broadcast |
| 0x03E | 0x4CBD00 | Consumed | lobby/menu bookkeeping, no network effect |
| 0x040 | 0x4CC3F1 | Internal | timer command: forwards 0x0C (32 bytes, mode 7) |
| 0x041 | 0x4CC447 | Answered | SendProbe |
| 0x042 | 0x4CC4A0 | Answered | no-op (state only in the original) |
| 0x043 | 0x4CC4B9 | Answered | AnswerProbe |
| 0x044 | 0x4CC4FD | Answered | ProbeResult |
| 0x045 | 0x4CC522 | Answered | ProbeResult |
| 0x046 | 0x4CC5BD | NoOp | no-op case (does nothing) |
| 0x048 | 0x4CC5BD | NoOp | no-op case (does nothing) |
| 0x101 | 0x4CBE5A | Internal | host menu command (FUN_004D3030), no network effect |
| 0x102 | 0x4CBE6A | Internal | host menu command: sends 0x197 to a player |
| 0x103 | 0x4CBDC1 | Consumed | world tick helper (jumps to 0x4C7202 tail), no network effect |
| 0x104 | 0x4CBEB1 | Internal | host menu command: forwards 0x169 (mode 4) |
| 0x105 | 0x4CC168 | Internal | host menu command: forwards 0x16A (mode 6) |
| 0x106 | 0x4CC286 | Internal | host menu command: forwards 0x167/0x16F (mode 6) |
| 0x107 | 0x4CC119 | Consumed | host menu command, no network effect |
| 0x108 | 0x4CC5BD | NoOp | no-op case (does nothing) |
| 0x109 | 0x4C4796 | Internal | menu/lobby thread command, never accepted from clients |
| 0x10A | 0x4C4668 | Internal | menu/lobby thread command, never accepted from clients |
| 0x10B | 0x4C4570 | Internal | menu/lobby thread command, never accepted from clients |
| 0x10C | 0x4C4740 | Internal | menu/lobby thread command, never accepted from clients |
| 0x10D | 0x4C7D8A | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x10E | 0x4C82B4 | Consumed | applied to the server's own state only |
| 0x10F | 0x4C82A2 | Consumed | applied to the server's own state only |
| 0x111 | 0x4C7881 | Forward | mode 3 SectorVisible |
| 0x112 | 0x4C7758 | Forward | mode 3 SectorVisible |
| 0x113 | 0x4C816A | Forward | mode 8 SectorAndObject |
| 0x114 | 0x4C8151 | ForwardAfterState | mode 6 ObjectVisible; the server-world update is TODO |
| 0x118 | 0x4C4750 | Internal | session end path: service code 0x68 + forward 0x08 |
| 0x119 | 0x4C567C | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x11A | 0x4C4D07 | Forward | mode 6 ObjectVisible |
| 0x11B | 0x4C4D07 | Forward | mode 6 ObjectVisible |
| 0x11C | 0x4C571C | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x11D | 0x4C55BD | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x11E | 0x4C54F9 | Forward | mode 8 SectorAndObject |
| 0x11F | 0x4CC3D5 | Forward | mode 8 SectorAndObject |
| 0x120 | 0x4C57AA | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x121 | 0x4C584E | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x122 | 0x4C5D3E | Consumed | applied to the server's own state only |
| 0x123 | 0x4C5DF0 | Consumed | applied to the server's own state only |
| 0x125 | 0x4CC582 | Forward | mode 2 InGame |
| 0x126 | 0x4C5904 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x127 | 0x4C59CB | ForwardAfterState | mode 6 ObjectVisible; the server-world update is TODO |
| 0x128 | 0x4C5A8B | ForwardAfterState | mode 5 Party; the server-world update is TODO |
| 0x129 | 0x4C5F60 | Consumed | applied to the server's own state only |
| 0x12A | 0x4C5FF2 | Consumed | applied to the server's own state only |
| 0x12B | 0x4C5EB4 | Consumed | applied to the server's own state only |
| 0x12C | 0x4C5F0A | Consumed | applied to the server's own state only |
| 0x12D | 0x4C58E8 | ForwardAfterState | mode 3 SectorVisible; the server-world update is TODO |
| 0x12E | 0x4C5C8A | ForwardAfterState | mode 6 ObjectVisible; the server-world update is TODO |
| 0x12F | 0x4C5B1A | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x130 | 0x4C5E2C | Consumed | applied to the server's own state only |
| 0x131 | 0x4C5E5A | Consumed | applied to the server's own state only |
| 0x134 | 0x4C4830 | Answered | Pause |
| 0x135 | 0x4C47AB | Answered | Pause |
| 0x138 | 0x4C6D0B | Forward | mode 1 Connected |
| 0x139 | 0x4C6C60 | Answered | OnStatisticsRequest: the request is echoed to the requester (origin = asked player); the 0x13A of 0x48 bytes from the statistics manager is TODO |
| 0x13B | 0x4C6DCE | Forward | mode 1 Connected |
| 0x13C | 0x4C67DB | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x13E | 0x4C6A07 | Consumed | applied to the server's own state only |
| 0x13F | 0x4C69EE | Consumed | SETITEM into inventory (FUN_004D37F0): own state; the cheat-protection correction (0x16A, mode 6) needs the object world |
| 0x140 | 0x4C67F2 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x141 | 0x4C6E88 | Forward | mode 8 SectorAndObject |
| 0x144 | 0x4C6970 | Consumed | applied to the server's own state only |
| 0x145 | 0x4C6827 | Forward | mode 5 Party |
| 0x146 | 0x4C5273 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x147 | 0x4C67F2 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x148 | 0x4C7202 | Answered | OnSectorEntry |
| 0x149 | 0x4CC582 | Forward | mode 2 InGame |
| 0x14A | 0x4CC582 | Forward | mode 2 InGame |
| 0x14B | 0x4C4D07 | Forward | mode 6 ObjectVisible |
| 0x14C | 0x4C70D7 | Answered | OnSectorMessage |
| 0x14D | 0x4CC582 | Forward | mode 2 InGame |
| 0x14E | 0x4CC582 | Forward | mode 2 InGame |
| 0x14F | 0x4C6030 | Consumed | applied to the server's own state only |
| 0x150 | 0x4CA4F9 | Answered | OnQuit |
| 0x151 | 0x4C4C6D | Forward | mode 1 Connected |
| 0x152 | 0x4C4A2C | Internal | menu/lobby thread command, never accepted from clients |
| 0x153 | 0x4C48B5 | Answered | OnReferenceRequest |
| 0x154 | 0x4C4B10 | Answered | OnTriggerRequest |
| 0x155 | 0x4C48C6 | Consumed | applied to the server's own state only |
| 0x156 | 0x4C4A5B | Answered | Kick |
| 0x157 | 0x4C4A5B | Answered | Kick |
| 0x15A | 0x4C4BD1 | Internal | menu/lobby thread command, never accepted from clients |
| 0x15B | 0x4C4A39 | Internal | menu/lobby thread command, never accepted from clients |
| 0x15C | 0x4C6BCC | ForwardAfterState | mode 3 SectorVisible; the server-world update is TODO |
| 0x15D | 0x4C6B2E | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x15E | 0x4C6A20 | Forward | mode 2 InGame |
| 0x160 | 0x4C6A20 | Forward | mode 2 InGame |
| 0x161 | 0x4C604B | Forward | mode 8 SectorAndObject |
| 0x162 | 0x4C615B | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x163 | 0x4C604B | Forward | mode 8 SectorAndObject |
| 0x164 | 0x4C60DA | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x165 | 0x4C5273 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x166 | 0x4C4D86 | Answered | WorldMessage/RemoveObject (original: object removal, leave of the owner) |
| 0x167 | 0x4C4D86 | Answered | WorldMessage/RemoveObject (original: object removal) |
| 0x168 | 0x4C4FE6 | Consumed | clears the sender's bit in the object visibility mask (+0x3A), no network effect |
| 0x169 | 0x4C4D07 | Answered | WorldMessage: store, then routed |
| 0x16B | 0x4C4D07 | Forward | mode 6 ObjectVisible |
| 0x16C | 0x4C4D07 | Answered | WorldMessage: store, then routed |
| 0x16D | 0x4C4D07 | Forward | mode 6 ObjectVisible |
| 0x16E | 0x4C4D07 | Answered | WorldMessage: store + stream |
| 0x16F | 0x4C4D07 | Answered | WorldMessage: store + stream |
| 0x170 | 0x4C5297 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x172 | 0x4C81F0 | Consumed | applied to the server's own state only |
| 0x173 | 0x4C538F | ForwardAfterState | mode 6 ObjectVisible; the server-world update is TODO |
| 0x174 | 0x4C5336 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x175 | 0x4C5407 | ForwardAfterState | mode 6 ObjectVisible; the server-world update is TODO |
| 0x176 | 0x4C547F | Consumed | applied to the server's own state only |
| 0x177 | 0x4C54DE | Forward | mode 6 ObjectVisible |
| 0x178 | 0x4C670B | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x179 | 0x4C5273 | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x17A | 0x4C61DA | ForwardAfterState | mode 6 ObjectVisible; the server-world update is TODO |
| 0x17B | 0x4C61AB | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x17C | 0x4C61AB | ForwardAfterState | mode 8 SectorAndObject; the server-world update is TODO |
| 0x17D | 0x4C647E | ForwardAfterState | kill report: applies native event 0x208, XP/loot (state not modelled), then forwards: mode 6 when the killer word is a player slot, else mode 8 |
| 0x17F | 0x4C61C7 | Consumed | applied to the server's own state only |
| 0x180 | 0x4C69C0 | Answered | OnDirectedRelay: unchanged to the slot in payload[0], origin = sender |
| 0x181 | 0x4CC582 | Forward | mode 2 InGame |
| 0x182 | 0x4CC582 | Forward | mode 2 InGame |
| 0x183 | 0x4C6996 | Forward | mode 8 SectorAndObject |
| 0x184 | 0x4CC582 | Forward | mode 2 InGame |
| 0x185 | 0x4C723B | Forward | mode 2 InGame |
| 0x186 | 0x4C7274 | Forward | mode 2 InGame |
| 0x187 | 0x4C72AD | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x188 | 0x4C72AD | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x189 | 0x4CC582 | Forward | mode 2 InGame |
| 0x18A | 0x4C7333 | Consumed | applied to the server's own state only |
| 0x18C | 0x4C7425 | Forward | mode 3 SectorVisible |
| 0x18D | 0x4C7600 | Forward | mode 3 SectorVisible |
| 0x18E | 0x4C9942 | Forward | mode 2 InGame |
| 0x18F | 0x4C9965 | Forward | mode 2 InGame |
| 0x190 | 0x4C9942 | Forward | mode 2 InGame |
| 0x191 | 0x4C9942 | Forward | mode 2 InGame |
| 0x192 | 0x4C9942 | Forward | mode 2 InGame |
| 0x193 | 0x4C9942 | Forward | mode 2 InGame |
| 0x194 | 0x4C9942 | Forward | mode 2 InGame |
| 0x195 | 0x4C9942 | Forward | mode 2 InGame |
| 0x196 | 0x4CC582 | Forward | mode 2 InGame |
| 0x197 | 0x4C82C9 | Consumed | applied to the server's own state only |
| 0x198 | 0x4C850E | Answered | OnTrade (Session/GameServerHost.Trade.cs): trade state machine; offer validation and the item exchange in the object world are TODO |
| 0x199 | 0x4C84F6 | Forward | mode 3 SectorVisible |
| 0x19A | 0x4CC5BD | NoOp | no-op case (does nothing) |
| 0x19B | 0x4C84DE | Forward | mode 3 SectorVisible |
| 0x19C | 0x4C99C0 | Consumed | applied to the server's own state only |
| 0x19D | 0x4C99C0 | Consumed | applied to the server's own state only |
| 0x1A4 | 0x4C95F6 | ForwardAfterState | mode 3 SectorVisible; the server-world update is TODO |
| 0x1A5 | 0x4C95F6 | ForwardAfterState | mode 3 SectorVisible; the server-world update is TODO |
| 0x1A6 | 0x4C96E4 | Forward | mode 6 ObjectVisible |
| 0x1A7 | 0x4CC5BD | NoOp | no-op case (does nothing) |
| 0x1A8 | 0x4CC5BD | NoOp | no-op case (does nothing) |
| 0x1A9 | 0x4C97FF | Forward | mode 6 ObjectVisible |
| 0x1AE | 0x4C79AE | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1AF | 0x4C7BB0 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1B0 | 0x4C7B08 | Forward | mode 2 InGame |
| 0x1B1 | 0x4C4CF4 | Answered | StoreWorldBank |
| 0x1B2 | 0x4C4CBB | Answered | StoreWorldBank |
| 0x1B3 | 0x4C4CE1 | Answered | StoreWorldBank |
| 0x1B4 | 0x4C4CCE | Answered | StoreWorldBank |
| 0x1B5 | 0x4C4C86 | Answered | SendWorldBanks |
| 0x1B6 | 0x4C7C77 | Forward | mode 2 InGame |
| 0x1B7 | 0x4C7C44 | Forward | mode 2 InGame |
| 0x1B8 | 0x4C7CC0 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1B9 | 0x4C7CA7 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1BA | 0x4C7CD9 | Forward | mode 2 InGame |
| 0x1BB | 0x4C7D3E | Forward | mode 2 InGame |
| 0x1BC | 0x4C7D0C | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1BD | 0x4C7D25 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1BE | 0x4C7D71 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1BF | 0x4C7DA0 | Forward | mode 2 InGame |
| 0x1C0 | 0x4C7DD3 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1C1 | 0x4C7E05 | Forward | mode 2 InGame |
| 0x1C2 | 0x4C7DEC | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1C3 | 0x4C7E3A | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1C4 | 0x4C7E50 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1C5 | 0x4C7EB5 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1C6 | 0x4C7E69 | Forward | mode 2 InGame |
| 0x1C7 | 0x4C7E9C | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1C8 | 0x4C7ECE | Forward | mode 2 InGame |
| 0x1C9 | 0x4C7F1A | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1CB | 0x4C7F01 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1CC | 0x4C7F50 | Forward | mode 2 InGame |
| 0x1CD | 0x4C7F37 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1CE | 0x4C7F83 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1CF | 0x4C811E | Forward | mode 2 InGame |
| 0x1D0 | 0x4C8105 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1D1 | 0x4C7F9C | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1D2 | 0x4C7FB5 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1D3 | 0x4C7FD0 | ForwardAfterState | mode 2 InGame; the server-world update is TODO |
| 0x1D4 | 0x4C8012 | Forward | mode 2 InGame |
| 0x1D5 | 0x4C8089 | Forward | mode 2 InGame |

Counts: 229 cases = 60 Forward, 57 ForwardAfterState (state part TODO), 47 Answered, 35 Consumed, 22 Internal, 7 NoOp, 0 Todo, 1 Admission. Parts of the Answered cases that need the original's object or statistics world are TODO inside their handlers (0x198 validation and exchange, 0x004 group, 0x139 answer 0x13A, 0x17D rewards, 0x01C/0x150 reason 7).

### 0x198 trade (case 0x4C850E)

Client message `[partner slot][sub command][...]`; the server answers with 0x198 `[other slot][code]` (origin = the other player). Per player: state (0 none, 1 invitation sent, 2 locked in, 3 trading, 4 offer rejected), partner, gold, offered references (0x23 words) and their item data (0x1AC bytes). A partner that does not exist, is not connected or not in the world ends the trade: the sender is told `[partner][0x31]` and the partner (if connected) gets the message with first word 0.

| Sub | Precondition | Effect |
|---|---|---|
| 0x20 invite | own 0; partner 0 | own 1; message forwarded to the partner with first word = sender. Partner not 0: `[partner][0x24]` back (busy) |
| 0x21 accept | partner 1 | both 3, offers cleared; both get `[other][0x25]` |
| 0x22 withdraw | own 1 | own reset; `[partner][0x31]` to the sender, message (first word 0, origin 0) to the partner |
| 0x23 decline | partner 1 | partner reset; the message (first word 0, origin 0) goes to partner and sender |
| 0x26 offer | own 3; partner 2/3/4 | offer stored; sender `[partner][0x2A]`, partner `[sender][0x2B]`, message forwarded (offer valid; with an invalid one the original sends 0x2C/0x2D and sets state 4 - needs the object world, TODO) |
| 0x27 gold | own 3; partner 2/3/4 | gold stored; partner 2 becomes 3 and the sender gets `[partner][0x2A]`; message forwarded |
| 0x28 lock in | own 3 | own 2; sender `[partner][0x29]`; forwarded; if both are 2: nothing offered -> both `[other][0x31]`, else both get `[other][0x2F][partner gold][own gold][partner item data]`; both trades reset |
| 0x2A unlock | own 2 | own 3; sender `[partner][0x2B]`; forwarded |
| 0x2E re-check | own 2/3/4 | state 4 -> 3 when the partner's offer is valid; sender `[partner][0x2B]`, partner `[sender][0x2A]` |
| 0x30 cancel | own 2/3/4 | both reset; both get `[other][0x31]` |

A leaving player (0x150/0x01C/any removal) ends the trade of its partner with `[leaver][0x31]` (origin = leaver) before its own state is cleared (case 0x4CA4F9).

## Custom net opcodes (Sacred Gear mod channels)

Two message types above the original's range (its dispatch tables end at 0x1D5; every other id reaches the default case that
only logs, so the original neither relays nor disconnects: checked with a Sacred Gear client sending 0x1E0 and a 4032 byte
0x1E1 message to GameServer.exe). Defaults 0x1E0 (control) and 0x1E1 (data); both are options of the server and announced.
Little endian payloads:

```
control 0x1E0   [u8 kind] ...
  1 HELLO      c->s  [u8 proto=1][u32 features][u32 mod-set hash]       features: 1 fragments, 2 JSON
  2 HELLO_ACK  s->c  [u8 proto][u32 features][u16 data opcode][u16 max fragment][u32 max message][u32 bytes/s][u8 max in flight]
                     features: 1 fragments, 2 JSON, 4 relay between clients, 8 server channels (scripts on)
  3 PEERS      s->c  [u16 mask]   bit n = slot n+1 said HELLO (re-sent to every capable player when it changes)
data 0x1E1      [u32 channel id = FNV-1a(name)][u8 flags][u8 target][u16 message id][bytes]
  flags   1 first fragment, 2 last fragment, 4 JSON payload (else raw bytes)
  target  0 server, 1 host, 0xFF everybody else, 0x10 + slot (0x11..0x20) one player
```

A client sends HELLO after the world started (before admission the server drops the connection like for any other opcode); the
server never sends 0x1E0/0x1E1 to a slot that did not. A message is cut into fragments of at most `max fragment` data bytes,
fragments of one message are in order and share the message id (per sender); a message needs both the first and the last flag
(a single wire message has both). The server relays fragments unchanged to the capable recipients with the real sender in the
header's origin (sender 0 = the server for its own messages), and reassembles only messages for the server. Limits (all
options): token bucket per session, messages in flight per sender, channels per client, message size, reassembly memory per
session and in total, fragment timeout. Over the KCP/SBT transport the frames are the same messages.

### Handshake on the chat carrier (net channels)

Channels also work without the custom types, on private chat (0x003) frames of at most 127 UTF-16 units that every server relays
(`Wire/NetChatWire.cs`): `[0xE1A0][0x4000+message number][0x4000+frame index]`, in the first frame `[0x4000+len>>15][0x4000+len&0x7FFF]`,
then the bytes packed 15 bits per unit (+0x4000); 228 bytes in the first frame, 232 in each further one; at most 64 KB per message.
A message is `[u8 kind][u8 name length][name][body]`: `n` bytes / `k` JSON for a channel; for the handshake (name `gear.net`):

```
h  c->s  [u8 proto]                      the client announces itself (on entering the game; repeated every 5 s up to 6 times)
q  s->c  [u8 proto][u16 control][u16 data]   the numbers the server offers (sent only to players that announced)
a  c->s  [u8 proto][u16 control][u16 data][u8 flags: 1 accepts]
```

Clients send to the server as private chat with target slot 0; the server sends from slot 0 (header origin 0), paced per player
(`NETOPS_CHAT_RATE`). After an accept with the server's own numbers the client sends HELLO (0x1E0 kind 1, above); the server
ignores a HELLO from a player that has not agreed, and writes nothing to a player that has not announced itself (plain clients would
show the frames as chat). The original GameServer.exe ignores the announcement. Frames between players are relayed unchanged by the
normal private chat path, except those of a `server_only` channel (the first frame names it; the rest of that message is dropped).

## Status messages to the game window (0x410)

Sacred.exe starts the server as `CreateProcess("GameServer.exe", "quiet <HWND>[ netlog][ tincatlog]")`; the number is the decimal handle of the game's main window (app object +0xB0). In "quiet" mode the server posts `PostMessageA(hwnd, 0x410, code, value)` to it (GameServer.exe FUN_004C27B0 / FUN_004C3F20); the game handles WM 0x410 in FUN_007EF640 (called from its window procedure at 0x813561): each code logs "Gameserver status: ..." and the error codes show a message. Implemented in `Platform/GameStatus.cs`, Windows only and only with a handle (a no-op elsewhere; blittable `DllImport`, AOT safe).

| Code | Value | Game's text | Sent by the original | Here |
|---|---|---|---|---|
| 1 | 0 | started | after start-up checks | at start-up |
| 2 | attempt 1..5 | testing port | before each try to open the network session | once (attempt 1) before the listener is opened |
| 3 | 0 | doing full reset | before the session loop restarts | not sent (no restart) |
| 4 | 0 | up and running | lobby login done / session ready | lobby accepted the game; without a lobby: listener open |
| 5 | 0 | graceful exit | normal shutdown | at the end of a normal shutdown |
| 6 | 0 | network failed | the original quits after losing the lobby | not sent (the lobby client keeps retrying) |
| 7 | 0 | init failed | the session could not be opened (port in use) | TCP port already in use (exit code 3) |
| 8 | 0 | could not connect to lobby server | lobby unreachable (then quits) | first failed lobby connect, once |
| 9 | error | lobby login failed, error #n | lobby login refused | lobby result code of the login |
| 10 | 0 | backconnect failed | the lobby could not reach the game's port | not sent (sacred-lobby has no such result) |
| 11 | 0 | wrong OS version | OS too old | never |
