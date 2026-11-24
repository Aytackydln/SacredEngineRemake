-- Quest helper: shows a hero's quest records with the hints and places this server's owner wrote for them, tells a player when a record
-- is new or changed, and shows which other heroes have the same record. A Sacred Gear panel (gear/examples/questhelper) shows it; chat
-- has "!quests"; a page lists the records on the server. Copy into the server's scripts folder to use it. Storage: script-data/quest_helper.json.
--
--   /mods/quest_helper/                the page: heroes online with their record counts, the most common records, the hints of the catalog
--   /mods/quest_helper/records         JSON: every record the server has seen, its hint and how many heroes have it
-- With WebUi:PublicApi=true the same addresses also work under /public/mods/quest_helper/... (character names only).
--
-- WHAT THE SERVER REALLY KNOWS (read this before expecting a quest log). A hero file carries exactly one kind of quest state: the
-- "quest pairs", 8-byte [key][value] records of the inventory stream that the game appends when a scripted event or a quest item
-- changes something (Sacred.exe 0x5D8280; see Sacred.GameServer README, "Quest pairs"). server.hero(player).quest_pairs gives them.
-- The journal (active quests, their text, the map markers) and the script variables of the quests are world state of the game's
-- interpreter: they are not in a hero file and no message carries them, so this server cannot list "active quests", "available
-- quests" or progress percentages, and nobody has mapped which key belongs to which quest. So this mod is a catalog framework:
--   * every record the hero has is listed (key and value), new and changed records are announced;
--   * a key that is in the CATALOG (below, or in the storage key "catalog" of this mod) gets a name, an area, a hint and value labels;
--   * every other key is shown as "record <key>". Fill the catalog as you learn what a key means.
-- The catalog and the tunables (poll interval, limits) are data in this mod's storage (key "config", live editable): see
-- "configuration as data" and /mods/quest_helper/config.
-- It needs hero data on the server (the web UI or --authoritative); without it the mod says so and shows nothing made up.

