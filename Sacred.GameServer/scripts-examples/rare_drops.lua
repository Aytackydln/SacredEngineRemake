-- Rare-drop feed: a live feed of rare and unique items that appear on the ground, with the creature that dropped them and the
-- hero who defeated it, as a page, JSON/CSV endpoints, a chat announcement and a message to Sacred Gear clients
-- (gear/examples/serverfeed shows it on screen). Own storage (script-data/rare_drops.json). Copy into the server's scripts folder.
--
--   /mods/rare_drops/        the feed page (refreshes every 15 s)     /mods/rare_drops/feed?n=20   JSON    /mods/rare_drops/feed.csv   CSV
--
-- The script API has no "item dropped" event of its own, so the server got one: server.on("drop", function(item, reporter)), fired
-- when a new item object lies on the ground (item = { ref, type, level, rarity, sockets, price, name, sector, x, y }). The game
-- sends no message that says who dropped it: the item is reported by the client that owns the sector. This mod therefore ties a
-- drop to the creature defeated in the last `kill_window` seconds (the most recent one) and ignores items without a kill
-- (chests, items already lying around when a client enters a sector) while `require_kill` is on.
-- The tunables (tier, kill window, feed length, ...) are data in this mod's storage (key "config", live editable): see "configuration
-- as data" and /mods/rare_drops/config.
-- Rarity is the item's tier 0..15 as Sacred Gear shows it: 0-4 normal, 5-11 magic, 12-14 rare, 15 unique.
server.describe("Live feed of rare and unique drops (item, tier, creature, hero) as a page, JSON/CSV, chat announcements and a Gear client message.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/rare_drops.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"rare_drops","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/rare_drops/config shows the active
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
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>rare_drops configuration</title>',
    "<style>body{margin:0;font:14px/1.4 system-ui,sans-serif;background:#15110d;color:#efe6d6;padding:1rem 1.2rem}h1{font-size:1.2rem;color:#e0a944}",
    "table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #3b3025;padding:.3rem .5rem;text-align:left;vertical-align:top}",
    "code{white-space:pre-wrap;word-break:break-all}small{color:#a39683}.err{color:#ff9a8a}</style></head><body><h1>rare_drops: configuration</h1>",
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"rare_drops\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "rare_drops configuration" })

local DEFAULTS = {
  min_rarity = 12,          -- announce items of this tier and above (12: rare and unique; 15: only unique)
  require_kill = true,      -- only drops that follow a kill within kill_window seconds
  kill_window = 5,
  announce = true,          -- chat line to everybody
  keep = 100,               -- drops kept
}
local RULES = {
  min_rarity = r_num(0, 15, true), require_kill = r_bool, kill_window = r_num(1, 120), announce = r_bool, keep = r_num(1, 1000, true),
}
config_start(DEFAULTS, RULES, function() end)

local feed = server.load("feed", {})            -- newest first
local kills = {}                                -- recent kills, newest last: { t, hero, creature, level }
local seen = {}                                 -- references already handled
local seen_order = {}

local function tier_name(r)
  if r >= 15 then return "unique" elseif r >= 12 then return "rare" elseif r >= 5 then return "magic" end
  return "normal"
end

server.on("kill", function(player, monster)
  kills[#kills + 1] = { t = server.time(), hero = player.character or player.name,
    creature = monster.name or ("creature type " .. monster.type), level = monster.level }
  while #kills > 20 do table.remove(kills, 1) end
end)

local function recent_kill()
  local now = server.time()
  for i = #kills, 1, -1 do
    if now - kills[i].t <= CONFIG.kill_window then return kills[i] end
  end
end

local channel = net and net.channel and net.channel("rare_drops:feed", { server_only = true })
local function push(payload)
  if channel then channel:send("all", payload) end
end

local function brief(d) return { item = d.item, tier = d.tier, rarity = d.rarity, by = d.by, from = d.from, time = d.time } end

server.on("drop", function(item, reporter)
  if (item.rarity or 0) < CONFIG.min_rarity then return end
  if seen[item.ref] then return end
  seen[item.ref] = true
  seen_order[#seen_order + 1] = item.ref
  if #seen_order > 500 then seen[table.remove(seen_order, 1)] = nil end

  local kill = recent_kill()
  if CONFIG.require_kill and not kill then return end
  local entry = {
    time = server.time(), item = item.name or server.item_name(item.type) or ("item type " .. item.type), type = item.type,
    level = item.level, rarity = item.rarity, tier = tier_name(item.rarity), sockets = item.sockets,
    by = kill and kill.hero or nil, from = kill and kill.creature or nil, from_level = kill and kill.level or nil,
  }
  table.insert(feed, 1, entry)
  while #feed > CONFIG.keep do table.remove(feed) end
  server.save("feed", feed)
  if kill then
    for _, p in ipairs(server.players()) do
      if (p.character or p.name) == kill.hero then server.add(p, "rare") end
    end
  end
  if CONFIG.announce then
    server.broadcast((entry.by and (entry.by .. " found a ") or "A ") .. entry.tier .. " " .. entry.item ..
      (entry.from and (", dropped by " .. entry.from) or "") .. "!")
  end
  push({ kind = "drop", drop = brief(entry) })
end)

if channel then
  -- A client asks for the latest drops when it connects.
  channel:on(function(player, payload)
    local list = {}
    for i = 1, math.min(5, #feed) do list[i] = brief(feed[i]) end
    channel:send(player, { kind = "recent", drops = list })
  end)
end

-- Hot reload and shutdown: the feed is saved on every drop already; this is the safety net.
server.on("unload", function(reason)
  pcall(server.save, "feed", feed)
  config_unload(reason)
end)

-- ---- endpoints ---------------------------------------------------------------------------------------------------------

local function rows(n)
  local list = {}
  for i = 1, math.min(n, #feed) do
    local d = feed[i]
    list[i] = { time = d.time, item = d.item, tier = d.tier, rarity = d.rarity, level = d.level, sockets = d.sockets, hero = d.by, creature = d.from }
  end
  return list
end

server.endpoint("feed", function(request) return rows(math.min(tonumber(request.query.n) or 20, CONFIG.keep)) end,
  { public = true, description = "the latest rare drops as JSON: ?n=20" })

server.endpoint("feed.csv", function()
  return server.csv(rows(CONFIG.keep), { "time", "item", "tier", "rarity", "level", "sockets", "hero", "creature" }), "text/csv"
end, { public = true, description = "the rare drops as CSV" })

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
main{display:grid;grid-template-columns:2fr 1fr;gap:1rem;padding:0 1.2rem 1.2rem}@media(max-width:700px){main{grid-template-columns:1fr}}
section{background:#241d16;border:1px solid #3b3025;border-radius:10px;padding:.8rem 1rem}h2{margin:0 0 .5rem;font-size:1rem;color:#f2c36b}
ol{margin:0;padding-left:1.3rem}.drop{margin:.4rem 0;padding:.4rem .6rem;border-left:4px solid #888;background:#1c1610}
.unique{border-color:#e07a2f}.rare{border-color:#e0c44a}.magic{border-color:#4a8fe0}b{color:#fff}small{color:#a39683}]]

server.page("", function()
  local h = server.html
  local parts = {
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">',
    '<meta http-equiv="refresh" content="15"><title>Rare drops</title><style>', style, "</style></head><body><h1>Rare drops</h1><main><section><h2>Latest</h2>",
  }
  if #feed == 0 then parts[#parts + 1] = "<small>nothing yet</small>" end
  for i, d in ipairs(feed) do
    if i > 40 then break end
    local cls = (d.tier == "unique" or d.tier == "rare" or d.tier == "magic") and d.tier or ""
    parts[#parts + 1] = '<div class="drop ' .. cls .. '"><b>' .. h(d.item) .. "</b> <small>" .. h(d.tier) .. (d.level and d.level > 0 and (", level " .. d.level) or "") ..
      ((d.sockets or 0) > 0 and (", " .. d.sockets .. " socket(s)") or "") .. "</small><br><small>" ..
      (d.by and ("found by " .. h(d.by)) or "found") .. (d.from and (" - dropped by " .. h(d.from)) or "") .. ", " .. stamp(d.time) .. " UTC</small></div>"
  end
  parts[#parts + 1] = "</section><section><h2>Luckiest heroes</h2>"
  local top = server.top("rare", 10)
  if #top == 0 then parts[#parts + 1] = "<small>nobody yet</small>" else
    parts[#parts + 1] = "<ol>"
    for _, r in ipairs(top) do parts[#parts + 1] = "<li><b>" .. h(r.name) .. "</b> " .. math.floor(r.value) .. "</li>" end
    parts[#parts + 1] = "</ol>"
  end
  parts[#parts + 1] = "</section></main>" .. config_footer() .. "</body></html>"
  return table.concat(parts)
end, { public = true, title = "Rare drop feed" })
