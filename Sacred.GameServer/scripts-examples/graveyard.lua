-- Graveyard: recent hero deaths with the name of the killer (creature or hero), the deadliest creatures and the heroes who died
-- most, as a page, JSON/CSV endpoints, an optional chat notice and a message to Sacred Gear clients (gear/examples/serverfeed).
-- Own storage (script-data/graveyard.json). Copy into the server's scripts folder to use it.
--
--   /mods/graveyard/        the page (tombstones, refreshes every 30 s)     /mods/graveyard/recent?n=20   JSON
--   /mods/graveyard/deadliest   JSON: creatures by heroes killed          /mods/graveyard/recent.csv   CSV
-- "!rip" in chat shows the last three deaths. Killer names are the game's own texts, so the server needs --game-dir for them
-- (otherwise "creature type N"). Only character names are shown. A hero death is reported once (several reports within 2 s count once).
server.describe("Graveyard: recent hero deaths with killer names, deadliest creatures and most deaths, as a page, JSON/CSV and a Gear client message.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/graveyard.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"graveyard","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/graveyard/config shows the active
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
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>graveyard configuration</title>',
    "<style>body{margin:0;font:14px/1.4 system-ui,sans-serif;background:#15110d;color:#efe6d6;padding:1rem 1.2rem}h1{font-size:1.2rem;color:#e0a944}",
    "table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #3b3025;padding:.3rem .5rem;text-align:left;vertical-align:top}",
    "code{white-space:pre-wrap;word-break:break-all}small{color:#a39683}.err{color:#ff9a8a}</style></head><body><h1>graveyard: configuration</h1>",
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"graveyard\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "graveyard configuration" })

local DEFAULTS = {
  keep = 200,               -- deaths kept
  announce = true,          -- "Alice was slain by Orc Warrior" to everybody
  page_stones = 48,         -- gravestones on the page
  page_killers = 8,         -- rows of the deadliest killers on the page
}
local RULES = { keep = r_num(1, 2000, true), announce = r_bool, page_stones = r_num(1, 500, true), page_killers = r_num(1, 100, true) }
config_start(DEFAULTS, RULES, function() end)

local deaths = server.load("deaths", {})         -- newest first: { time, name, level, by, kind, by_level }
local killers = server.load("killers", {})      -- killer name -> heroes killed

local channel = net and net.channel and net.channel("graveyard:feed", { server_only = true })

server.on("death", function(player, by)
  local who = player.character or player.name
  local killer, kind, killer_level
  if by.player then killer, kind = by.player.character or by.player.name, "hero"
  elseif by.name or by.type then killer, kind, killer_level = by.name or ("creature type " .. by.type), "creature", by.level
  else killer, kind = "unknown", "unknown" end
  local entry = { time = server.time(), name = who, level = player.level or 0, by = killer, kind = kind, by_level = killer_level }
  table.insert(deaths, 1, entry)
  while #deaths > CONFIG.keep do table.remove(deaths) end
  server.save("deaths", deaths)
  server.add(player, "deaths")
  if kind ~= "unknown" then
    killers[killer] = (killers[killer] or 0) + 1
    server.save("killers", killers)
  end
  if CONFIG.announce then server.broadcast(who .. (kind == "unknown" and " has died." or (" was slain by " .. killer .. "."))) end
  if channel then channel:send("all", { kind = "death", name = who, level = entry.level, by = killer, killer_kind = kind, time = entry.time }) end
end)

if channel then
  channel:on(function(player)
    local list = {}
    for i = 1, math.min(5, #deaths) do local d = deaths[i]; list[i] = { name = d.name, level = d.level, by = d.by, killer_kind = d.kind, time = d.time } end
    channel:send(player, { kind = "recent", deaths = list })
  end)
end

server.on("chat", function(player, text)
  if text ~= "!rip" then return end
  if #deaths == 0 then server.message(player, "Nobody has died yet.") end
  for i = 1, math.min(3, #deaths) do
    local d = deaths[i]
    server.message(player, d.name .. " (level " .. d.level .. ") - " .. (d.kind == "unknown" and "cause unknown" or ("slain by " .. d.by)))
  end
  return true
end)

local function recent(n)
  local list = {}
  for i = 1, math.min(n, #deaths) do
    local d = deaths[i]
    list[i] = { time = d.time, hero = d.name, level = d.level, killer = d.by, killer_kind = d.kind, killer_level = d.by_level }
  end
  return list
end

local function deadliest()
  local list = {}
  for name, count in pairs(killers) do list[#list + 1] = { killer = name, kills = count } end
  table.sort(list, function(a, b) if a.kills ~= b.kills then return a.kills > b.kills end return a.killer < b.killer end)
  return list
end

server.endpoint("recent", function(request) return recent(math.min(tonumber(request.query.n) or 20, CONFIG.keep)) end,
  { public = true, description = "the latest hero deaths as JSON: ?n=20" })
server.endpoint("recent.csv", function()
  return server.csv(recent(CONFIG.keep), { "time", "hero", "level", "killer", "killer_kind", "killer_level" }), "text/csv"
end, { public = true, description = "the latest hero deaths as CSV" })
server.endpoint("deadliest", function() return deadliest() end, { public = true, description = "killers by heroes killed, as JSON" })

-- Hot reload and shutdown: deaths and killers are saved on every death already; this is the safety net.
server.on("unload", function(reason)
  pcall(server.save, "deaths", deaths); pcall(server.save, "killers", killers)
  config_unload(reason)
end)

-- ---- the page ------------------------------------------------------------------------------------------------------------

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
  local secs = t % 86400
  return string.format("%04d-%02d-%02d %02d:%02d", yoe + era * 400 + (month <= 2 and 1 or 0), month, day, math.floor(secs / 3600), math.floor(secs / 60) % 60)
end

local style = [[
body{margin:0;font:15px/1.4 system-ui,sans-serif;background:#15110d;color:#efe6d6}h1{margin:0;padding:1rem 1.2rem;font-size:1.3rem;color:#e0a944}
.side{display:grid;grid-template-columns:repeat(auto-fill,minmax(16rem,1fr));gap:1rem;padding:0 1.2rem 1rem}
.stones{display:grid;grid-template-columns:repeat(auto-fill,minmax(12rem,1fr));gap:.8rem;padding:0 1.2rem 1.2rem}
.stone{background:#2b2a2c;border:1px solid #4a4a4e;border-radius:40% 40% 6px 6px/28% 28% 6px 6px;padding:1rem .8rem .7rem;text-align:center}
.stone b{display:block;color:#fff;font-size:1.05rem}.stone span{display:block;color:#cfc8bb}.stone small{color:#908a7e}
section{background:#241d16;border:1px solid #3b3025;border-radius:10px;padding:.8rem 1rem}h2{margin:0 0 .5rem;font-size:1rem;color:#f2c36b}
ol{margin:0;padding-left:1.3rem}small{color:#a39683}.rip{color:#8a8478;letter-spacing:.2em;font-size:.75rem}]]

server.page("", function()
  local h = server.html
  local parts = {
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">',
    '<meta http-equiv="refresh" content="30"><title>Graveyard</title><style>', style, "</style></head><body><h1>Graveyard</h1><div class=\"side\">",
    "<section><h2>Deadliest killers</h2>",
  }
  local top = deadliest()
  if #top == 0 then parts[#parts + 1] = "<small>nobody has died yet</small>" else
    parts[#parts + 1] = "<ol>"
    for i, k in ipairs(top) do
      if i > CONFIG.page_killers then break end
      parts[#parts + 1] = "<li>" .. h(k.killer) .. " <small>" .. k.kills .. " heroes</small></li>"
    end
    parts[#parts + 1] = "</ol>"
  end
  parts[#parts + 1] = "</section><section><h2>Most deaths</h2>"
  local most = server.top("deaths", 8)
  if #most == 0 then parts[#parts + 1] = "<small>nobody yet</small>" else
    parts[#parts + 1] = "<ol>"
    for _, r in ipairs(most) do parts[#parts + 1] = "<li>" .. h(r.name) .. " <small>" .. math.floor(r.value) .. "</small></li>" end
    parts[#parts + 1] = "</ol>"
  end
  parts[#parts + 1] = '</section></div><div class="stones">'
  for i, d in ipairs(deaths) do
    if i > CONFIG.page_stones then break end
    parts[#parts + 1] = '<div class="stone"><div class="rip">R.I.P.</div><b>' .. h(d.name) .. "</b><span>level " .. d.level .. "</span><span>" ..
      (d.kind == "unknown" and "cause unknown" or ("slain by " .. h(d.by) .. (d.by_level and d.by_level > 0 and (" (level " .. d.by_level .. ")") or ""))) ..
      "</span><small>" .. stamp(d.time) .. " UTC</small></div>"
  end
  if #deaths == 0 then parts[#parts + 1] = "<small>The graveyard is empty.</small>" end
  parts[#parts + 1] = "</div>" .. config_footer() .. "</body></html>"
  return table.concat(parts)
end, { public = true, title = "Graveyard" })
