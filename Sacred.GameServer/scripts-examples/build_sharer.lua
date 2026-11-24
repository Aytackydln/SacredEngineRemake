-- Build sharer: players publish their hero build (class, level, skills, equipment) to this server, everybody can browse and search the
-- shared builds on a web page, and Sacred Gear clients can list and fetch them in a panel (the mod gear/examples/buildsharer).
-- Copy into the server's scripts folder to use it. Builds live in this mod's storage (script-data/build_sharer.json).
--
--   /mods/build_sharer/                      the page: every build as a card, with a live filter (text, class); ?q=orc&class=3 filters on the server too
--   /mods/build_sharer/?id=b3                one build in full
--   /mods/build_sharer/builds?q=&class=&sort=new|level&page=1&n=20   JSON list      /mods/build_sharer/build?id=b3   JSON, one build
-- With WebUi:PublicApi=true the same addresses also work under /public/mods/build_sharer/... (character names only: no account, no ids).
-- In chat: "!sharebuild [title]" publishes your build, "!unsharebuild" removes it, "!builds" counts them.
--
-- The caps and limits (builds kept, text lengths, page size, publish cooldown, request rate) are data in this mod's storage (key "config",
-- live editable): see "configuration as data" and /mods/build_sharer/config.
--
-- Where the data comes from. With server.hero (the server keeps hero state with the web UI or --authoritative) the SERVER fills class,
-- level, skills, combat arts and equipment from its own view of the hero: a client cannot make those up ("verified"). Without hero data
-- the build only has class and level as the server knows them plus what the client says about itself (a title, a note, tags and the items
-- it carries): those are shown as "reported". Whatever a client sends is length limited, type checked and HTML-escaped when shown.

