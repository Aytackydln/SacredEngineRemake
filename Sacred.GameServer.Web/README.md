# GameServer web UI

A management page and JSON API for `SacredGameServer`, off by default.

```
SacredGameServer --webserver --webui-port=8080 --webui-password=secret --game-dir=/srv/sacred
```

Open `http://127.0.0.1:8080/`: overview, players (kick, ban), live chat, broadcast and private messages from the server,
session settings (lock, pause, max players, name, password, game mode and level before the world exists), end session,
shutdown, ban list. These are the controls of the original GameServer.exe window (its menu: network/lobby, game mode, level,
password, max players, lock) plus the chat and moderation the original never had.

## Configuration (`WebUi:*`)

.NET configuration, lowest to highest priority: environment variables (`SACRED_GS_WebUi__Port=8080`), JSON file (`webui.json`
in the working directory, or `--webui-config=path`), command line (`--WebUi:Port=8080`, `--WebUi:Port 8080`, `--webui-port=8080`).

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | start the UI (`--webserver`, or `--WebUi:Enabled=true`) |
| `Hostname` | `127.0.0.1` | bind address or name (`localhost`, a DNS name, `*`/`0.0.0.0` for all interfaces) |
| `Port` | `8080` | TCP port |
| `Password` | none | HTTP Basic (any user) or `Authorization: Bearer <password>`. **Required** when `Hostname` is not loopback |
| `CorsOrigins` | none | origins allowed to call the API from a browser; `*` allows all |
| `FrameAncestors` | none | who may embed the page in a frame: `self`, `*` or origins (CSP `frame-ancestors`; default `none`) |
| `AllowedHosts` | none | accepted `Host` headers; on password-less loopback only localhost/127.0.0.1/the hostname are accepted (DNS rebinding) |
| `PathBase` | none | URL prefix behind a reverse proxy |

To put the UI on another site, list that origin in `FrameAncestors` and frame the server. `CorsOrigins` is for scripts or pages that call the JSON API directly with `Authorization: Bearer`.

## Endpoints (minimal API, no controllers)

* `GET /` server rendered page; its forms post to `/actions/{say|kick|ban|unban|lock|pause|max-players|password|name|mode|end-session|shutdown}` and are answered with a redirect back (Post/Redirect/Get). Cross-site posts (`Sec-Fetch-Site`, foreign `Origin`) are refused.
* `GET /fragment` the server rendered state block (header, players, bans) the page swaps in every 3 s.
* `GET /api/chat/stream[?after=ID]` chat as Server-Sent Events (`id`, `event: chat`, JSON `data`; resumes with `Last-Event-ID`; keep-alive comment every 15 s). `GET /api/chat?after=ID` is the same data as one JSON page.
* `GET /api/flood` flood protection limits and counters; `POST /api/flood` with `text` = `KEY=VALUE` pairs (`;` separated, the `FLOOD_*` keys and `MAX_CONNECTIONS_PER_ADDRESS`) changes them live. The page's "Flood protection" card posts only the values that changed (`/actions/flood-form`).
* `GET /api/overview | players | bans` and `POST /api/{command}` with the same command names and a JSON body (`slot`, `value`, `text`, `reason`, `address`; needs `Content-Type: application/json`).

## Design

* `Sacred.GameServer.Web` only knows `IServerAdmin`; `GameServerHost` implements it (`Session/GameServerHost.Admin.cs`).
* The game loop stays the only writer: commands go through a bounded queue and run in the tick; reads come from a snapshot
  refreshed four times a second; chat goes into a preallocated ring of char buffers. No allocation on the packet path.
* Kestrel with `CreateSlimBuilder`, minimal API endpoints, source generated JSON: trim/AOT clean (no ILC warnings). The project trims only with Native AOT: ILLink-trimming a JIT publish breaks Kestrel (`TypeLoadException` on `X509Certificate2Collection`).
* One deployable unit: the UI is compiled into `SacredGameServer`; without `--webserver` Kestrel is never loaded.
* Bans are by IP address, kept in memory (they are lost on restart). A banned address is refused at FIRSTCONTACT (reason 3).
* Server messages use origin slot 0 like the server's own frames; how the stock client labels them is unverified.
