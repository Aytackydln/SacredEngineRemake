# Server mods (Lua) for Sacred.GameServer

Ready-made Lua scripts in `Sacred.GameServer/scripts-examples/`. Each one is a "mod": one `.lua` file with its own storage,
its own chat commands, and (most of them) a web page and JSON endpoints. Some also talk to the Sacred Gear client mods
(the `sacred-bild` repository, `gear/examples/`).

| Mod (script) | What it does | Page | Chat | Gear client mod | Doc |
|---|---|---|---|---|---|
| `bounty_board.lua` | A rotating board of "kill N creatures" bounties paid in gold and/or an item | `/mods/bounty_board/` | `!bounty` `!where` | `bounty` | [bounty-board.md](bounty-board.md) |
| `achievements.lua` | 31 achievements and a daily login streak, with rewards | `/mods/achievements/` | `!ach` `!streak` | `achievements` | [achievements.md](achievements.md) |
| `build_sharer.lua` | Players publish their hero build, everybody browses and searches them | `/mods/build_sharer/` | `!sharebuild` `!unsharebuild` `!builds` | `buildsharer` | [build-sharer.md](build-sharer.md) |
| `quest_helper.lua` | A hero's quest records with an owner-written catalog of hints | `/mods/quest_helper/` | `!quests` | `questhelper` | [quest-helper.md](quest-helper.md) |
| `rare_drops.lua` | Live feed of rare and unique drops | `/mods/rare_drops/` | | `serverfeed` | [feeds.md](feeds.md) |
| `graveyard.lua` | Recent hero deaths with the killer, deadliest creatures | `/mods/graveyard/` | `!rip` | `serverfeed` | [feeds.md](feeds.md) |
| `server_stats.lua` | Online players, peak, uptime, totals, 24 h charts | `/mods/server_stats/` | | | [statistics.md](statistics.md) |
| `hall_of_fame.lua` | All-time records, first to reach a level, first to defeat a creature | `/mods/hall_of_fame/` | `!hof` | | [statistics.md](statistics.md) |
| `seasons.lua` | Ladders per season with an archive | `/mods/seasons/` | `!season` | | [statistics.md](statistics.md) |
| `ladders.lua` | The original ladder example (kills, level, time online) | `/mods/ladders/` | `!top` | | [statistics.md](statistics.md) |

The client half of the Gear mods is documented in the client repository: `docs/GearServerMods.md` (section "Server mods
and their Gear client mods") and `docs/GearSessionMods.md`.

## Install (all of them)

1. Copy the `.lua` file(s) from `Sacred.GameServer/scripts-examples/` into the server's scripts folder (`--scripts-dir`,
   default `./scripts`). Scripts are loaded in name order at start and reloaded half a second after any file changes, so a
   running server needs no restart. `--scripts=0` turns all scripts off.
2. Adjust the settings if you like: they are **data in the mod's storage**, not code (see "Configuration, live edits and hot
   reload" below). The tables at the top of the file are only the defaults for the first run.
3. The mod's data goes to `script-data/<mod>.json` (folder: `--scripts-data`). Delete the file to reset a mod; keep it when you
   move the server. Limits: 8 MB per mod, 32 MB for all (`--scripts-store-max`, `--scripts-store-total-max`).

## Configuration, live edits and hot reload

Every mod is data driven and can be reloaded while the server runs. The pattern is the same for all of them:

* **Defaults in the script, truth in the storage.** The table at the top of each script (`DEFAULTS`: the bounty `pool`, the
  achievement list, the season length, the build caps, the quest `catalog`, ...) is copied once into the mod's storage as the key
  `config` (`script-data/<mod>.json`) the first time the mod runs (marked by `config_seeded`). From then on the storage decides;
  editing the defaults in the script file does not change a server that has already run (delete the `config` key, or single
  fields inside it, to get the defaults back: a missing or invalid field always falls back to its default).
* **Edit it live.** As admin, `GET /api/mod-store?mod=<mod>&key=config` reads it and
  `POST /api/mod-store` with `{"mod":"seasons","set":{"config":{"length_days":14,"top_n":20}}}` writes it (JSON,
  `Content-Type: application/json`; write the whole `config` table, fields you leave out use their defaults). The write bumps
  `config_rev`; the mod looks at it every 2 seconds and applies the change at once: no reload, no restart. Two fields are
  timer intervals (`quest_helper.poll_seconds`, `server_stats.sample_seconds`): a timer keeps the interval it was started with,
  so they apply on the next reload.
