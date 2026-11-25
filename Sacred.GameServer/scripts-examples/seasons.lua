-- Seasonal ladders, on top of the pattern of ladders.lua: the same kind of boards (creatures defeated, levels gained, time online)
-- but per season. When a season ends (after `length_days`, or by hand) its top heroes are archived and a fresh season starts
-- from zero. Own storage (script-data/seasons.json). Copy into the server's scripts folder to use it.
--
--   /mods/seasons/             the page: this season's standings, the time left and the archive of past seasons
--   /mods/seasons/current      JSON: this season          /mods/seasons/archive   JSON: past seasons, newest first
--   /mods/seasons/end-season   ADMIN only (needs the web password): ends the season now
-- "!season" in chat tells the player the season, the time left and its place. Only character names are shown.
-- Per-hero numbers of a season live under the keys "s<number>.kills" etc.; old seasons' keys stay in the mod's file (small).
server.describe("Seasonal ladders: kills, levels gained and time online per season; seasons end on a timer, are archived and restart from zero.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/seasons.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"seasons","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/seasons/config shows the active
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
    server.render(server.file("mod-config.html"), { mod = "seasons" }),
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"seasons\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "seasons configuration" })

-- Boards: id is what is counted (kills, gain = levels gained since the season began, online = seconds, shown divided by `scale`); the
-- title, scale and unit are only how it is shown. Take one out of the list to hide it.
local DEFAULTS = {
  length_days = 30,         -- 0: a season ends only by hand (/mods/seasons/end-season)
  keep_archive = 24,        -- past seasons kept
  top_n = 10,               -- heroes per board
  announce = true,          -- broadcast the end of a season
  boards = {
    { id = "kills", title = "Most creatures defeated", scale = 1, unit = "" },
    { id = "gain", title = "Most levels gained", scale = 1, unit = "" },
    { id = "online", title = "Longest online", scale = 3600, unit = " h" },
  },
}
local BOARD_IDS = { kills = true, gain = true, online = true }
local RULES = {
  length_days = r_num(0, 3650), keep_archive = r_num(1, 500, true), top_n = r_num(1, 100, true), announce = r_bool,
  boards = r_list(function(b)
    if type(b) ~= "table" or not BOARD_IDS[b.id] then error("a board's id is kills, gain or online", 0) end
    local scale = b.scale == nil and 1 or r_num(0.001, 1e9)(b.scale)
    return { id = b.id, title = r_text(b.title or b.id), scale = scale, unit = type(b.unit) == "string" and b.unit:sub(1, 20) or "" }
  end, 1),
}
local boards = DEFAULTS.boards
config_start(DEFAULTS, RULES, function() boards = CONFIG.boards end)

-- The first start of the season is kept in the storage right away: a reload (or a crash) must not begin the season anew.
local state = server.load("state", nil)
if type(state) ~= "table" then state = { number = 1, start = server.time() }; server.save("state", state) end
local archive = server.load("archive", {})      -- newest first: { number, start, finish, boards = { id = { { name, value } } } }

local end_season   -- defined below
local function key(name, number) return "s" .. (number or state.number) .. "." .. name end
local function finish_time() if CONFIG.length_days > 0 then return state.start + CONFIG.length_days * 86400 end end

-- A hero's level at its first sighting in the season is its base; "gain" is the best level minus the base.
local function touch(player)
  if not player.level or player.level <= 0 then return end
  local base = server.get(player, key("base"), 0)
  if base == 0 then server.set(player, key("base"), player.level); base = player.level end
  local gain = player.level - base
  if gain > server.get(player, key("gain"), 0) then server.set(player, key("gain"), gain) end
end

server.on("kill", function(player) server.add(player, key("kills")); touch(player) end)
server.on("enter", touch)
server.on("leave", touch)

server.every(60, function()
  for _, player in ipairs(server.players()) do server.add(player, key("online"), 60); touch(player) end
  local finish = finish_time()
  if finish and server.time() >= finish then end_season() end
end)

local function rows(board, number, n)
  local result = {}
  for _, row in ipairs(server.top(key(board.id, number), n or CONFIG.top_n)) do
    if row.value > 0 then result[#result + 1] = { rank = #result + 1, name = row.name, value = row.value / board.scale } end
  end
  return result
end

local function standings(number)
  local result = {}
  for _, board in ipairs(boards) do result[board.id] = rows(board, number) end
  return result
end

local function place(player)
  for i, row in ipairs(server.top(key("kills"), 1000)) do
    if row.name == (player.character or player.name) then return i, row.value end
  end
end

end_season = function()
  local now = server.time()
  local ended = { number = state.number, start = state.start, finish = now, boards = standings() }
  table.insert(archive, 1, ended)
  while #archive > CONFIG.keep_archive do table.remove(archive) end
  state = { number = state.number + 1, start = now }
  server.save("archive", archive); server.save("state", state)
  local champion = ended.boards.kills[1]
  if CONFIG.announce then server.broadcast("Season " .. ended.number .. " is over!" .. (champion and (" Most creatures defeated: " .. champion.name .. " (" .. math.floor(champion.value) .. ").") or "") ..
    " Season " .. state.number .. " has begun, everybody starts at zero.") end
  for _, player in ipairs(server.players()) do touch(player) end
end

server.on("chat", function(player, text)
  if text ~= "!season" then return end
  local finish = finish_time()
  local left = finish and math.max(0, math.floor((finish - server.time()) / 3600)) or nil
  local rank, kills = place(player)
  server.message(player, "Season " .. state.number .. (left and (": " .. math.floor(left / 24) .. " d " .. (left % 24) .. " h left.") or ": no end date.") ..
    (rank and (" You are number " .. rank .. " with " .. math.floor(kills) .. " creatures defeated.") or " You have not defeated anything yet."))
  return true
end)

-- Hot reload and shutdown: the season's number and start and the archive are in the storage (the per-hero counters are written as they
-- count), so the season carries on in the new version, boundary included.
server.on("unload", function(reason)
  pcall(server.save, "state", state); pcall(server.save, "archive", archive)
  config_unload(reason)
end)

-- ---- endpoints ---------------------------------------------------------------------------------------------------------

server.endpoint("current", function()
  return { season = state.number, start = state.start, finish = finish_time(), boards = standings() }
end, { public = true, description = "this season's standings as JSON" })

server.endpoint("archive", function()
  local list = {}
  for i, s in ipairs(archive) do list[i] = { season = s.number, start = s.start, finish = s.finish, boards = s.boards } end
  return list
end, { public = true, description = "past seasons with their final standings, newest first (JSON)" })

server.endpoint("end-season", function()
  local ended = state.number
  end_season()
  return { ended = ended, season = state.number }
end, { description = "admin: end the current season now and start the next" })

-- ---- the page ------------------------------------------------------------------------------------------------------------

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

-- The page head and its style are seasons.html next to this script (server.file).

local function board_card(board, list)
  local h = server.html
  if #list == 0 then return "<section><h2>" .. h(board.title) .. "</h2><small>nobody yet</small></section>" end
  local parts = { "<section><h2>", h(board.title), "</h2><ol>" }
  for _, r in ipairs(list) do
    local value = board.scale == 1 and tostring(math.floor(r.value)) or string.format("%.1f", r.value)
    parts[#parts + 1] = "<li><b>" .. h(r.name) .. "</b> " .. value .. h(board.unit) .. "</li>"
  end
  parts[#parts + 1] = "</ol></section>"
  return table.concat(parts)
end

local function cards(data)
  local parts = { "<main>" }
  for _, board in ipairs(boards) do parts[#parts + 1] = board_card(board, data[board.id] or {}) end
  parts[#parts + 1] = "</main>"
  return table.concat(parts)
end

server.page("", function()
  local finish = finish_time()
  local parts = {
    server.file("seasons.html"),
    "<h1>Season ", tostring(state.number), "</h1>",
    '<div class="sub">started ', stamp(state.start), " UTC", finish and (", ends " .. stamp(finish) .. " UTC") or ", no end date", "</div>",
    cards(standings()),
  }
  if #archive > 0 then
    parts[#parts + 1] = '<h1 style="font-size:1.1rem">Past seasons</h1><div class="past">'
    for _, s in ipairs(archive) do
      parts[#parts + 1] = "<details><summary>Season " .. s.number .. " <small>" .. stamp(s.start) .. " to " .. stamp(s.finish) .. "</small></summary>" ..
        cards(s.boards or {}) .. "</details>"
    end
    parts[#parts + 1] = "</div>"
  end
  parts[#parts + 1] = config_footer() .. "</body></html>"
  return table.concat(parts)
end, { public = true, title = "Seasonal ladders" })
