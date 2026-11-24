-- Hall of fame: all-time records (highest level, most creatures defeated, most deaths, longest session, most time online, the
-- mightiest creature defeated), the first hero to reach each level milestone and the first hero to defeat each kind of
-- creature, as a page and JSON endpoints. Own storage (script-data/hall_of_fame.json). Copy into the server's scripts folder.
--
--   /mods/hall_of_fame/            the page          /mods/hall_of_fame/records    JSON: the records
--   /mods/hall_of_fame/firsts      JSON: level milestones and first kills of each creature
-- "!hof" in chat shows the records to the player who asked. Records beaten by another hero are announced to everybody
-- (only above the minimums below, so a fresh server does not shout). Only character names are shown. The milestones, the minimums and
-- the list length are data in the mod's storage (key "config", live editable): see "configuration as data" and /mods/hall_of_fame/config.
server.describe("Hall of fame: all-time records and the first hero to reach each level or defeat each creature, as a page and JSON endpoints.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/hall_of_fame.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"hall_of_fame","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/hall_of_fame/config shows the active
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
    server.render(server.file("mod-config.html"), { mod = "hall_of_fame" }),
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"hall_of_fame\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "hall_of_fame configuration" })

local DEFAULTS = {
  milestones = { 10, 25, 50, 100, 150, 200 },                       -- levels announced as "first hero to reach"
  announce_min = { level = 30, kills = 500, deaths = 50, biggest = 40 },   -- records below these are kept, not announced (online and session are never announced)
  keep_first_kills = 60,                                            -- "first to defeat" entries kept (the page shows 20)
  announce = true,                                                  -- broadcast records and first-to-reach messages
}
local RULES = {
  milestones = r_list(r_num(1, 1000, true)), keep_first_kills = r_num(1, 1000, true), announce = r_bool,
  announce_min = function(v)
    if type(v) ~= "table" then error("needs a table like { level = 30, kills = 500 }", 0) end
    local out, check = {}, r_num(0, 1e12)
    for _, key in ipairs({ "level", "kills", "deaths", "biggest", "online", "session" }) do
      if v[key] ~= nil then out[key] = check(v[key]) end
    end
    return out
  end,
}
local milestones, announce_min = DEFAULTS.milestones, DEFAULTS.announce_min
config_start(DEFAULTS, RULES, function() milestones, announce_min = CONFIG.milestones, CONFIG.announce_min end)

local records = server.load("records", {})        -- key -> { name, uid, value, time, extra }
local level_firsts = server.load("level_firsts", {})   -- { { level, name, time }, ... }
local first_kills = server.load("first_kills", {})     -- newest first: { creature, name, time }
local seen = server.load("seen_creatures", {})         -- creature name -> true

local titles = {
  level = "Highest level", kills = "Most creatures defeated", deaths = "Most deaths", session = "Longest single session",
  online = "Most time online", biggest = "Mightiest creature defeated",
}
local order = { "level", "kills", "online", "session", "biggest", "deaths" }

local function who(player) return player.character or player.name end

-- A hero takes a record when its value is higher (the holder is kept on a tie). Returns true when the holder changed.
local function try_record(key, player, value, extra)
  local r = records[key]
  if value <= (r and r.value or 0) then return false end
  local changed = not r or r.uid ~= player.uid
  local previous = r
  records[key] = { name = who(player), uid = player.uid, value = value, time = server.time(), extra = extra }
  server.save("records", records)
  if CONFIG.announce and changed and previous and value >= (announce_min[key] or math.huge) then
    server.broadcast(who(player) .. " set a new Hall of Fame record: " .. titles[key]:lower() .. " " .. math.floor(value) ..
      (extra and (" (" .. extra .. ")") or "") .. " - beating " .. previous.name .. "!")
  end
  return changed
end

local function check_levels(player)
  if not player.level or player.level <= 0 then return end
  try_record("level", player, player.level)
  for _, m in ipairs(milestones) do
    if player.level >= m then
      local taken = false
      for _, f in ipairs(level_firsts) do if f.level == m then taken = true end end
      if not taken then
        level_firsts[#level_firsts + 1] = { level = m, name = who(player), time = server.time() }
        server.save("level_firsts", level_firsts)
        if CONFIG.announce then server.broadcast(who(player) .. " is the first hero on this server to reach level " .. m .. "!") end
      end
    end
  end
end

server.on("kill", function(player, monster)
  local kills = server.add(player, "kills")
  try_record("kills", player, kills)
  local creature = monster.name or ("creature type " .. monster.type)
  if (monster.level or 0) > 0 then try_record("biggest", player, monster.level, creature) end
  if not seen[creature] then
    seen[creature] = true
    table.insert(first_kills, 1, { creature = creature, name = who(player), time = server.time() })
    while #first_kills > CONFIG.keep_first_kills do table.remove(first_kills) end
    server.save("seen_creatures", seen); server.save("first_kills", first_kills)
  end
  check_levels(player)
end)

server.on("death", function(player)
  try_record("deaths", player, server.add(player, "deaths"))
end)

server.on("enter", check_levels)

server.on("leave", function(player)
  check_levels(player)
  local seconds = math.floor(player.online or 0)
  if seconds <= 0 then return end
  try_record("session", player, seconds)
  try_record("online", player, server.add(player, "online", seconds))
end)

-- Hot reload and shutdown: the records and lists are saved when they change; this makes sure of it.
server.on("unload", function(reason)
  pcall(server.save, "records", records); pcall(server.save, "level_firsts", level_firsts)
  pcall(server.save, "first_kills", first_kills); pcall(server.save, "seen_creatures", seen)
  config_unload(reason)
end)

-- ---- views -----------------------------------------------------------------------------------------------------------

local function stamp(t)   -- "2026-11-17" (UTC), from Unix seconds
  local z = math.floor(t / 86400) + 719468
  local era = math.floor(z / 146097)
  local doe = z - era * 146097
  local yoe = math.floor((doe - math.floor(doe / 1460) + math.floor(doe / 36524) - math.floor(doe / 146096)) / 365)
  local doy = doe - (365 * yoe + math.floor(yoe / 4) - math.floor(yoe / 100))
  local mp = math.floor((5 * doy + 2) / 153)
  local day = doy - math.floor((153 * mp + 2) / 5) + 1
  local month = mp < 10 and mp + 3 or mp - 9
  return string.format("%04d-%02d-%02d", yoe + era * 400 + (month <= 2 and 1 or 0), month, day)
end

local function shown(key, value)
  if key == "session" or key == "online" then
    local h, m = math.floor(value / 3600), math.floor(value / 60) % 60
    return string.format("%dh %02dm", h, m)
  end
  return tostring(math.floor(value))
end

local function record_rows()
  local rows = {}
  for _, key in ipairs(order) do
    local r = records[key]
    if r then rows[#rows + 1] = { key = key, title = titles[key], name = r.name, value = r.value, shown = shown(key, r.value), time = r.time, extra = r.extra } end
  end
  return rows
end

server.endpoint("records", function()
  local rows = {}
  for i, r in ipairs(record_rows()) do rows[i] = { record = r.key, title = r.title, hero = r.name, value = r.value, extra = r.extra, time = r.time } end
  return rows
end, { public = true, description = "the all-time records as JSON" })

server.endpoint("firsts", function()
  local levels, kills = {}, {}
  for i, f in ipairs(level_firsts) do levels[i] = { level = f.level, hero = f.name, time = f.time } end
  for i, f in ipairs(first_kills) do kills[i] = { creature = f.creature, hero = f.name, time = f.time } end
  return { levels = levels, creatures = kills }
end, { public = true, description = "first hero to reach each level milestone and to defeat each creature, as JSON" })

server.on("chat", function(player, text)
  if text ~= "!hof" then return end
  local rows = record_rows()
  if #rows == 0 then server.message(player, "The Hall of Fame is still empty.") end
  for _, r in ipairs(rows) do
    server.message(player, r.title .. ": " .. r.name .. " (" .. r.shown .. (r.extra and (", " .. r.extra) or "") .. ")")
  end
  return true
end)

-- The page head and its style are hall_of_fame.html next to this script (server.file).

server.page("", function()
  local h = server.html
  local parts = {
    server.file("hall_of_fame.html"),
    "<h1>Hall of fame</h1><main>",
    "<section><h2>Records</h2>",
  }
  local rows = record_rows()
  if #rows == 0 then parts[#parts + 1] = "<small>no records yet</small>" end
  for _, r in ipairs(rows) do
    parts[#parts + 1] = '<div class="rec"><span>' .. h(r.title) .. '</span><span><b>' .. h(r.name) .. "</b> " .. h(r.shown) ..
      (r.extra and (" <small>" .. h(r.extra) .. "</small>") or "") .. " <small>" .. stamp(r.time) .. "</small></span></div>"
  end
  parts[#parts + 1] = "</section><section><h2>First to reach a level</h2>"
  if #level_firsts == 0 then parts[#parts + 1] = "<small>nobody yet</small>" else
    parts[#parts + 1] = "<ul>"
    for _, f in ipairs(level_firsts) do parts[#parts + 1] = "<li>level " .. f.level .. ": <b>" .. h(f.name) .. "</b> <small>" .. stamp(f.time) .. "</small></li>" end
    parts[#parts + 1] = "</ul>"
  end
  parts[#parts + 1] = "</section><section><h2>First to defeat a creature</h2>"
  if #first_kills == 0 then parts[#parts + 1] = "<small>nobody yet</small>" else
    parts[#parts + 1] = "<ul>"
    for i, f in ipairs(first_kills) do
      if i > 20 then break end
      parts[#parts + 1] = "<li>" .. h(f.creature) .. ": <b>" .. h(f.name) .. "</b> <small>" .. stamp(f.time) .. "</small></li>"
    end
    parts[#parts + 1] = "</ul>"
  end
  parts[#parts + 1] = "</section></main>" .. config_footer() .. "</body></html>"
  return table.concat(parts)
end, { public = true, title = "Hall of fame" })