server.describe("Build sharer: players publish their hero build (class, level, skills, equipment) and everybody browses and searches them, on a page and in a Sacred Gear panel.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/build_sharer.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"build_sharer","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/build_sharer/config shows the active
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
    server.render(server.file("mod-config.html"), { mod = "build_sharer" }),
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"build_sharer\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "build_sharer configuration" })

local CHANNEL = "buildsharer"
-- The caps are data (config fields, live editable). What a client sends is cut to them; builds already stored keep what they have until
-- they are published again or the mod is reloaded (the reload cuts them to the caps).
local DEFAULTS = {
  max_builds = 200,          -- builds kept (one per hero); more are refused until somebody removes theirs
  max_title = 40, max_note = 240, max_tag = 16, max_tags = 5, max_carried = 12, max_item = 48,
  max_skills = 8, max_arts = 40, max_equipment = 19,
  max_query = 40, max_page_size = 25, default_page_size = 12,
  publish_cooldown = tonumber(server.load("publish_cooldown", 15)) or 15,   -- seconds between two publishes of one hero (the earlier storage key "publish_cooldown" is the default)
  bucket_capacity = 8,       -- requests a client may burst ...
  bucket_refill = 0.5,       -- ... and per second after that
}
local RULES = {
  max_builds = r_num(1, 100000, true), max_title = r_num(0, 200, true), max_note = r_num(0, 2000, true), max_tag = r_num(1, 100, true),
  max_tags = r_num(0, 20, true), max_carried = r_num(0, 100, true), max_item = r_num(1, 200, true), max_skills = r_num(0, 100, true),
  max_arts = r_num(0, 200, true), max_equipment = r_num(0, 19, true), max_query = r_num(1, 200, true), max_page_size = r_num(1, 200, true),
  default_page_size = r_num(1, 200, true), publish_cooldown = r_num(0, 86400), bucket_capacity = r_num(1, 100), bucket_refill = r_num(0.01, 100),
}
config_start(DEFAULTS, RULES, function() end)

-- The hero type numbers of Sacred Gear's gear.world.CLASSES (the FIRSTCONTACT class byte is passed as is: unverified against a real client).
local CLASS_NAMES = { [1] = "Seraphim", [2] = "Gladiator", [3] = "Battle Mage", [4] = "Dark Elf", [5] = "Wood Elf", [6] = "Vampiress", [8] = "Dwarf", [9] = "Daemon" }
local CLASS_IDS = {}
for id, name in pairs(CLASS_NAMES) do CLASS_IDS[string.lower(name)] = id; CLASS_IDS[string.lower((string.gsub(name, " ", "")))] = id end

local function class_name(id) return CLASS_NAMES[id] or ("Class " .. tostring(id)) end
-- Item tiers 0..15 as Sacred Gear reads them (set items cannot be told apart by the tier).
local function tier_name(r) if r >= 15 then return "unique" elseif r >= 12 then return "rare" elseif r >= 5 then return "magic" else return "normal" end end

-- ---- cleaning what clients send ----

local function clean(text, max)
  if type(text) ~= "string" then return "" end
  text = string.gsub(text, "%c", " ")
  text = string.gsub(text, "^%s+", "")
  text = string.gsub(text, "%s+$", "")
  if #text > max then text = string.sub(text, 1, max) end
  return text
end

local function whole(value, low, high, default)
  if type(value) ~= "number" or value ~= value then return default end
  value = math.floor(value)
  if value < low then return low elseif value > high then return high end
  return value
end

local function list_of(value, limit)
  local result = {}
  if type(value) == "table" then
    for i = 1, math.min(#value, limit) do result[#result + 1] = value[i] end
  end
  return result
end

-- ---- storage: one value per build ("build:<id>"), an index in memory ----

local builds, by_owner, search_text, count = {}, {}, {}, 0
local last_publish, buckets = {}, {}    -- rate limits: kept over a reload by the unload handler

local function build_key(id) return "build:" .. id end

local function search_of(b)
  local parts = { b.name, b.title, b.note, class_name(b.class), table.concat(b.tags, " ") }
  for _, s in ipairs(b.skills) do parts[#parts + 1] = s.name end
  for _, e in ipairs(b.equipment) do parts[#parts + 1] = e.name end
  for _, c in ipairs(b.carried) do parts[#parts + 1] = c end
  return string.lower(table.concat(parts, " "))
end

local function index(b)
  if not builds[b.id] then count = count + 1 end
  builds[b.id] = b; by_owner[b.owner] = b.id; search_text[b.id] = search_of(b)
end

local function unindex(b)
  builds[b.id] = nil; by_owner[b.owner] = nil; search_text[b.id] = nil; count = count - 1
end

for _, key in ipairs(server.keys("build:")) do
  local b = server.load(key)
  -- a damaged or hand-edited value is ignored, never trusted
  if type(b) == "table" and type(b.id) == "string" and type(b.owner) == "string" and type(b.name) == "string" then
    b.tags = list_of(b.tags, CONFIG.max_tags); b.skills = list_of(b.skills, CONFIG.max_skills); b.equipment = list_of(b.equipment, CONFIG.max_equipment)
    b.arts = list_of(b.arts, CONFIG.max_arts); b.carried = list_of(b.carried, CONFIG.max_carried)
    b.class = whole(b.class, 0, 99, 0); b.level = whole(b.level, 0, 216, 0)
    b.title = clean(b.title, CONFIG.max_title); b.note = clean(b.note, CONFIG.max_note)
    index(b)
  end
end

-- ---- what leaves the server: a whitelist of fields, never the owner ----

local function summary(b)
  return { id = b.id, name = b.name, class = b.class, class_name = class_name(b.class), level = b.level, title = b.title,
    tags = b.tags, verified = b.verified and true or false, updated = b.updated }
end

local function full(b)
  local view = summary(b)
  view.note = b.note; view.created = b.created; view.experience = b.experience; view.skill_points = b.skill_points
  view.skills = b.skills; view.arts = b.arts; view.equipment = b.equipment; view.carried = b.carried
  view.attributes = b.attributes; view.attribute_points = b.attribute_points
  return view
end

-- ---- assembling a build from the player and its request ----

local function assemble(player, request)
  local old = builds[by_owner[player.uid]]
  local now = server.time()
  local b = {
    id = old and old.id, owner = player.uid, name = player.character, created = old and old.created or now, updated = now,
    title = clean(request.title, CONFIG.max_title), note = clean(request.note, CONFIG.max_note),
    tags = {}, skills = {}, arts = {}, equipment = {}, carried = {}, verified = false,
  }
  for _, tag in ipairs(list_of(request.tags, CONFIG.max_tags)) do
    tag = clean(tag, CONFIG.max_tag)
    if #tag > 0 then b.tags[#b.tags + 1] = tag end
  end
  b.class = whole(player.class, 0, 99, 0)
  b.level = whole(player.level, 0, 216, 0)
  local hero = server.hero(player)
  if hero then
    b.verified = true
    b.class = whole(hero.class, 0, 99, b.class); b.level = whole(hero.level, 0, 216, b.level)
    b.experience = hero.experience; b.skill_points = hero.skill_points
    b.attribute_points = hero.attribute_points; b.attributes = hero.attributes
    for _, s in ipairs(list_of(hero.skills, CONFIG.max_skills)) do
      b.skills[#b.skills + 1] = { id = s.id, level = s.level, bonus = s.bonus, name = clean(s.name or ("skill " .. s.id), CONFIG.max_item) }
    end
    for _, a in ipairs(list_of(hero.arts, CONFIG.max_arts)) do
      b.arts[#b.arts + 1] = { id = a.id, kind = a.kind, rank = a.rank, name = clean(a.name or ("art " .. a.id), CONFIG.max_item) }
    end
    for _, e in ipairs(list_of(hero.equipment, CONFIG.max_equipment)) do
      b.equipment[#b.equipment + 1] = { slot = e.slot, level = e.level, tier = tier_name(e.rarity), sockets = e.sockets,
        name = clean(e.name or ("item " .. e.type), CONFIG.max_item) }
    end
  else
    if b.level == 0 then b.level = whole(request.level, 1, 216, 0) end    -- reported by the client: only when the server has no level
    for _, item in ipairs(list_of(request.carried, CONFIG.max_carried)) do
      item = clean(item, CONFIG.max_item)
      if #item > 0 then b.carried[#b.carried + 1] = item end
    end
  end
  return b, old
end

local function publish(player, request)
  local now = server.time()
  if last_publish[player.uid] and now - last_publish[player.uid] < CONFIG.publish_cooldown then
    return nil, "wait a few seconds before publishing again"
  end
  local b, old = assemble(player, request)
  if not old then
    if count >= CONFIG.max_builds then return nil, "the build list of this server is full" end
    b.id = "b" .. string.format("%d", server.add(nil, "next_id"))
  end
  local ok, err = pcall(server.save, build_key(b.id), b)
  if not ok then
    if not old then server.add(nil, "next_id", -1) end
    return nil, "the build could not be stored"
  end
  last_publish[player.uid] = now
  index(b)
  return b
end

local function remove(player)
  local id = by_owner[player.uid]
  if not id then return false end
  local b = builds[id]
  server.delete(build_key(id))
  unindex(b)
  return true
end

-- ---- search ----

local function class_filter(value)
  if type(value) == "number" then return value end
  if type(value) ~= "string" or value == "" then return nil end
  return tonumber(value) or CLASS_IDS[string.lower(value)]
end

local function find(q, class, sort, page, per)
  q = string.lower(clean(q, CONFIG.max_query)); class = class_filter(class)
  local matched = {}
  for id, b in pairs(builds) do
    if (not class or b.class == class) and (q == "" or string.find(search_text[id], q, 1, true)) then matched[#matched + 1] = b end
  end
  if sort == "level" then
    table.sort(matched, function(a, b) if a.level ~= b.level then return a.level > b.level end return a.id < b.id end)
  else
    table.sort(matched, function(a, b) if a.updated ~= b.updated then return a.updated > b.updated end return a.id < b.id end)
  end
  per = whole(per, 1, CONFIG.max_page_size, CONFIG.default_page_size)
  local pages = math.max(1, math.ceil(#matched / per))
  page = whole(page, 1, pages, 1)
  local rows = {}
  for i = (page - 1) * per + 1, math.min(#matched, page * per) do rows[#rows + 1] = summary(matched[i]) end
  return { total = #matched, page = page, pages = pages, per = per, builds = rows }
end

-- ---- the net channel ----

local ch = net.channel(CHANNEL)

local function reply(player, payload)
  if ch then ch:send(player, payload) end
end

-- A token bucket per client (capacity and refill are config fields).
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

server.on("leave", function(player) buckets[player.slot] = nil end)

if ch then
  ch:on(function(player, request)
    if type(request) ~= "table" or type(request.op) ~= "string" then return end
    if not allowed(player.slot) then reply(player, { op = "error", error = "too many requests" }); return end
    local op = request.op
    if op == "list" then
      local result = find(request.q, request.class, request.sort, request.page, request.per)
      result.op = "list"
      reply(player, result)
    elseif op == "get" then
      local b = type(request.id) == "string" and builds[request.id]
      if b then reply(player, { op = "build", build = full(b) }) else reply(player, { op = "error", error = "no such build" }) end
    elseif op == "mine" then
      local b = builds[by_owner[player.uid]]
      reply(player, { op = "mine", build = b and full(b) or nil, count = count, hero_data = server.hero(player) ~= nil })
    elseif op == "publish" then
      local b, err = publish(player, request)
      if b then reply(player, { op = "published", build = full(b) }) else reply(player, { op = "error", error = err }) end
    elseif op == "unpublish" then
      reply(player, { op = "unpublished", removed = remove(player) })
    end
  end)
end

-- Hot reload and shutdown: builds are stored when published, so nothing is lost; the publish cooldowns and the request buckets (rate
-- limits) only live in memory and are carried over a reload (a restart starts them clean: slots mean other heroes then).
server.on("unload", function(reason)
  local session = { publish = last_publish, buckets = {} }
  if reason == "reload" then
    for _, player in ipairs(server.players()) do
      if buckets[player.slot] then session.buckets[tostring(player.slot)] = { uid = player.uid, tokens = buckets[player.slot].tokens, at = buckets[player.slot].at } end
    end
  end
  pcall(server.save, "_session", session)
  config_unload(reason)
end)

if CFG.reason == "reload" then
  local ok, session = pcall(server.load, "_session", nil)
  if ok and type(session) == "table" then
    if type(session.publish) == "table" then
      for uid, t in pairs(session.publish) do if type(t) == "number" then last_publish[uid] = t end end
    end
    for slot_text, entry in pairs(type(session.buckets) == "table" and session.buckets or {}) do
      local slot = tonumber(slot_text)
      local player = slot and server.player(slot)
      if player and type(entry) == "table" and player.uid == entry.uid and tonumber(entry.tokens) and tonumber(entry.at) then
        buckets[slot] = { tokens = entry.tokens, at = entry.at }
      end
    end
  end
end
pcall(server.delete, "_session")

-- ---- chat: !sharebuild [title], !unsharebuild, !builds ----

server.on("chat", function(player, text)
  if text == "!builds" then
    server.message(player, count .. " shared build" .. (count == 1 and "" or "s") .. ". !sharebuild [title] publishes yours.")
    return true
  elseif text == "!unsharebuild" then
    server.message(player, remove(player) and "Your build is removed." or "You have no shared build.")
    return true
  elseif text == "!sharebuild" or string.sub(text, 1, 12) == "!sharebuild " then
    local b, err = publish(player, { title = string.sub(text, 13) })
    if b then
      server.message(player, "Build shared as " .. b.id .. (b.verified and "" or " (the server has no hero data: class and level only)") .. ".")
    else
      server.message(player, "Not shared: " .. err .. ".")
    end
    return true
  end
end)

-- ---- endpoints ----

server.endpoint("builds", function(request)
  local q = request.query
  return find(q.q, q.class, q.sort, tonumber(q.page), tonumber(q.n))
end, { public = true, description = "shared builds as JSON: ?q=text&class=3|Seraphim&sort=new|level&page=1&n=12" })

server.endpoint("build", function(request)
  local b = request.query.id and builds[request.query.id]
  if not b then return "no such build", "text/plain", 404 end
  return full(b)
end, { public = true, description = "one shared build in full as JSON: ?id=b1" })

-- ---- the page: built on the server, every text passes through server.html ----

-- The page head and style are build_sharer.html, the filter script build_sharer.js, both next to this script (server.file).

local script = server.file("build_sharer.js")

local function ago(t)
  local s = math.max(0, server.time() - (t or 0))
  if s < 90 then return "just now" elseif s < 5400 then return string.format("%d min ago", math.floor(s / 60))
  elseif s < 129600 then return string.format("%d h ago", math.floor(s / 3600)) else return string.format("%d days ago", math.floor(s / 86400)) end
end

local function head(title)
  return server.render(server.file("build_sharer.html"), { title = title })
end

local function tags_html(b)
  local parts = {}
  for _, tag in ipairs(b.tags) do parts[#parts + 1] = '<span class="tag">' .. server.html(tag) .. "</span>" end
  return table.concat(parts)
end

local function badge(b)
  return b.verified and '<small class="ok">server-verified</small>' or "<small>reported by the player</small>"
end

local function card(b)
  local h = server.html
  return table.concat({ '<section class="card" data-c="', tostring(b.class), '" data-s="', h(search_text[b.id] or ""), '"><h2><a href="?id=', h(b.id), '">',
    h(b.name), "</a></h2><div>", h(class_name(b.class)), " &middot; level ", tostring(b.level), " ", badge(b), "</div>",
    b.title ~= "" and ("<p><b>" .. h(b.title) .. "</b></p>") or "", "<div>", tags_html(b), "</div><small>", ago(b.updated), "</small>", "</section>" })
end

local function list_page(request)
  local h = server.html
  local result = find(request.query.q, request.query.class, request.query.sort, 1, CONFIG.max_page_size * 8)
  local q = clean(request.query.q, CONFIG.max_query)
  local options = { '<option value="">all classes</option>' }
  local wanted = class_filter(request.query.class)
  for _, id in ipairs({ 1, 2, 3, 4, 5, 6, 8, 9 }) do
    options[#options + 1] = '<option value="' .. id .. '"' .. (wanted == id and " selected" or "") .. ">" .. h(class_name(id)) .. "</option>"
  end
  local cards = {}
  for _, row in ipairs(result.builds) do cards[#cards + 1] = card(builds[row.id]) end
  if #cards == 0 then cards[1] = "<p class=\"dim\">Nobody has shared a build yet. In the game: !sharebuild [title], or the Build sharer panel of Sacred Gear.</p>" end
  return table.concat({ head("Shared builds"), '<h1>Shared hero builds</h1><div class="bar"><input id="q" type="search" placeholder="search heroes, skills, items, tags" size="34" value="',
    h(q), '"><select id="c">', table.concat(options), '</select><span class="dim" id="n"></span></div><main>', table.concat(cards), "</main>", config_footer(), "<script>", script, "</script></body></html>" })
end

local function detail_page(b)
  local h = server.html
  local parts = { head(b.name .. " - build"), '<h1><a href="?">Shared builds</a> / ', h(b.name), '</h1><main><section class="card"><h2>', h(class_name(b.class)), ", level ", tostring(b.level),
    "</h2>", badge(b), b.title ~= "" and ("<p><b>" .. h(b.title) .. "</b></p>") or "", b.note ~= "" and ("<p>" .. h(b.note) .. "</p>") or "", "<div>", tags_html(b), "</div>",
    "<small>shared ", ago(b.created), ", updated ", ago(b.updated), "</small></section>" }
  local function section(title, items, line)
    if #items == 0 then return end
    parts[#parts + 1] = '<section class="card"><h2>' .. h(title) .. "</h2><ul>"
    for _, item in ipairs(items) do parts[#parts + 1] = "<li>" .. line(item) .. "</li>" end
    parts[#parts + 1] = "</ul></section>"
  end
  section("Skills", b.skills, function(s) return h(s.name) .. " <b>" .. tostring(s.level) .. "</b>" .. ((s.bonus or 0) > 0 and (" <small>+" .. tostring(s.bonus) .. "</small>") or "") end)
  section("Combat arts", b.arts, function(a) return h(a.name) end)
  section("Equipment", b.equipment, function(e)
    return '<span class="' .. h(e.tier or "normal") .. '">' .. h(e.name) .. "</span> <small>item level " .. tostring(e.level) .. ((e.sockets or 0) > 0 and (", " .. tostring(e.sockets) .. " socketed") or "") .. "</small>"
  end)
  section("Carried (reported by the player)", b.carried, function(c) return h(c) end)
  parts[#parts + 1] = "</main>" .. config_footer() .. "</body></html>"
  return table.concat(parts)
end

server.page("", function(request)
  local id = request.query.id
  if id and builds[id] then return detail_page(builds[id]) end
  return list_page(request)
end, { public = true, title = "Shared builds" })
