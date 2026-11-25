-- Server stats dashboard: who is online, the peak, uptime, totals (joins, heroes, kills, deaths, chat lines, hours played), the
-- last 24 hours of players and activity, and the most defeated creatures, as a page and a JSON endpoint. Own storage
-- (script-data/server_stats.json), its settings (sample interval, history length, ...) too: see "configuration as data" and /mods/server_stats/config. Copy into the server's scripts folder to use it.
--
--   /mods/server_stats/          the dashboard (server-rendered, refreshes every 30 s, charts are inline SVG: no script)
--   /mods/server_stats/stats     the same numbers as JSON
-- With WebUi:PublicApi=true both also work under /public/mods/server_stats/... for anybody. Only character names are shown.
server.describe("Server stats: players online, peak, uptime, totals and 24 hour activity as a dashboard page and a JSON endpoint.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/server_stats.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"server_stats","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/server_stats/config shows the active
-- values, where each came from, when the mod was loaded, why, and the errors.
local CONFIG, CFG = {}, { loaded = server.time(), reason = "start", rev = 0, errors = {}, source = {} }
local CFG_DEFAULTS, CFG_RULES

local function cfg_copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, x in pairs(v) do out[k] = cfg_copy(x) end
  return out
end

local function cfg_log(text)
  if #CFG.errors < 20 then CFG.errors[#CFG.errors + 1] = text end
  server.log("config: " .. text)
end

-- Rules: a function that returns the cleaned value or raises error(text, 0).
local function r_num(lo, hi, whole)
  return function(v)
    if type(v) ~= "number" or v ~= v or v < lo or v > hi then error("needs a number from " .. lo .. " to " .. hi, 0) end
    return whole and math.floor(v) or v
  end
end
local function r_bool(v) if type(v) ~= "boolean" then error("needs true or false", 0) end return v end
local function r_text(v) if type(v) ~= "string" or v == "" or #v > 200 then error("needs a short text", 0) end return v end
-- A list whose items are cleaned by `item` (it returns the item or raises); refused items are logged, the others stay; fewer than
-- `min` valid items refuse the whole field.
local function r_list(item, min)
  return function(v)
    if type(v) ~= "table" then error("needs a list", 0) end
    local out = {}
    for i, x in ipairs(v) do
      local ok, clean = pcall(item, x)
      if ok and clean ~= nil then out[#out + 1] = clean else cfg_log("item #" .. i .. " refused: " .. tostring(ok and "invalid" or clean)) end
    end
    if #out < (min or 0) then error("needs at least " .. (min or 0) .. " valid item(s)", 0) end
    return out
  end
end

local function cfg_rev() local ok, v = pcall(server.load, "config_rev", 0); return ok and tonumber(v) or 0 end

-- Reads the storage over the defaults into CONFIG, field by field.
local function config_read()
  CFG.errors, CFG.source = {}, {}
  local ok, stored = pcall(server.load, "config", nil)
  if not ok or type(stored) ~= "table" then
    if ok and stored ~= nil then cfg_log("the storage key config is not a table: defaults used") end
    stored = {}
  end
  for key, rule in pairs(CFG_RULES) do
    local value
    if stored[key] ~= nil then
      local good, result = pcall(rule, stored[key])
      if good and result ~= nil then value, CFG.source[key] = result, "storage"
      else cfg_log(key .. ": " .. tostring(good and "invalid" or result) .. " - default kept") end
    end
    if value == nil then value, CFG.source[key] = cfg_copy(CFG_DEFAULTS[key]), "default" end
    CONFIG[key] = value
  end
  for key in pairs(stored) do if CFG_RULES[key] == nil then cfg_log("unknown field " .. tostring(key) .. " ignored") end end
  CFG.rev = cfg_rev()
end

-- First run only: the defaults become data in the storage (the admin edits them there; deleting a field restores its default).
local function config_seed()
  local ok, seeded = pcall(server.load, "config_seeded", false)
  if ok and seeded == true then return end
  local has, stored = pcall(server.load, "config", nil)
  if has and stored == nil then pcall(server.save, "config", cfg_copy(CFG_DEFAULTS)) end
  pcall(server.save, "config_seeded", true)
end

-- defaults, rules = { field = rule }, apply = called after every (re)read to rebuild what depends on CONFIG.
local function config_start(defaults, rules, apply)
  CFG_DEFAULTS, CFG_RULES = defaults, rules
  local ok, life = pcall(server.load, "_lifecycle", nil)
  if ok and type(life) == "table" then CFG.reason = life.reason == "reload" and "reload" or "restart" end
  pcall(server.delete, "_lifecycle")     -- a crash must not look like a reload next time
  config_seed()
  config_read()
  apply()
  server.every(2, function()
    if cfg_rev() == CFG.rev then return end
    config_read()
    CFG.applied = server.time()
    local good, err = pcall(apply)
    if not good then cfg_log("applying the new config failed: " .. tostring(err)) end
    server.log("config changed by an edit and applied")
  end)
end

-- Call from the mod's unload handler (after saving its state).
local function config_unload(reason)
  pcall(server.save, "_lifecycle", { reason = reason, at = server.time() })
  pcall(server.flush)
end

local function cfg_stamp(t)   -- "2026-11-17 09:30:12 UTC"
  t = math.floor(t)
  local z = math.floor(t / 86400) + 719468
  local era = math.floor(z / 146097)
  local doe = z - era * 146097
  local yoe = math.floor((doe - math.floor(doe / 1460) + math.floor(doe / 36524) - math.floor(doe / 146096)) / 365)
  local doy = doe - (365 * yoe + math.floor(yoe / 4) - math.floor(yoe / 100))
  local mp = math.floor((5 * doy + 2) / 153)
  local day = doy - math.floor((153 * mp + 2) / 5) + 1
  local month = mp < 10 and mp + 3 or mp - 9
  local secs = t % 86400
  return string.format("%04d-%02d-%02d %02d:%02d:%02d UTC", yoe + era * 400 + (month <= 2 and 1 or 0), month, day,
    math.floor(secs / 3600), math.floor(secs / 60) % 60, secs % 60)
end

local function cfg_from_storage()
  local n = 0
  for _, s in pairs(CFG.source) do if s == "storage" then n = n + 1 end end
  return n
end

-- A line for the foot of the mod's own pages.
local function config_footer()
  local n = cfg_from_storage()
  return "<p style='padding:0 1.2rem'><small>Configuration: " .. (n > 0 and "storage" or "default") .. " (" .. n .. " field(s) from the storage), loaded "
    .. server.html(cfg_stamp(CFG.loaded)) .. " (" .. server.html(CFG.reason) .. ")" .. (#CFG.errors > 0 and (", " .. #CFG.errors .. " config error(s)") or "") .. "</small></p>"
end

server.page("config", function()
  local h, rows, names = server.html, {}, {}
  for key in pairs(CFG_RULES) do names[#names + 1] = key end
  table.sort(names)
  for _, key in ipairs(names) do
    local value = CONFIG[key]
    local text = tostring(value)
    if type(value) == "table" then
      local ok, json = pcall(server.json, value)
      text = ok and json or "(table)"
    end
    if #text > 3000 then text = text:sub(1, 3000) .. " ..." end
    rows[#rows + 1] = "<tr><td>" .. h(key) .. "</td><td>" .. h(CFG.source[key] or "default") .. "</td><td><code>" .. h(text) .. "</code></td></tr>"
  end
  local errors = {}
  for _, e in ipairs(CFG.errors) do errors[#errors + 1] = "<li>" .. h(e) .. "</li>" end
  return table.concat({
    server.render(server.file("mod-config.html"), { mod = "server_stats" }),
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"server_stats\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "server_stats configuration" })

local DEFAULTS = {
  sample_seconds = 300,     -- one players-online sample every 5 minutes (applies on the next reload: a timer keeps its interval) ...
  keep_samples = 288,       -- ... for 24 hours
  keep_hours = 48,          -- hourly activity buckets kept (24 h are shown)
  top_creatures = 8,        -- rows of "most defeated creatures"
}
local RULES = {
  sample_seconds = r_num(10, 3600, true), keep_samples = r_num(2, 5000, true), keep_hours = r_num(24, 24 * 90, true), top_creatures = r_num(1, 50, true),
}
config_start(DEFAULTS, RULES, function() end)

-- "uptime" is the time since the server started this mod: a hot reload keeps it (the start time is stored), a restart begins anew.
local loaded = server.time()
if CFG.reason == "reload" then
  local ok, t = pcall(server.load, "up_since", nil)
  if ok and tonumber(t) then loaded = t end
end
server.save("up_since", loaded)
local state = server.load("state", nil) or { since = loaded, peak = 0, peak_time = 0 }
local samples = server.load("samples", {})      -- { { t, n }, ... } oldest first
local hours = server.load("hours", {})         -- { { h = hour number, kills, deaths, joins, chat }, ... } oldest first
local kinds = server.load("kinds", {})         -- creature name -> defeats
local dirty = false

local function total(name) return server.get(nil, "total." .. name, 0) end

local function bump(field)
  local h = math.floor(server.time() / 3600)
  local last = hours[#hours]
  if not last or last.h ~= h then
    last = { h = h, kills = 0, deaths = 0, joins = 0, chat = 0 }
    hours[#hours + 1] = last
    while #hours > CONFIG.keep_hours do table.remove(hours, 1) end
  end
  last[field] = (last[field] or 0) + 1
  dirty = true
end

local function note_peak()
  local n = #server.players()
  if n > state.peak then
    state.peak, state.peak_time = n, server.time()
    server.save("state", state)
  end
end

server.on("join", function(player)
  server.add(nil, "total.joins")
  if server.get(player, "seen", 0) == 0 then
    server.set(player, "seen", 1)
    server.add(nil, "total.heroes")
  end
  bump("joins")
  note_peak()
end)

server.on("kill", function(player, monster)
  server.add(nil, "total.kills")
  local creature = monster.name or ("creature type " .. monster.type)
  kinds[creature] = (kinds[creature] or 0) + 1
  bump("kills")
end)

server.on("death", function() server.add(nil, "total.deaths"); bump("deaths") end)
server.on("chat", function() server.add(nil, "total.chat"); bump("chat") end)   -- returns nothing: the line is relayed as usual

server.every(60, function()
  local n = #server.players()
  if n > 0 then server.add(nil, "total.online", 60 * n) end
end)

server.every(CONFIG.sample_seconds, function()     -- a timer's interval is read when the mod loads: a changed sample_seconds applies on the next reload
  samples[#samples + 1] = { t = math.floor(server.time()), n = #server.players() }
  while #samples > CONFIG.keep_samples do table.remove(samples, 1) end
  dirty = true
end)

server.every(30, function()
  if not dirty then return end
  dirty = false
  server.save("samples", samples); server.save("hours", hours); server.save("kinds", kinds)
end)

-- Hot reload and shutdown: what was only counted in memory (samples, hours, creatures, the peak) is saved now.
server.on("unload", function(reason)
  pcall(server.save, "state", state); pcall(server.save, "samples", samples); pcall(server.save, "hours", hours); pcall(server.save, "kinds", kinds)
  config_unload(reason)
end)

-- ---- numbers -------------------------------------------------------------------------------------------------------

local function duration(seconds)
  seconds = math.floor(seconds)
  local d, h, m = math.floor(seconds / 86400), math.floor(seconds / 3600) % 24, math.floor(seconds / 60) % 60
  if d > 0 then return string.format("%dd %dh", d, h) end
  if h > 0 then return string.format("%dh %dm", h, m) end
  return string.format("%dm", m)
end

local function stamp(t)   -- "2026-11-17 09:30" (UTC), from Unix seconds
  t = math.floor(t)
  local z = math.floor(t / 86400) + 719468
  local era = math.floor(z / 146097)
  local doe = z - era * 146097
  local yoe = math.floor((doe - math.floor(doe / 1460) + math.floor(doe / 36524) - math.floor(doe / 146096)) / 365)
  local doy = doe - (365 * yoe + math.floor(yoe / 4) - math.floor(yoe / 100))
  local mp = math.floor((5 * doy + 2) / 153)
  local day = doy - math.floor((153 * mp + 2) / 5) + 1
  local month = mp < 10 and mp + 3 or mp - 9
  local year = yoe + era * 400 + (month <= 2 and 1 or 0)
  local secs = t % 86400
  return string.format("%04d-%02d-%02d %02d:%02d", year, month, day, math.floor(secs / 3600), math.floor(secs / 60) % 60)
end

local function last_24h()
  local now_h, result = math.floor(server.time() / 3600), {}
  for i = 23, 0, -1 do
    local want, found = now_h - i, nil
    for _, b in ipairs(hours) do if b.h == want then found = b end end
    result[#result + 1] = { hour = want, kills = found and found.kills or 0, deaths = found and found.deaths or 0,
      joins = found and found.joins or 0, chat = found and found.chat or 0 }
  end
  return result
end

local function creatures()
  local list = {}
  for name, count in pairs(kinds) do list[#list + 1] = { creature = name, kills = count } end
  table.sort(list, function(a, b) if a.kills ~= b.kills then return a.kills > b.kills end return a.creature < b.creature end)
  while #list > CONFIG.top_creatures do table.remove(list) end
  return list
end

local function snapshot()
  local names = {}
  for _, p in ipairs(server.players()) do names[#names + 1] = p.character or p.name end
  table.sort(names)
  local recent = {}
  for _, s in ipairs(samples) do recent[#recent + 1] = { time = s.t, players = s.n } end
  return {
    online = #names, players = names, peak = state.peak, peak_time = state.peak_time,
    uptime = math.floor(server.time() - loaded), tracking_since = state.since,
    totals = { joins = total("joins"), heroes = total("heroes"), kills = total("kills"), deaths = total("deaths"),
      chat = total("chat"), hours_played = math.floor(total("online") / 36) / 100 },
    activity = last_24h(), samples = recent, creatures = creatures(),
  }
end

server.endpoint("stats", function() return snapshot() end, { public = true, description = "players online, peak, uptime, totals, 24 h activity as JSON" })

-- ---- the page --------------------------------------------------------------------------------------------------------

-- The page head and its style are server_stats.html next to this script (server.file).

local function tile(label, value) return '<div class="tile"><small>' .. server.html(label) .. "</small><b>" .. server.html(tostring(value)) .. "</b></div>" end

local function line_chart(values, w, h)
  local max = 1
  for _, v in ipairs(values) do if v > max then max = v end end
  if #values < 2 then return '<small>collecting data (one sample every 5 minutes)</small>' end
  local pts = {}
  for i, v in ipairs(values) do
    pts[#pts + 1] = string.format("%.1f,%.1f", (i - 1) / (#values - 1) * w, h - 4 - v / max * (h - 8))
  end
  local path = table.concat(pts, " ")
  return string.format('<svg viewBox="0 0 %d %d" role="img" aria-label="players online over 24 hours"><line class="axis" x1="0" y1="%d" x2="%d" y2="%d"/>' ..
    '<polygon class="area" points="0,%d %s %d,%d"/><polyline class="line" points="%s"/><text x="4" y="12" fill="#a39683" font-size="11">max %d</text></svg>',
    w, h, h, w, h, h, path, w, h, path, max)
end

local function bar_chart(buckets, field, w, h)
  local max = 1
  for _, b in ipairs(buckets) do if b[field] > max then max = b[field] end end
  local bw, parts = w / #buckets, {}
  parts[1] = string.format('<svg viewBox="0 0 %d %d" role="img" aria-label="%s per hour">', w, h, server.html(field))
  for i, b in ipairs(buckets) do
    local bh = b[field] / max * (h - 16)
    parts[#parts + 1] = string.format('<rect class="bar" x="%.1f" y="%.1f" width="%.1f" height="%.1f"><title>%s UTC: %d</title></rect>',
      (i - 1) * bw + 1, h - bh, bw - 2, bh, stamp(b.hour * 3600), b[field])
  end
  parts[#parts + 1] = string.format('<text x="4" y="12" fill="#a39683" font-size="11">max %d / hour</text></svg>', max)
  return table.concat(parts)
end

server.page("", function()
  local s, h = snapshot(), server.html
  local counts = {}
  for _, sample in ipairs(s.samples) do counts[#counts + 1] = sample.players end
  local who = {}
  for _, name in ipairs(s.players) do who[#who + 1] = "<li>" .. h(name) .. "</li>" end
  local top = {}
  for _, c in ipairs(s.creatures) do top[#top + 1] = "<li>" .. h(c.creature) .. " <small>" .. c.kills .. "</small></li>" end
  return table.concat({
    server.file("server_stats.html"),
    "<h1>Server stats</h1>",
    '<div class="tiles">', tile("Online now", s.online), tile("Peak", s.peak), tile("Uptime", duration(s.uptime)),
    tile("Heroes seen", s.totals.heroes), tile("Joins", s.totals.joins), tile("Creatures defeated", s.totals.kills),
    tile("Hero deaths", s.totals.deaths), tile("Chat lines", s.totals.chat), tile("Hours played", s.totals.hours_played), "</div>",
    '<div class="cards">',
    "<section><h2>Online now</h2>", #who > 0 and ("<ul>" .. table.concat(who) .. "</ul>") or "<small>nobody</small>", "</section>",
    "<section><h2>Players, last 24 hours</h2>", line_chart(counts, 400, 110), "</section>",
    "<section><h2>Creatures defeated per hour</h2>", bar_chart(s.activity, "kills", 400, 110), "</section>",
    "<section><h2>Hero deaths per hour</h2>", bar_chart(s.activity, "deaths", 400, 110), "</section>",
    "<section><h2>Most defeated creatures</h2>", #top > 0 and ("<ol>" .. table.concat(top) .. "</ol>") or "<small>nothing yet</small>", "</section>",
    "</div>",
    "<p style='padding:0 1.2rem'><small>Peak ", s.peak > 0 and (s.peak .. " on " .. h(stamp(s.peak_time)) .. " UTC") or "none yet",
    ". Counting since ", h(stamp(s.tracking_since)), " UTC.</small></p>",
    config_footer(), "</body></html>",
  })
end, { public = true, title = "Server stats" })
