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
