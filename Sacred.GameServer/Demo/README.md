# Demo mode (`--demo`): stub state for screenshots

`SacredGameServer --demo` starts the server with **fake game state**, so every page of the web UI can be looked at and screenshotted without
a game, clients or game files. Loopback only: the game ports are ephemeral and bound to 127.0.0.1, LAN announce and the lobby are off, and the
web UI is forced onto 127.0.0.1 (default port 8080, or `--webui-port=18080`).

```
SacredGameServer --demo --webui-port=18080          # then open http://127.0.0.1:18080/
SacredGameServer --demo --demo-file=my-state.json   # your own stub state
SacredGameServer --demo --game-dir=D:\Games\Sacred  # same, with the real world map on /areas and /sectors
```

## What is stubbed, what is real

| Page | Source |
|---|---|
| Server (players, heroes, live chat, bans, name, uptime) | the stub state |
| Sectors (`/sectors`: owners, who holds a sector, positions) | the stub state; sector owners are given as grid cells |
| Areas (`/areas`) map | the real map with `--game-dir`, else a made-up island (about 1800 sectors, ids from 1000) |
| Mods and their pages, `/events`, `/auction`, mod storage | **real**: the scripts in `./scripts` and the data in `./script-data` of the working directory |
| Flood settings | the real ones |

Commands in the web UI (kick, ban, lock...) do nothing in demo mode; only "reload mods" is passed on.

## The stub state file

`--demo-file` replaces the built-in state completely; start from [`demo-state.json`](demo-state.json) (it is also the built-in default,
embedded into the program). Comments and trailing commas are allowed.

| Key | Meaning |
|---|---|
| `name`, `maxPlayers` | session name, 1..16 |
| `players[]` | in slot order 1..16: `name`, `class`, `level`, `xp`, `gold`, `address`, `latency`, `online` (seconds), and either `at: [cellX, cellY]` (a position inside the grid, fractions allowed: the dot is drawn exactly there) or `sector: [cellX, cellY]` (centre of that cell) |
| `chat[]` | `text`, `kind` (`public` default, `private`, `broadcast`, `server`, `system`), `from` (player slot), `to` (private) |
| `bans[]` | `address`, `reason`, `ago` (seconds) |
| `owners[]` | `slot`, `rect: [cellX, cellY, width, height]` (the sectors it owns), `also: [slots]` (other players holding them) |
| `unowned[]` | `cell: [x, y]`, `players: [slots]`: a sector held but owned by nobody |

Grid cells, not sector ids, because ids differ between map versions: the demo resolves cells with whichever map is in use.

## Mod pages with data

The mods bring their own pages; give them data by putting the scripts in `scripts/` and mod storage in `script-data/<mod>.json`.
[`seed_mod_data.py`](seed_mod_data.py) writes plausible storage for `server_stats`, `hall_of_fame`, `achievements`, `bounty_board`, `seasons`,
`rare_drops`, `graveyard`, `build_sharer` and `quest_helper` (names, 48 hours of activity, records, bounties...):

```
mkdir demo && cd demo
mkdir scripts && copy ..\Sacred.GameServer\scripts-examples\*.lua scripts
python ..\Sacred.GameServer\Demo\seed_mod_data.py script-data
SacredGameServer --demo --webui-port=18080
```

## Taking the screenshot

Use the built-in browser, or any headless browser against `http://127.0.0.1:18080/<page>`; the pages are server-rendered and need no
internet. `/sectors` and `/areas` draw on a canvas, so take them from a real browser rather than saving the HTML.