server.describe("Quest helper: a hero's quest records (the only quest state a hero file has) with catalog hints and areas, change notices, which heroes share a record; a Sacred Gear panel, chat and a page.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/quest_helper.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"quest_helper","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/quest_helper/config shows the active
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
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>quest_helper configuration</title>',
    "<style>body{margin:0;font:14px/1.4 system-ui,sans-serif;background:#15110d;color:#efe6d6;padding:1rem 1.2rem}h1{font-size:1.2rem;color:#e0a944}",
    "table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #3b3025;padding:.3rem .5rem;text-align:left;vertical-align:top}",
    "code{white-space:pre-wrap;word-break:break-all}small{color:#a39683}.err{color:#ff9a8a}</style></head><body><h1>quest_helper: configuration</h1>",
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"quest_helper\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "quest_helper configuration" })

local CHANNEL = "questhelper"

local function clean(text, max)
  if type(text) ~= "string" then return "" end
  text = string.gsub(text, "%c", " ")
  if #text > max then text = string.sub(text, 1, max) end
  return text
end

-- The catalog: record key -> { name, area, hint, states = { [value] = label } }. Empty on purpose: nothing here is guessed. It is
-- the "catalog" field of the config in the storage (JSON keys are strings), e.g.
--   "catalog": { "1234": { "name": "Example quest", "area": "Somewhere", "hint": "Talk to X.", "states": { "1": "started", "2": "done" } } }
-- (the earlier storage key "catalog" is still read and merged in). Texts are cut to a size: they are shown to players.
local function catalog_entry(entry)
  if type(entry) ~= "table" then error("an entry is a table", 0) end
  local states = {}
  if type(entry.states) == "table" then
    for value, label in pairs(entry.states) do states[tostring(value)] = clean(tostring(label), 60) end
  end
  return { name = clean(entry.name, 60), area = clean(entry.area, 60), hint = clean(entry.hint, 400), states = states }
end

local function catalog_of(map)
  if type(map) ~= "table" then error("needs a table of record key -> entry", 0) end
  local out, n = {}, 0
  for key, entry in pairs(map) do
    local ok, clean_entry = pcall(catalog_entry, entry)
    if ok and tonumber(key) and n < 2000 then out[tostring(math.floor(tonumber(key)))] = clean_entry; n = n + 1
    else cfg_log("catalog entry " .. tostring(key) .. " refused: " .. tostring(ok and "key must be a number (and at most 2000 entries)" or clean_entry)) end
  end
  return out
end

local DEFAULTS = {
  catalog = {},
  poll_seconds = 5,          -- how often the heroes' records are read (applies on the next reload: a timer keeps its interval)
  max_pairs = 120,           -- records sent to a client at once
  max_others = 6,            -- other heroes named per record
  max_known_keys = 400,      -- records the server remembers (a page lists 50)
  notice_seconds = 3,        -- at most one chat notice per hero in this time
  bucket_capacity = 6,       -- a client may send this many requests at once ...
  bucket_refill = 0.5,       -- ... and this many more per second
}
local RULES = {
  catalog = catalog_of, poll_seconds = r_num(1, 300), max_pairs = r_num(1, 1000, true), max_others = r_num(0, 50, true),
  max_known_keys = r_num(1, 5000, true), notice_seconds = r_num(0, 300), bucket_capacity = r_num(1, 100), bucket_refill = r_num(0.01, 100),
}

local catalog = {}
config_start(DEFAULTS, RULES, function()
  catalog = {}
  local stored = server.load("catalog", nil)             -- the earlier place of the owner's additions
  if type(stored) == "table" then
    for key, entry in pairs(catalog_of(stored)) do catalog[key] = entry end
  end
  for key, entry in pairs(CONFIG.catalog) do catalog[key] = entry end
end)

local function catalog_size()
  local n = 0
  for _ in pairs(catalog) do n = n + 1 end
  return n
end

local function key_text(key) return string.format("%d", key) end
local function title_of(key)
  local entry = catalog[key_text(key)]
  return entry and entry.name ~= "" and entry.name or ("record " .. key_text(key))
end

-- ---- what each online hero has: key -> value, read from server.hero ----

local current = {}        -- slot -> { [key] = value } (last poll)
local changed_at = {}     -- slot -> { [key] = server.time() } this session
local last_notice = {}    -- slot -> time
local buckets = {}

local function pairs_of(player)
  local hero, why = server.hero(player)
  if not hero then return nil, why end
  local map, order = {}, {}
  for _, pair in ipairs(hero.quest_pairs) do
    map[key_text(pair.key)] = pair.value
    order[#order + 1] = pair.key
  end
  return map, order, hero
end

-- Records of this server, with the heroes that have them, kept in this mod's storage so the page survives a restart:
-- known[key] = number of heroes that ever had it; per-hero values "q.<key>" are read with server.top.
local known = server.load("known", {})
local function remember(player, key, value)
  local k = key_text(key)
  if not known[k] then
    local total = 0
    for _ in pairs(known) do total = total + 1 end
    if total >= CONFIG.max_known_keys then return end
    known[k] = true
    server.save("known", known)
  end
  server.set(player, "q." .. k, value)
end

local function notice(player, text)
  local now = server.time()
  if last_notice[player.slot] and now - last_notice[player.slot] < CONFIG.notice_seconds then return end
  last_notice[player.slot] = now
  server.message(player, text)
end

-- The clients that asked for the state get it again when something changed.
local ch = net.channel(CHANNEL)
local watching = {}       -- slot -> true while the client's panel asked for updates

local function others_with(key, except_slot)
  local names = {}
  for slot, map in pairs(current) do
    if slot ~= except_slot and map[key_text(key)] ~= nil then
      local other = server.player(slot)
      if other then names[#names + 1] = other.character end
    end
  end
  table.sort(names)
  while #names > CONFIG.max_others do table.remove(names) end
  return names
end

local function state_for(player)
  local map, order, hero = pairs_of(player)
  if not map then
    return { op = "state", available = false, reason = "the server has no hero data of you yet (it keeps it with its web UI or --authoritative, once your client sent the hero)" }
  end
  local rows = {}
  for i = 1, math.min(#order, CONFIG.max_pairs) do
    local key = order[i]
    local value = map[key_text(key)]
    local entry = catalog[key_text(key)]
    rows[#rows + 1] = {
      key = key, value = value, name = entry and entry.name ~= "" and entry.name or nil, area = entry and entry.area ~= "" and entry.area or nil,
      hint = entry and entry.hint ~= "" and entry.hint or nil, label = entry and entry.states[key_text(value)] or nil, known = entry ~= nil,
      others = others_with(key, player.slot), changed = (changed_at[player.slot] or {})[key_text(key)],
    }
  end
  local known_count = 0
  for _, row in ipairs(rows) do if row.known then known_count = known_count + 1 end end
  return { op = "state", available = true, total = #order, known = known_count, records = rows, catalog = catalog_size(), time = server.time() }
end

local function push(player)
  if ch and net.supports(player) then ch:send(player, state_for(player)) end
end

-- ---- polling: new and changed records ----

local function poll(player)
  local map, order = pairs_of(player)
  if not map then return end
  local before = current[player.slot]
  current[player.slot] = map
  local times = changed_at[player.slot]
  if not times then times = {}; changed_at[player.slot] = times end
  local news, updates = {}, 0
  for _, key in ipairs(order) do
    local k = key_text(key)
    remember(player, key, map[k])
    if before and before[k] == nil then news[#news + 1] = key; times[k] = server.time()
    elseif before and before[k] ~= map[k] then updates = updates + 1; times[k] = server.time(); news[#news + 1] = key end
  end
  if before and #news > 0 then
    local first = news[1]
    local entry = catalog[key_text(first)]
    local extra = #news > 1 and (" (+" .. (#news - 1) .. " more)") or ""
    local line = "Quest record: " .. title_of(first) .. extra
    if entry and entry.hint ~= "" then line = line .. ". " .. entry.hint end
    notice(player, line)
    if watching[player.slot] then push(player) end
  elseif not before and watching[player.slot] then push(player) end
end

server.every(CONFIG.poll_seconds, function()     -- a timer's interval is read when the mod loads: a changed poll_seconds applies on the next reload
  for _, player in ipairs(server.players()) do
    if player.in_game then poll(player) end
  end
end)

server.on("enter", function(player)
  changed_at[player.slot] = nil; last_notice[player.slot] = nil; current[player.slot] = nil
  server.after(3, function()
    local p = server.player(player.slot)
    if p and p.uid == player.uid then poll(p) end
  end)
end)

server.on("leave", function(player)
  current[player.slot] = nil; changed_at[player.slot] = nil; last_notice[player.slot] = nil; watching[player.slot] = nil; buckets[player.slot] = nil
end)

-- ---- the client's requests ----

local function allowed(slot)
  local now = server.time()
  local bucket = buckets[slot]
  if not bucket then bucket = { tokens = CONFIG.bucket_capacity, at = now }; buckets[slot] = bucket end
  bucket.tokens = math.min(CONFIG.bucket_capacity, bucket.tokens + (now - bucket.at) * CONFIG.bucket_refill)
  bucket.at = now
  if bucket.tokens < 1 then return false end
  bucket.tokens = bucket.tokens - 1
  return true
end

if ch then
  ch:on(function(player, request)
    if type(request) ~= "table" or type(request.op) ~= "string" then return end
    if not allowed(player.slot) then ch:send(player, { op = "error", error = "too many requests" }); return end
    if request.op == "state" then
      poll(player)                         -- fresh before answering (the poll also seeds the baseline)
      watching[player.slot] = request.watch == true
      ch:send(player, state_for(player))
    elseif request.op == "unwatch" then
      watching[player.slot] = nil
    end
  end)
end

-- ---- hot reload: what the server holds only in memory about the heroes online ----
-- The baseline of each hero's records (so a change right before a reload is still announced after it), the times of the changes, who
-- is watching with the panel open and the rate-limit buckets. Saved by the unload handler under "_session" and read back after a reload
-- only (a restart starts clean: slots mean other heroes then); an entry counts only while the same hero (uid) is still in that slot.
server.on("unload", function(reason)
  pcall(server.save, "known", known)
  local session = {}
  if reason == "reload" then
    for _, player in ipairs(server.players()) do
      local slot = player.slot
      if current[slot] or watching[slot] then
        session[tostring(slot)] = { uid = player.uid, current = current[slot], changed = changed_at[slot], watching = watching[slot] or false,
          notice = last_notice[slot], bucket = buckets[slot] }
      end
    end
  end
  pcall(server.save, "_session", session)
  config_unload(reason)
end)

if CFG.reason == "reload" then
  local ok, session = pcall(server.load, "_session", nil)
  if ok and type(session) == "table" then
    for slot_text, entry in pairs(session) do
      local slot = tonumber(slot_text)
      local player = slot and server.player(slot)
      if player and type(entry) == "table" and player.uid == entry.uid then
        current[slot], changed_at[slot], watching[slot] = entry.current, entry.changed, entry.watching == true or nil
        last_notice[slot], buckets[slot] = entry.notice, entry.bucket
      end
    end
  end
end
pcall(server.delete, "_session")

-- ---- chat: !quests ----

server.on("chat", function(player, text)
  if text ~= "!quests" then return end
  local map, order = pairs_of(player)
  if not map then
    server.message(player, "Quest records: the server has no hero data of you yet.")
    return true
  end
  server.message(player, #order .. " quest record" .. (#order == 1 and "" or "s") .. ". Newest first:")
  for i = #order, math.max(1, #order - 4), -1 do
    local key = order[i]
    local entry = catalog[key_text(key)]
    local line = "  " .. title_of(key) .. " = " .. key_text(map[key_text(key)])
    if entry and entry.states[key_text(map[key_text(key)])] then line = line .. " (" .. entry.states[key_text(map[key_text(key)])] .. ")" end
    if entry and entry.area ~= "" then line = line .. " - " .. entry.area end
    server.message(player, line)
  end
  return true
end)

-- ---- the page and the records endpoint ----

local function record_rows()
  local rows = {}
  for k in pairs(known) do
    local heroes = server.top("q." .. k, 1000)
    local entry = catalog[k]
    rows[#rows + 1] = { key = tonumber(k), name = entry and entry.name ~= "" and entry.name or nil, area = entry and entry.area ~= "" and entry.area or nil,
      hint = entry and entry.hint ~= "" and entry.hint or nil, heroes = #heroes }
  end
  table.sort(rows, function(a, b) if a.heroes ~= b.heroes then return a.heroes > b.heroes end return a.key < b.key end)
  while #rows > 50 do table.remove(rows) end
  return rows
end

server.endpoint("records", function() return record_rows() end, { public = true, description = "quest records seen on this server with catalog name, area, hint and the number of heroes that have them" })

local style = [[
body{margin:0;font:15px/1.4 system-ui,sans-serif;background:#15110d;color:#efe6d6}h1{margin:0;padding:1rem 1.2rem;font-size:1.3rem;color:#e0a944}
main{display:grid;grid-template-columns:repeat(auto-fill,minmax(22rem,1fr));gap:1rem;padding:0 1.2rem 1.2rem}
section{background:#241d16;border:1px solid #3b3025;border-radius:10px;padding:.8rem 1rem}h2{margin:0 0 .5rem;font-size:1rem;color:#f2c36b}
ul{margin:0;padding-left:1.2rem}li{margin:.2rem 0}b{color:#fff}small{color:#a39683}]]

local function list_card(title, items, line, empty)
  local parts = { "<section><h2>", server.html(title), "</h2>" }
  if #items == 0 then parts[#parts + 1] = "<small>" .. server.html(empty) .. "</small>"
  else
    parts[#parts + 1] = "<ul>"
    for _, item in ipairs(items) do parts[#parts + 1] = "<li>" .. line(item) .. "</li>" end
    parts[#parts + 1] = "</ul>"
  end
  parts[#parts + 1] = "</section>"
  return table.concat(parts)
end

server.page("", function()
  local h = server.html
  local online = {}
  for _, player in ipairs(server.players()) do
    local map = current[player.slot]
    if map then
      local n = 0
      for _ in pairs(map) do n = n + 1 end
      online[#online + 1] = { name = player.character, level = player.level, records = n }
    end
  end
  table.sort(online, function(a, b) return a.name < b.name end)
  local rows = record_rows()
  local catalog_rows = {}
  for k, entry in pairs(catalog) do catalog_rows[#catalog_rows + 1] = { key = tonumber(k) or 0, entry = entry } end
  table.sort(catalog_rows, function(a, b) return a.key < b.key end)
  return table.concat({
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">',
    '<meta http-equiv="refresh" content="30"><title>Quest records</title><style>', style, '</style></head><body><h1>Quest records</h1><main>',
    list_card("Heroes online", online, function(o) return "<b>" .. h(o.name) .. "</b> <small>level " .. tostring(o.level) .. ", " .. tostring(o.records) .. " record(s)</small>" end,
      "nobody with hero data online"),
    list_card("Most common records", rows, function(r)
      return "<b>" .. h(r.name or ("record " .. string.format("%d", r.key))) .. "</b> <small>" .. tostring(r.heroes) .. " hero(es)" .. (r.area and (", " .. h(r.area)) or "") .. "</small>"
    end, "no records seen yet"),
    list_card("Catalog (hints)", catalog_rows, function(c)
      return "<b>" .. h(c.entry.name ~= "" and c.entry.name or ("record " .. string.format("%d", c.key))) .. "</b> <small>" .. h(c.entry.area) .. "</small><br>" .. h(c.entry.hint)
    end, "the catalog is empty: the server owner adds names and hints for record keys"),
    "</main>", config_footer(), "</body></html>",
  })
end, { public = true, title = "Quest records" })