* **Validated, never fatal.** Each field has a type and a range (a whole number from 1 to 20, a list of valid entries, ...).
  A field that does not validate is ignored, its default stays and the reason is logged and shown on the overview page; the
  other fields still apply. A list (bounty pool, achievements, boards, catalog) drops its bad entries one by one and falls
  back to the default list only when none is valid. A `config` that is not a table at all is ignored.
* **Overview.** `/mods/<mod>/config` (admin only, never public) shows the active value of every field and whether it comes from
  the `storage` or is the `default`, the revision, when the mod was loaded, why (`start` = the server started the mod for the
  first time since a crash or an old install, `restart` = it was stopped with the server before, `reload` = hot reload), the
  time of the last live edit and the list of config errors. Each mod's own page ends with a line "Configuration: ..." with
  the same summary.
* **Hot reload.** Reload one mod with the Reload button of the Mods tab, `POST /api/reload-mods` with `{"text":"seasons"}`
  (empty text = all mods) or just save the `.lua` file (the watcher reloads that file half a second later). Every mod saves what
  it holds only in memory in an `unload` handler (`server.on("unload", fn(reason))`, reason `"reload"` or `"shutdown"`) and reads
  it back from the storage when it loads, so nothing is lost or counted twice: bounties in progress and the board, the
  achievement roster and the queue of rewards still waiting for a hero, the season's start and archive, statistics samples and
  uptime, the hall of fame, feeds, quest baselines of heroes who are online, and the publish cooldowns and request budgets of
  the build sharer. Timers and handlers are dropped and registered again by the new version, so nothing runs twice.
  A restart is the same thing, with the reason `shutdown`.

Fields of each mod are listed in its document below (the first table of its "Settings" section).

## Web pages

Pages and endpoints need the web UI (`--webserver=1`, port `--webui-port`, address `--webui-hostname`, default loopback; a
password is required when it is not bound to loopback). They live under `/mods/<mod>/...` (with the admin password, if one is
set). With `WebUi:PublicApi=true` (`--webui-public-api`) the same pages and JSON/CSV endpoints are also served to anybody under
`/public/mods/<mod>/...` (read-only, CORS `*`). Only **character names** are shown: no account names, slots or player ids.
Every player-made text passes `server.html` (escaped). Pages are sent with a sandboxing Content-Security-Policy, so a page
can run its own inline script but cannot load anything from elsewhere. Admin-only endpoints (for example `seasons/end-season`)
are never public.

The server lists every mod page and endpoint in the "Mods" card of the web UI and under `GET /api/mods` (`/public/mods` for
the public ones).

## Other things the mods need

| Need | Mods | Why |
|---|---|---|
| `--game-dir` (the game's folder, for names and item rewards) | `bounty_board` (`names` filters, item rewards), `achievements` (item rewards), `graveyard` (killer names), `rare_drops` (item names), `build_sharer` (skill and item names) | Without it creatures and items have no names: use `types` (bounty board) or expect "creature type N" |
| Hero data on the server (`server.hero`): web UI running, or `--authoritative` | `build_sharer`, `quest_helper` | They read skills, equipment and quest pairs from the server's own copy of the hero |
| Custom net messages (`NETOPS`, on by default) and a Sacred Gear client | the Gear client mods | The original `GameServer.exe` never answers them. Without them each mod still works through chat and the web page |

## Screenshots

The screenshots in [img/](img/) were taken from the real pages served by a built server (loopback only) with **made-up demo
data** (fictional hero names such as Thornwick and Lyra Dawnbrook, invented numbers): the mods' storage files were written
by a script, no real player or game session is shown. "Online now: 0" is real: nobody was connected. The Gear client panels
need the game itself and are **not** pictured anywhere; their layout is described in the client repository's docs.

## Known limits (all mods)

* No script event exists for gold, exploration, quests, items found or level changes, so the mods that would like them read
  what exists (player level at kills, joins and timers). See each mod's page for what it does not cover.
* Rewards are dropped next to the hero with `server.drop_gold` / `server.drop_item`. Unverified with a real client: that the
  owner of the sector can pick up what is dropped (see the server README).
* The mods are examples with simple storage (JSON files, no moderation tools besides editing the file). Edit the Lua file to
  change the rules.
