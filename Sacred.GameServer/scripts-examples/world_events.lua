-- World events: server-wide timed goals ("the realm must slay 1000 orcs in 2 hours"). Everybody who took part shares the reward,
-- the realm is told when an event starts, at 25/50/75 % and how it ended, and Sacred Gear clients with the "world-events" mod
-- show a movable progress bar. Copy into the server's scripts folder (default ./scripts) to use it.
--
--   !event                       progress of the running event (or the next one) to the player who asked
--   !sector / !here              the sector the player stands in (id, grid cell), the position and the areas it is in
--   !area ...                    (admins) record named areas by walking: see "areas" below
--   /mods/world_events/          the public dashboard page (character names only)
--   /mods/world_events/state     the same as JSON (public, with WebUi:PublicApi=true also under /public/mods/...)
--   the web UI's Events page     add, change, disable and remove events while the server runs, start or cancel one (see "web events")
--
-- WEB EVENTS: the web UI's Events page keeps events of its own in this mod's storage (key "events", the same fields as EVENTS below, plus
-- "created" for the order) and a list of switched-off ids (key "events_off"). They are picked up within two seconds, no reload and no
-- restart: a web event with the id of one in EVENTS replaces it, switching one off takes it out of the rotation (an event that is
-- running keeps its own copy of the definition, so editing or removing it never changes it midway). The page also starts an event at
-- once, cancels the running one (no rewards) and moves the next start up (key "control"; each command runs exactly once).
-- HOT RELOAD: the mod saves everything in its "unload" handler (see ScriptHost.Reload.cs), so editing this file (or the Reload button
-- of the Mods page) loses nothing: the running event, the contributions and the rewards still waiting carry on in the new version.
--
-- Everything is kept in this mod's own storage (script-data/world_events.json): the running event, who contributed what, the last
-- results and rewards still waiting for heroes who were offline. An event that ran out while the server was down is closed
-- (failed) when the server starts. Rewards need the hero in the world: heroes who are not get theirs when they enter.
-- Needs player.sector (the "sector" field of the player table) only for events with `sectors`; `names` needs --game-dir (without
-- it a creature has no name; use `types` then). The client bar needs the custom net messages (NETOPS, on by default).
server.describe("Server-wide timed goals (slay N creatures in a time), shared rewards, chat commands, a public dashboard and a progress bar for Sacred Gear clients.")

-- ---- configuration -----------------------------------------------------------------------------------------------------------

local CONFIG = {
  first_delay = 60,         -- seconds after the very first start of this mod until the first event begins
  cooldown = 900,           -- seconds between the end of one event and the start of the next
  min_players = 1,          -- an event only starts while at least this many heroes are connected
  result_hold = 120,        -- seconds the clients keep showing the result
  order = "sequence",       -- "sequence" (in the list's order) or "random" (never the same one twice in a row)
  milestones = { 25, 50, 75 },
  announce = true,          -- broadcast start, milestones and result to everybody
  max_contributors = 2000,  -- heroes remembered per event (more still count for the total, but get no share)
  where = "victim",         -- where a kill counts, unless an event says otherwise: "victim" = where the creature died (where the
                            -- player stands when the server never saw the creature), "player" = where the killer stands,
                            -- "either" = either place is in the area
  margin = 1,               -- the client shows the event this many sector cells beyond the area too (needs --game-dir)
  circle_margin = 1500,     -- ... and this many world units beyond a circle
  max_area_sectors = 1500,  -- an area is cut off at this many sectors
  admins = {},              -- character names that may record areas with !area (empty: the host player)
  debug_kills = false,      -- log every kill with the creature's sector and position (to read sector numbers off)
}

-- Named AREAS: sets of sectors an event can be tied to (`area = "silver_creek"` in an event, or `areas = {...}` for several).
-- Sector ids are slots of the game's sector table, NOT coordinates: neighbours in the world are not neighbours in the numbers. To
-- find the numbers: stand in a place and type !sector (or !here), or set debug_kills = true and read the log, or record an area by
-- walking with !area start, or use the area editor page of the web UI. An area is the UNION of its parts:
--   sectors   sector ids and inclusive ranges: { 2909, 2910, { 2915, 2920 } }   (`ranges` = { { 2915, 2920 } } is the same)
--   rect      { x1, y1, x2, y2 }: every sector of those cells of the 128 x 128 sector grid (needs --game-dir)
--   near      n: plus every sector within n grid cells of the ones above (needs --game-dir)
--   circle    { x = , y = , r = } in world units: a point inside it counts (a tile is 53.67 units, a sector 64 tiles = 3435 units);
--             finer than sectors, for a village inside a big sector. !here prints your x and y.
-- Areas recorded in game (!area) and by the area editor are kept in this mod's storage (key "areas") in this same format; a
-- recorded one wins over one here with the same id.
local AREAS = {
  -- Fill in the real numbers (see above); the "Thief hunt" below is offered once this area has sectors:
  silver_creek = { name = "Silver Creek", sectors = {}, near = 1 },
}

-- DEFAULT EVENTS (only copied into the storage on the first run, see above). One entry per event. Rotation goes through the list; an event ends when its target is reached (success) or its time is up (fail).
--   id, title      short key and the name shown to players
--   goal           "kill": defeat `target` creatures (the only kind for now)
--   target         how many
--   duration       seconds
--   names          only creatures whose name contains one of these words count (needs --game-dir; any case)
--   types          or creature types (numbers), when the server has no game data
--   area / areas   only kills in these named AREAS count (see above), and the client's indicator shows only in or near them
--   sectors        or a list of sector ids and ranges right here, for an area of its own
--   where          "victim", "player" or "either" for this event (default: CONFIG.where)
--   text           the goal in words (default: built from the above)
--   unit           what is counted, for the clients' two-line label ("370 / 1,000 orcs"; default: the first name + s, else creatures)
--   min_contribution  kills a hero needs to share the reward (default 1)
--   reward         gold = pool shared by contribution, gold_each = gold for everyone who qualifies,
--                  item = {quality=.., category=.., level=..} (as server.drop_item) one for everyone who qualifies,
--                  top_item = the same for the best contributor only,
--                  milestones = rarity ODDS that improve with the event's progress (below); then `item` only says which kind of
--                  item (category, level) and its quality is rolled from the odds,
--                  on_fail = false: no item rolls when the time runs out (default: a failed event still rolls, with the odds of the
--                  highest milestone it reached; gold and the top prize are paid only for a completed event)
--
-- REWARD ODDS (milestones). `milestones = { { at = 1, weights = { common = 100 } }, { at = 10, weights = { common = 70, magic = 30 } }, ... }`:
-- `at` is the percent of the target the realm has reached, `weights` are relative weights per rarity tier (TIERS below); a tier's chance
-- is its weight divided by the sum of the weights, so the chances always add up to 100 %. Rule, checked when the script loads: every
-- weight is >= 0, at least one is > 0, and the best tier (legendary) may never be the only one with weight: its chance stays
-- below 100 % (a milestone that breaks the rule is refused with a log line). When the event ends (completed or out of time) every
-- hero with at least `min_contribution` kills gets ONE item rolled with server.random using the odds of the HIGHEST milestone the event
-- reached (an event that ended at 35 % uses the 30 % row). Nothing else changes the roll: not level, class, luck or magic find, not how
-- many kills beyond the minimum. All odds are shown: the dashboard page, /mods/world_events/state, !event rewards, the announcement
-- when a milestone is reached and the tooltip of the Sacred Gear mod. What was rolled for whom goes to the public "recent rewards".
-- The rarity tiers and the game's generation quality (0..14, higher is rarer) they force in server.drop_item:
local TIERS = {
  { id = "common", name = "Common", quality = 0 },
  { id = "magic", name = "Magic", quality = 4 },
  { id = "rare", name = "Rare", quality = 8 },
  { id = "epic", name = "Epic", quality = 11 },
  { id = "legendary", name = "Legendary", quality = 14 },
}
local EVENTS = {
  { id = "orc-hunt", title = "Orc hunt", goal = "kill", target = 1000, duration = 2 * 3600, names = { "orc" },
    text = "Slay 1000 orcs", min_contribution = 10,
    reward = { gold = 50000, gold_each = 500, item = {}, top_item = { quality = 9 },
      milestones = {
        { at = 1,  weights = { common = 100 } },
        { at = 10, weights = { common = 75, magic = 25 } },
        { at = 30, weights = { common = 50, magic = 30, rare = 20 } },
        { at = 80, weights = { common = 35, magic = 30, rare = 25, epic = 8, legendary = 2 } },
      } } },
  { id = "great-cull", title = "The great cull", goal = "kill", target = 3000, duration = 3 * 3600,
    text = "Defeat 3000 creatures of any kind", min_contribution = 20,
    reward = { gold = 150000, gold_each = 1000, item = { quality = 6 }, top_item = { quality = 10 } } },
  -- Dormant until the area silver_creek above has sectors. The names are matched in any case against the game's creature names
  -- (check them with debug_kills = true).
  { id = "thief-hunt", title = "Thief hunt", goal = "kill", target = 500, duration = 2 * 3600, area = "silver_creek",
    names = { "thief", "bandit", "brigand", "robber" }, unit = "thieves", text = "Defeat 500 thieves around Silver Creek",
    min_contribution = 5, reward = { gold = 40000, gold_each = 400, item = { quality = 3 }, top_item = { quality = 8 } } },
}

-- ---- helpers -----------------------------------------------------------------------------------------------------------

local PREFIX = "[World event] "
local NOW = server.time

local function say(text) if CONFIG.announce then server.broadcast(PREFIX .. text) end end

local function duration_text(seconds)
  seconds = math.max(0, math.floor(seconds + 0.5))
  if seconds >= 3600 then return string.format("%dh %02dm", math.floor(seconds / 3600), math.floor((seconds % 3600) / 60)) end
  if seconds >= 60 then return string.format("%dm %02ds", math.floor(seconds / 60), seconds % 60) end
  return seconds .. "s"
end

local function copy_list(list, convert)
  if type(list) ~= "table" then return nil end
  local out = {}
  for _, v in ipairs(list) do
    v = convert and convert(v) or v
    if v ~= nil then out[#out + 1] = v end
  end
  if #out == 0 then return nil end
  return out
end

local function number_of(v) return tonumber(v) end
local function lower_of(v) return type(v) == "string" and v ~= "" and v:lower() or nil end

-- ---- reward odds ---------------------------------------------------------------------------------------------------------

local tier_index = {}
table.sort(TIERS, function(a, b) return a.quality < b.quality end)
for i, tier in ipairs(TIERS) do tier_index[tier.id] = i end
local TOP_TIER = TIERS[#TIERS]

local function percent_text(value)
  local text = string.format("%.1f", value):gsub("%.0$", "")
  return text .. "%"
end

-- A milestone from its config: { at, odds = { { id, name, quality, weight, percent }, ... } } (tiers with a weight, worst first), or
-- nil and the reason. The chances are the weights divided by their sum, so they add up to 100 %.
local function make_milestone(m)
  if type(m) ~= "table" or type(m.weights) ~= "table" then return nil, "no weights" end
  local at = tonumber(m.at)
  if not at or at < 0 or at > 100 then return nil, "'at' must be a percent from 0 to 100" end
  local total, entries = 0, {}
  for id, weight in pairs(m.weights) do
    local i = tier_index[id]
    if not i then return nil, "unknown tier '" .. tostring(id) .. "'" end
    weight = tonumber(weight)
    if not weight or weight < 0 then return nil, "the weight of '" .. id .. "' must be a number >= 0" end
    if weight > 0 then total = total + weight; entries[#entries + 1] = { i = i, weight = weight } end
  end
  if total <= 0 then return nil, "no tier has a weight" end
  table.sort(entries, function(a, b) return a.i < b.i end)
  local odds, top = {}, 0
  for _, e in ipairs(entries) do
    local tier = TIERS[e.i]
    local percent = e.weight * 100 / total
    odds[#odds + 1] = { id = tier.id, name = tier.name, quality = tier.quality, weight = e.weight, percent = percent }
    if tier.id == TOP_TIER.id then top = percent end
  end
  if top >= 100 then return nil, "the best tier (" .. TOP_TIER.id .. ") would be 100 %: give another tier a weight" end
  return { at = at, odds = odds }
end

local milestone_cache = setmetatable({}, { __mode = "k" })

-- The validated milestones of a reward config (sorted by `at`), nil when there are none. Refused rows are logged once.
local function build_milestones(reward, event_id)
  if type(reward) ~= "table" or type(reward.milestones) ~= "table" then return nil end
  if milestone_cache[reward] ~= nil then return milestone_cache[reward] or nil end
  local list = {}
  for i, m in ipairs(reward.milestones) do
    local milestone, why = make_milestone(m)
    if milestone then list[#list + 1] = milestone
    else server.log("event " .. tostring(event_id) .. ": reward milestone #" .. i .. " refused: " .. why) end
  end
  table.sort(list, function(a, b) return a.at < b.at end)
  milestone_cache[reward] = #list > 0 and list or false
  return #list > 0 and list or nil
end

local function odds_text(milestone)
  local parts = {}
  for i = #milestone.odds, 1, -1 do
    parts[#parts + 1] = milestone.odds[i].name .. " " .. percent_text(milestone.odds[i].percent)
  end
  return table.concat(parts, ", ")
end

-- The highest milestone with at <= percent (index), and the next one (index), either may be nil.
local function milestone_at(milestones, percent)
  local current, nextm
  for i, m in ipairs(milestones or {}) do
    if m.at <= percent then current = i elseif not nextm then nextm = i end
  end
  return current, nextm
end

local function roll_tier(milestone)
  local total = 0
  for _, o in ipairs(milestone.odds) do total = total + o.weight end
  local pick = server.random() * total
  for _, o in ipairs(milestone.odds) do
    pick = pick - o.weight
    if pick < 0 then return o end
  end
  return milestone.odds[#milestone.odds]
end

-- A definition as it is stored with a running event (so editing this file does not change an event in progress).
local function snapshot(def)
  local reward = type(def.reward) == "table" and def.reward or {}
  return {
    id = tostring(def.id), title = tostring(def.title or def.id), goal = "kill",
    target = math.max(1, math.floor(tonumber(def.target) or 100)), duration = math.max(1, tonumber(def.duration) or 3600),
    names = copy_list(def.names, lower_of), types = copy_list(def.types, number_of), where = def.where,
    text = def.text and tostring(def.text) or nil, unit = def.unit and tostring(def.unit) or nil, min_contribution = math.max(1, math.floor(tonumber(def.min_contribution) or 1)),
    reward = {
      gold = math.max(0, math.floor(tonumber(reward.gold) or 0)), gold_each = math.max(0, math.floor(tonumber(reward.gold_each) or 0)),
      item = type(reward.item) == "table" and reward.item or nil, top_item = type(reward.top_item) == "table" and reward.top_item or nil,
      milestones = build_milestones(reward, def.id), on_fail = reward.on_fail,
    },
  }
end

local definitions = {}          -- the events in the rotation, in order (filled by rebuild_definitions below, rebuilt when the web changes them)

local function unit_of(def)
  if def.unit then return def.unit end
  if def.names then return def.names[1] .. "s" end
  return "creatures"
end

local function goal_text(def)
  if def.text then return def.text end
  local what = "creatures"
  if def.names then what = table.concat(def.names, "/") .. " creatures" end
  local text = "Defeat " .. def.target .. " " .. what
  if def.region and def.region.name then text = text .. " around " .. def.region.name end
  return text
end

local function reward_text(def)
  local r, parts = def.reward or {}, {}
  local gold = (r.gold or 0) + (r.gold_each or 0)
  if gold > 0 then parts[#parts + 1] = "gold" end
  if r.item then parts[#parts + 1] = "an item" end
  if r.top_item then parts[#parts + 1] = "a prize for the best" end
  return table.concat(parts, ", ")
end

local function in_set(list, value)
  for _, v in ipairs(list) do if v == value then return true end end
  return false
end

-- ---- areas: where an event happens ------------------------------------------------------------------------------------------

local function clean_name(text)
  return (tostring(text or ""):gsub("[%c\\]", " "):sub(1, 40))
end

local function add_range(set, a, b)
  a, b = math.floor(a), math.floor(b)
  if b < a then a, b = b, a end
  if b - a >= CONFIG.max_area_sectors then b = a + CONFIG.max_area_sectors - 1 end
  for id = a, b do set[id] = true end
end

local function set_size(set)
  local n = 0
  for _ in pairs(set) do n = n + 1 end
  return n
end

-- Every sector within n grid cells of the ones in `set` (the set itself included); needs the sector grid.
local function grow(set, n)
  if n <= 0 or set_size(set) > CONFIG.max_area_sectors then return set end
  local out = {}
  for id in pairs(set) do
    out[id] = true
    local cell = server.sector_cell(id)
    if cell then
      for dx = -n, n do
        for dy = -n, n do
          local other = server.sector_at(cell.x + dx, cell.y + dy)
          if other then out[other] = true end
        end
      end
    end
  end
  return out
end

-- The sectors and circles of one area definition (a config or a recorded one) added to `ids` / `circles`.
local function add_parts(def, ids, circles)
  local own = {}
  for _, part in ipairs(def.sectors or {}) do
    if type(part) == "table" then add_range(own, tonumber(part[1]) or 0, tonumber(part[2]) or tonumber(part[1]) or 0)
    elseif tonumber(part) then own[math.floor(tonumber(part))] = true end
  end
  for _, part in ipairs(def.ranges or {}) do
    if type(part) == "table" then add_range(own, tonumber(part[1]) or 0, tonumber(part[2]) or tonumber(part[1]) or 0) end
  end
  if type(def.rect) == "table" and #def.rect >= 4 then
    local x1, y1 = math.min(def.rect[1], def.rect[3]), math.min(def.rect[2], def.rect[4])
    local x2, y2 = math.max(def.rect[1], def.rect[3]), math.max(def.rect[2], def.rect[4])
    local count = 0
    for cx = x1, x2 do
      for cy = y1, y2 do
        local id = count < CONFIG.max_area_sectors and server.sector_at(cx, cy)
        if id then own[id] = true; count = count + 1 end
      end
    end
  end
  if tonumber(def.near) and def.near > 0 then own = grow(own, math.floor(def.near)) end
  for id in pairs(own) do ids[id] = true end
  local c = def.circle
  if type(c) == "table" and tonumber(c.x) and tonumber(c.y) and tonumber(c.r) and c.r > 0 then
    circles[#circles + 1] = { x = c.x, y = c.y, r = c.r }
  end
end

local recorded = (function()
  local ok, value = pcall(server.load, "areas", nil)
  return ok and type(value) == "table" and value or {}
end)()
local areas_rev = (function() local ok, v = pcall(server.load, "areas_rev", 0); return ok and tonumber(v) or 0 end)()

local function area_def(id)
  return recorded[id] or AREAS[id]
end

local function sorted_ids(set)
  local list = {}
  for id in pairs(set) do list[#list + 1] = id end
  table.sort(list)
  return list
end

-- The region of an event definition as it is stored with the event: { name, ids = {sorted}, margin = {sorted}, circles = {..} },
-- or nil for "everywhere". A region with no sector and no circle is empty: it matches nothing (the event is not offered).
local function region_of(def)
  local names, ids, circles = {}, {}, {}
  local wanted = {}
  if def.area then wanted[#wanted + 1] = def.area end
  for _, id in ipairs(def.areas or {}) do wanted[#wanted + 1] = id end
  for _, id in ipairs(wanted) do
    local a = area_def(id)
    if a then
      names[#names + 1] = clean_name(a.name or id)
      add_parts(a, ids, circles)
    else
      server.log("event " .. tostring(def.id) .. ": unknown area '" .. tostring(id) .. "'")
    end
  end
  if def.sectors then add_parts({ sectors = def.sectors }, ids, circles) end
  if #wanted == 0 and not def.sectors then return nil end
  local region = { ids = sorted_ids(ids), circles = circles }
  if #names > 0 then region.name = table.concat(names, " & ") end
  region.margin = sorted_ids(grow(ids, CONFIG.margin))
  return region
end

-- The sets behind a stored region, built once per stored table.
local runtime = setmetatable({}, { __mode = "k" })
local function sets_of(region)
  local r = runtime[region]
  if not r then
    r = { ids = {}, margin = {} }
    for _, id in ipairs(region.ids or {}) do r.ids[id] = true end
    for _, id in ipairs(region.margin or {}) do r.margin[id] = true end
    runtime[region] = r
  end
  return r
end

local function in_circle(c, x, y)
  if not x or not y then return false end
  local dx, dy = x - c.x, y - c.y
  return dx * dx + dy * dy <= c.r * c.r
end

-- Is the point inside the region (or, with `slack`, close to it)?
local function contains(region, sector, x, y, slack)
  local r = sets_of(region)
  if sector and (r.ids[sector] or (slack and r.margin[sector])) then return true end
  for _, c in ipairs(region.circles or {}) do
    if in_circle({ x = c.x, y = c.y, r = c.r + (slack and CONFIG.circle_margin or 0) }, x, y) then return true end
  end
  return false
end

local function region_empty(region)
  return region ~= nil and #region.ids == 0 and #(region.circles or {}) == 0
end

-- Is the player where the event happens (or near it: the indicator shows a little beyond)? Events without an area happen everywhere.
local function in_area(def, player)
  local region = def.region
  if not region then return true end
  return contains(region, player.sector, player.x, player.y, true)
end

-- Does the kill happen in the event's area? See CONFIG.where.
local function kill_in_area(def, player, monster)
  local region = def.region
  if not region then return true end
  local where = def.where or CONFIG.where
  local function victim() return monster.sector ~= nil and contains(region, monster.sector, monster.x, monster.y) end
  local function killer() return contains(region, player.sector, player.x, player.y) end
  if where == "player" then return killer() end
  if where == "either" then return victim() or killer() end
  if monster.sector ~= nil then return victim() end       -- "victim": the creature's place, the player's when it is unknown
  return killer()
end

local function counts(def, player, monster)
  if not kill_in_area(def, player, monster) then return false end
  if not def.names and not def.types then return true end
  if def.names and monster.name then
    local name = monster.name:lower()
    for _, word in ipairs(def.names) do if name:find(word, 1, true) then return true end end
  end
  if def.types and in_set(def.types, monster.type) then return true end
  return false
end

-- Compact text of a sorted list of sector ids: runs of three or more become ranges: 2909, {2911, 2913}.
local function compact(ids)
  local out, i = {}, 1
  while i <= #ids do
    local j = i
    while ids[j + 1] == ids[j] + 1 do j = j + 1 end
    if j - i >= 2 then out[#out + 1] = "{" .. ids[i] .. ", " .. ids[j] .. "}"
    else for k = i, j do out[#out + 1] = tostring(ids[k]) end end
    i = j + 1
  end
  return table.concat(out, ", ")
end

local function lua_string(text)
  local escaped = tostring(text):gsub("%c", " "):gsub("\\", "\\\\"):gsub('"', '\\"')
  return '"' .. escaped .. '"'
end

-- The Lua line for an area (what !area show prints and the area editor copies).
local function snippet(id, a)
  local parts = { "name = " .. lua_string(a.name or id) }
  local ids = {}
  for _, part in ipairs(a.sectors or {}) do
    if type(part) == "table" then for n = part[1], part[2] do ids[#ids + 1] = n end else ids[#ids + 1] = part end
  end
  table.sort(ids)
  if #ids > 0 then parts[#parts + 1] = "sectors = {" .. compact(ids) .. "}" end
  if a.near then parts[#parts + 1] = "near = " .. a.near end
  if a.circle then parts[#parts + 1] = string.format("circle = { x = %d, y = %d, r = %d }", a.circle.x, a.circle.y, a.circle.r) end
  return id .. " = { " .. table.concat(parts, ", ") .. " },"
end

-- ---- state (kept in this mod's storage) ---------------------------------------------------------------------------------------

local function fresh()
  return { seq = 0, index = 0, history = {}, next_start = NOW() + CONFIG.first_delay, control_rev = (function() local ok, v = pcall(server.load, "control_rev", 0); return ok and tonumber(v) or 0 end)() }
end

local function load_state()
  local ok, value = pcall(server.load, "state", nil)
  if not ok or type(value) ~= "table" then return fresh() end
  value.seq = tonumber(value.seq) or 0
  value.index = tonumber(value.index) or 0
  value.history = type(value.history) == "table" and value.history or {}
  value.next_start = tonumber(value.next_start) or NOW()
  if value.current ~= nil and (type(value.current) ~= "table" or type(value.current.def) ~= "table"
      or not tonumber(value.current.ends) or type(value.current.players) ~= "table") then
    value.current = nil
  end
  return value
end

local state = load_state()
local pending = (function()
  local ok, value = pcall(server.load, "pending", nil)
  return ok and type(value) == "table" and value or {}
end)()

local rewardlog = (function()
  local ok, value = pcall(server.load, "rewardlog", nil)
  return ok and type(value) == "table" and value or {}
end)()

local dirty, pending_dirty, push_due, rewardlog_dirty = false, false, false, false

-- ---- the events: those of this file and those of the web UI ---------------------------------------------------------------------

local function load_key(key, default)
  local ok, value = pcall(server.load, key, nil)
  if ok and type(value) == "table" then return value end
  return default
end

local function rev_of(key)
  local ok, value = pcall(server.load, key, 0)
  return ok and tonumber(value) or 0
end

local stored_events = load_key("events", {})
local events_off = load_key("events_off", {})
local events_rev, events_off_rev = rev_of("events_rev"), rev_of("events_off_rev")

-- EVENTS below only seeds a fresh install: the first time this mod runs with no events in its storage they are copied there, and from then on
-- the storage is the truth (edit, add and delete on the web UI's Events page; the file's list is not read again).
local seeded = (function() local ok, v = pcall(server.load, "events_seeded", false); return ok and v == true end)()
local function deep_copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, x in pairs(v) do out[k] = deep_copy(x) end
  return out
end
if not seeded and next(stored_events) == nil then
  for i, def in ipairs(EVENTS) do
    if type(def) == "table" and def.id and def.target then
      local copy = deep_copy(def)
      copy.created = i
      stored_events[tostring(def.id)] = copy
    end
  end
  seeded = pcall(server.save, "events", stored_events) and pcall(server.save, "events_seeded", true)
  server.log("the events of the script file were copied into the storage: manage them on the web UI's Events page from now on.")
end
local def_source, event_errors = {}, {}   -- id -> "file" | "web" | "override"; id -> why a web event is refused
local all_defs, all_order = {}, {}        -- every known event by id, and the ids in order (switched-off ones too)
local MAX_WEB_EVENTS = 60

local function text_of(v, limit)
  return (tostring(v or ""):gsub("[%c\\]", " "):sub(1, limit))
end

local function words_of(list, convert, limit)
  if type(list) ~= "table" then return nil end
  local out = {}
  for _, v in ipairs(list) do
    v = convert(v)
    if v ~= nil and #out < limit then out[#out + 1] = v end
  end
  if #out == 0 then return nil end
  return out
end

local function int_in(v, low, high)
  v = tonumber(v)
  if not v or v ~= v then return nil end
  v = math.floor(v)
  if v < low or v > high then return nil end
  return v
end

local function item_spec(raw)
  if type(raw) ~= "table" then return nil end
  local spec = {}
  if raw.quality ~= nil then spec.quality = int_in(raw.quality, 0, 14) end
  if type(raw.category) == "string" and raw.category ~= "" then spec.category = text_of(raw.category, 20):lower() end
  if raw.level ~= nil then spec.level = int_in(raw.level, 1, 200) end
  return spec
end

local function milestones_of(raw)
  if type(raw) ~= "table" then return nil end
  local out = {}
  for _, m in ipairs(raw) do
    if type(m) == "table" and type(m.weights) == "table" and #out < 12 then
      local row = { at = tonumber(m.at), weights = {} }
      for tier, weight in pairs(m.weights) do row.weights[tostring(tier)] = tonumber(weight) end
      out[#out + 1] = row
    end
  end
  if #out == 0 then return nil end
  return out
end

-- A web event checked and cleaned into a definition like the ones of EVENTS, or nil and why not. The odds (milestones) are checked in
-- full when the event starts or is shown (build_milestones logs refused rows); here only their shape.
local function sanitize_event(id, raw)
  if type(raw) ~= "table" then return nil, "not an object" end
  if type(id) ~= "string" or #id > 40 or not id:match("^[a-z0-9][a-z0-9_%-]*$") then
    return nil, "the id must be 1-40 characters of a-z 0-9 _ -"
  end
  local target = int_in(raw.target, 1, 10000000)
  if not target then return nil, "the target must be a whole number from 1 to 10,000,000" end
  local duration = tonumber(raw.duration)
  if not duration or duration < 1 or duration > 30 * 86400 then return nil, "the duration must be 1 second to 30 days" end
  local reward = type(raw.reward) == "table" and raw.reward or {}
  local def = {
    id = id, title = text_of(raw.title ~= nil and raw.title ~= "" and raw.title or id, 60), goal = "kill", target = target, duration = math.floor(duration),
    created = tonumber(raw.created) or 0,
    names = words_of(raw.names, function(v) v = tostring(v):gsub("[%c]", " "):lower():sub(1, 40); if v:match("%S") then return v end end, 12),
    types = words_of(raw.types, function(v) return int_in(v, 0, 4294967295) end, 50),
    area = type(raw.area) == "string" and raw.area ~= "" and text_of(raw.area, 40) or nil,
    areas = words_of(raw.areas, function(v) if type(v) == "string" and v ~= "" then return text_of(v, 40) end end, 10),
    sectors = type(raw.sectors) == "table" and raw.sectors or nil,
    where = (raw.where == "victim" or raw.where == "player" or raw.where == "either") and raw.where or nil,
    text = type(raw.text) == "string" and raw.text:match("%S") and text_of(raw.text, 200) or nil,
    unit = type(raw.unit) == "string" and raw.unit:match("%S") and text_of(raw.unit, 30) or nil,
    min_contribution = int_in(raw.min_contribution, 1, 1000000) or 1,
    reward = {
      gold = int_in(reward.gold, 0, 2000000000) or 0, gold_each = int_in(reward.gold_each, 0, 2000000000) or 0,
      item = item_spec(reward.item), top_item = item_spec(reward.top_item), milestones = milestones_of(reward.milestones),
    },
  }
  if reward.on_fail == false then def.reward.on_fail = false end
  return def
end

-- Rebuilds `definitions` (in place: other functions keep their reference) from EVENTS, the web events and the switched-off ids.
local function rebuild_definitions()
  for i = #definitions, 1, -1 do definitions[i] = nil end
  all_defs, all_order, def_source, event_errors = {}, {}, {}, {}
  for i, def in ipairs(seeded and {} or EVENTS) do
    if type(def) == "table" and def.id and def.target then
      local id = tostring(def.id)
      if not all_defs[id] then all_order[#all_order + 1] = id end
      all_defs[id] = def
      def_source[id] = "file"
    else
      server.log("event #" .. i .. " has no id or target: ignored")
    end
  end
  local web, count = {}, 0
  for id, raw in pairs(stored_events) do
    count = count + 1
    local def, why = sanitize_event(tostring(id), raw)
    if count > MAX_WEB_EVENTS then def, why = nil, "more than " .. MAX_WEB_EVENTS .. " web events"
    elseif def then web[#web + 1] = def end
    if not def then
      event_errors[tostring(id)] = why
      server.log("web event " .. tostring(id) .. " refused: " .. why)
    end
  end
  table.sort(web, function(a, b) if a.created ~= b.created then return a.created < b.created end return a.id < b.id end)
  for _, def in ipairs(web) do
    if not all_defs[def.id] then all_order[#all_order + 1] = def.id; def_source[def.id] = "web" else def_source[def.id] = "override" end
    all_defs[def.id] = def
  end
  for _, id in ipairs(all_order) do
    if not events_off[id] then definitions[#definitions + 1] = all_defs[id] end
  end
end

rebuild_definitions()

-- Notices a change of the web events (the page writes events and events_off; each write bumps its _rev).
local function poll_events()
  local rev, off_rev = rev_of("events_rev"), rev_of("events_off_rev")
  if rev == events_rev and off_rev == events_off_rev then return end
  events_rev, events_off_rev = rev, off_rev
  stored_events, events_off = load_key("events", {}), load_key("events_off", {})
  rebuild_definitions()
  server.log("web events changed: " .. #definitions .. " event(s) in the rotation.")
end

local function save_state()
  local ok, err = pcall(server.save, "state", state)
  if ok then dirty = false else server.log("cannot save the state: " .. tostring(err)) end
  if rewardlog_dirty then
    local ok3, err3 = pcall(server.save, "rewardlog", rewardlog)
    if ok3 then rewardlog_dirty = false else server.log("cannot save the reward log: " .. tostring(err3)) end
  end
  if pending_dirty then
    local ok2, err2 = pcall(server.save, "pending", pending)
    if ok2 then pending_dirty = false else server.log("cannot save pending rewards: " .. tostring(err2)) end
  end
end

local function participants(ev)
  local n = 0
  for _ in pairs(ev.players) do n = n + 1 end
  return n
end

local function ranked(ev)
  local list = {}
  for uid, p in pairs(ev.players) do list[#list + 1] = { uid = uid, name = p.name or "?", n = p.n or 0 } end
  table.sort(list, function(a, b)
    if a.n ~= b.n then return a.n > b.n end
    if a.name ~= b.name then return a.name < b.name end
    return a.uid < b.uid
  end)
  return list
end

-- ---- net: the progress bar of the clients -------------------------------------------------------------------------------------

local channel = net and net.channel("worldevents:state", { server_only = true })

local function payload_for(player)
  local t = NOW()
  local ev = state.current
  if ev then
    local def = ev.def
    local mine = ev.players[player.uid]
    local odds_now, odds_at, odds_next, odds_next_at
    local milestones = def.reward.milestones
    if milestones then
      local current, nextm = milestone_at(milestones, math.min(100, ev.count * 100 / def.target))
      if current then odds_now, odds_at = odds_text(milestones[current]), milestones[current].at end
      if nextm then odds_next, odds_next_at = odds_text(milestones[nextm]), milestones[nextm].at end
    end
    return {
      v = 1, st = "active", n = ev.n, title = def.title, text = goal_text(def), unit = unit_of(def), count = ev.count, target = def.target,
      left = math.floor(math.max(0, ev.ends - t)), dur = def.duration, here = in_area(def, player), mine = mine and mine.n or 0,
      need = def.min_contribution, people = participants(ev), reward = reward_text(def), area = def.region and def.region.name or nil,
      odds = odds_now, odds_at = odds_at, odds_next = odds_next, odds_next_at = odds_next_at,
    }
  end
  local last = state.last
  if last and not last.cancelled and t - last.finished < CONFIG.result_hold then
    return {
      v = 1, st = last.success and "won" or "failed", n = last.n, title = last.title, text = last.text, unit = last.unit, count = last.count,
      target = last.target, hold = math.floor(CONFIG.result_hold - (t - last.finished)), here = in_area(last, player), people = last.people,
      area = last.region and last.region.name or nil,
    }
  end
  return { v = 1, st = "idle", next_in = math.floor(math.max(0, (state.next_start or t) - t)), here = false }
end

local function push(player)
  if not channel or not net.supports(player) then return end
  local ok, err = pcall(channel.send, channel, player, payload_for(player))
  if not ok then server.log("push failed: " .. tostring(err)) end
end

local function push_all()
  push_due = false
  if not channel then return end
  for _, player in ipairs(server.players()) do push(player) end
end

-- ---- rewards ----------------------------------------------------------------------------------------------------------------

-- Gives a reward entry { gold, items = { spec, ... } } to a player who is in the world. Never throws.
local function give(player, entry, why)
  local given = {}
  if (entry.gold or 0) > 0 then
    local ok, pile, err = pcall(server.drop_gold, player, entry.gold)
    if ok and pile then given[#given + 1] = entry.gold .. " gold"
    else server.log("no gold for " .. player.name .. ": " .. tostring(ok and err or pile)) end
  end
  for _, wrapped in ipairs(entry.items or {}) do
    local spec, roll = wrapped.spec or wrapped, wrapped.roll
    local ok, item, err = pcall(server.drop_item, player, spec)
    if ok and item then
      local name = (item.name and item.name ~= "") and item.name or "an item"
      given[#given + 1] = name
      if roll then
        for _, logged in ipairs(rewardlog) do
          if logged.id == roll then logged.item = name; rewardlog_dirty = true; break end
        end
      end
    else server.log("no item for " .. player.name .. ": " .. tostring(ok and err or item)) end
  end
  if #given > 0 then server.message(player, PREFIX .. why .. ": " .. table.concat(given, ", ") .. " dropped for you.") end
end

local function online_in_world(uid)
  for _, player in ipairs(server.players()) do
    if player.uid == uid and player.in_game then return player end
  end
end

local function grant(uid, name, entry, why)
  local player = online_in_world(uid)
  if player then give(player, entry, why); return end
  local waiting = pending[uid]
  if not waiting then
    local count = 0
    for _ in pairs(pending) do count = count + 1 end
    if count >= 500 then server.log("pending rewards full: no reward kept for " .. name); return end
    waiting = { name = name, gold = 0, items = {}, why = why }
    pending[uid] = waiting
  end
  waiting.gold = (waiting.gold or 0) + (entry.gold or 0)
  for _, spec in ipairs(entry.items or {}) do if #waiting.items < 20 then waiting.items[#waiting.items + 1] = spec end end
  pending_dirty = true
end

local function deliver_pending()
  if next(pending) == nil then return end
  for _, player in ipairs(server.players()) do
    local waiting = pending[player.uid]
    if waiting and player.in_game then
      pending[player.uid] = nil
      pending_dirty = true
      give(player, waiting, waiting.why or "Your reward")
    end
  end
end

-- Splits the reward of an event: who qualifies, how much gold (a completed event), who gets the prize (a completed event) and which
-- item each rolls: with milestones from the odds of the highest milestone reached (`percent`), else the fixed `item`.
local function reward_plan(ev, success, percent)
  local def, plan, total = ev.def, {}, 0
  local list = {}
  local milestones = def.reward.milestones
  local reached = milestones and milestone_at(milestones, percent)
  if not success and not (reached and def.reward.on_fail ~= false) then return plan end
  for _, p in ipairs(ranked(ev)) do
    if p.n >= def.min_contribution then list[#list + 1] = p; total = total + p.n end
  end
  for i, p in ipairs(list) do
    local entry = { uid = p.uid, name = p.name, n = p.n, gold = 0, items = {} }
    if success then
      entry.gold = def.reward.gold_each
      if def.reward.gold > 0 and total > 0 then entry.gold = entry.gold + math.floor(def.reward.gold * p.n / total) end
    end
    if milestones then
      if reached then
        local milestone = milestones[reached]
        local tier = roll_tier(milestone)
        state.roll_seq = (state.roll_seq or 0) + 1
        local spec = {}
        for k, v in pairs(def.reward.item or {}) do spec[k] = v end
        spec.quality = tier.quality
        table.insert(rewardlog, 1, { id = state.roll_seq, time = NOW(), event = def.title, name = p.name, tier = tier.name, at = milestone.at,
          chance = tier.percent, odds = odds_text(milestone) })
        entry.items[#entry.items + 1] = { spec = spec, roll = state.roll_seq }
        entry.roll_note = tier.name
      end
    elseif success and def.reward.item then
      entry.items[#entry.items + 1] = def.reward.item
    end
    if success and i == 1 and def.reward.top_item then entry.items[#entry.items + 1] = def.reward.top_item end
    plan[#plan + 1] = entry
  end
  while #rewardlog > 100 do table.remove(rewardlog) end
  rewardlog_dirty = true
  return plan
end

-- ---- the life of an event -----------------------------------------------------------------------------------------------------

local function finish(success)
  local ev = state.current
  if not ev then return end
  state.current = nil
  local def, top = ev.def, ranked(ev)
  local record = {
    n = ev.n, id = def.id, title = def.title, text = goal_text(def), unit = unit_of(def), success = success, count = ev.count, target = def.target,
    started = ev.started, finished = NOW(), people = #top, region = def.region, top = {},
  }
  for i = 1, math.min(5, #top) do record.top[i] = { name = top[i].name, n = top[i].n } end
  state.last = record
  table.insert(state.history, 1, { n = record.n, title = record.title, success = success, count = record.count, target = record.target,
    finished = record.finished, people = record.people, best = record.top[1] and record.top[1].name or nil })
  while #state.history > 10 do table.remove(state.history) end
  state.next_start = NOW() + CONFIG.cooldown

  local percent = math.min(100, ev.count * 100 / def.target)
  local milestones = def.reward.milestones
  local reached_index = milestones and milestone_at(milestones, percent)
  record.reached = reached_index and milestones[reached_index].at or nil
  local ok, plan = pcall(reward_plan, ev, success, percent)
  if ok then
    for _, entry in ipairs(plan) do
      local granted, err = pcall(grant, entry.uid, entry.name, entry, "Your reward for '" .. def.title .. "'")
      if not granted then server.log("reward failed for " .. entry.name .. ": " .. tostring(err)) end
    end
    record.rewarded = #plan
  else
    server.log("reward plan failed: " .. tostring(plan))
  end
  local odds_note = reached_index and (" Rewards were rolled with the " .. percent_text(milestones[reached_index].at) .. " odds: "
    .. odds_text(milestones[reached_index]) .. ".") or ""
  if success then
    local names = {}
    for i = 1, math.min(3, #top) do names[i] = top[i].name .. " (" .. top[i].n .. ")" end
    say(def.title .. " is complete! " .. ev.count .. "/" .. def.target .. " in " .. duration_text(NOW() - ev.started)
      .. ". " .. (record.rewarded or 0) .. " heroes share the reward." .. (#names > 0 and (" Best: " .. table.concat(names, ", ") .. ".") or "") .. odds_note)
  else
    say(def.title .. " has failed: " .. ev.count .. "/" .. def.target .. " when the time ran out."
      .. ((record.rewarded or 0) > 0 and (" " .. record.rewarded .. " heroes get an item for what the realm reached." .. odds_note) or ""))
  end
  save_state()
  push_all()
end

-- Is the event offered? Not when its area has no sector and no circle yet.
local function offered(index)
  local def = definitions[index]
  if not def or not (def.area or def.areas or def.sectors) then return def ~= nil end
  local region = region_of(def)
  if region_empty(region) then
    server.log("event " .. tostring(def.id) .. " is not offered: its area has no sector yet")
    return false
  end
  return true
end

local function next_index()
  local n = #definitions
  if n == 0 then return nil end
  local from = state.index or 0
  if state.last_id then
    for i, d in ipairs(definitions) do if tostring(d.id) == state.last_id then from = i; break end end
  end
  if from > n then from = 0 end
  if CONFIG.order == "random" and n > 1 then
    for _ = 1, 8 do
      local pick = server.random(n - 1)
      if from > 0 and pick >= from then pick = pick + 1 end
      if offered(pick) then return pick end
    end
  end
  for step = 1, n do
    local index = ((from + step - 1) % n) + 1
    if offered(index) then return index end
  end
  return nil
end

local function begin_event(def, index)
  state.seq = state.seq + 1
  state.index = index or state.index or 0
  state.last_id = tostring(def.id)
  local snap, t = snapshot(def), NOW()
  snap.region = region_of(def)
  state.current = { n = state.seq, def = snap, started = t, ends = t + snap.duration, count = 0, players = {}, marks = {} }
  say(snap.title .. " begins: " .. goal_text(snap) .. " within " .. duration_text(snap.duration) .. "! Reward: "
    .. (reward_text(snap) ~= "" and reward_text(snap) or "glory") .. ". Type !event for the progress.")
  save_state()
  push_all()
end

local function start_event(index)
  local def = definitions[index]
  if def then begin_event(def, index) end
end

-- Ends the running event without rewards (admin): it is recorded as cancelled and the next one starts after the usual cooldown.
local function cancel_event()
  local ev = state.current
  if not ev then return false end
  state.current = nil
  local def, top = ev.def, ranked(ev)
  local record = {
    n = ev.n, id = def.id, title = def.title, text = goal_text(def), unit = unit_of(def), success = false, cancelled = true, count = ev.count,
    target = def.target, started = ev.started, finished = NOW(), people = #top, region = def.region, top = {},
  }
  state.last = record
  table.insert(state.history, 1, { n = record.n, title = record.title, success = false, cancelled = true, count = record.count, target = record.target,
    finished = record.finished, people = record.people })
  while #state.history > 10 do table.remove(state.history) end
  state.next_start = NOW() + CONFIG.cooldown
  say(def.title .. " was cancelled by an admin.")
  save_state()
  push_all()
  return true
end

-- The rules and odds of the reward of an event definition, as lines of text.
local function reward_lines(def)
  local lines, r = {}, def.reward or {}
  local milestones = r.milestones
  if milestones then
    lines[#lines + 1] = "Item odds by progress (rolled once when the event ends, with the highest milestone reached):"
    for _, m in ipairs(milestones) do lines[#lines + 1] = "  from " .. percent_text(m.at) .. ": " .. odds_text(m) end
    lines[#lines + 1] = "Only heroes with at least " .. def.min_contribution .. " kills take part. Nothing else changes the odds."
      .. (r.on_fail == false and "" or " An event that runs out of time still rolls items with the odds it reached.")
  elseif r.item then
    lines[#lines + 1] = "Everyone with at least " .. def.min_contribution .. " kills gets an item"
      .. (r.item.quality and (" of generation quality " .. r.item.quality) or " of the game's usual rarity") .. "."
  end
  if (r.gold or 0) > 0 or (r.gold_each or 0) > 0 then
    lines[#lines + 1] = "Gold (completed event only): " .. (r.gold_each or 0) .. " each plus " .. (r.gold or 0) .. " shared by kills."
  end
  if r.top_item then lines[#lines + 1] = "The best contributor gets a prize item of generation quality " .. tostring(r.top_item.quality or "?") .. " (completed event only)." end
  return lines
end

local function status_lines(player)
  local t, lines = NOW(), {}
  local ev = state.current
  if ev then
    local def = ev.def
    local mine = ev.players[player.uid]
    lines[#lines + 1] = string.format("%s: %s - %d/%d (%d%%), %s left.", def.title, goal_text(def), ev.count, def.target,
      math.min(100, math.floor(ev.count * 100 / def.target)), duration_text(ev.ends - t))
    local own = mine and mine.n or 0
    lines[#lines + 1] = string.format("Your share: %d (%d needed for the reward)%s.", own, def.min_contribution,
      in_area(def, player) and "" or "; you are not in the right sector")
  else
    local last = state.last
    if last then
      lines[#lines + 1] = string.format("%s %s (%d/%d).", last.title, last.success and "was completed" or "failed", last.count, last.target)
    end
    lines[#lines + 1] = #definitions == 0 and "There are no world events configured."
      or ("No world event is running. The next one starts in about " .. duration_text((state.next_start or t) - t) .. ".")
  end
  return lines
end

-- ---- events ------------------------------------------------------------------------------------------------------------------

server.on("kill", function(player, monster)
  if CONFIG.debug_kills then
    server.log(string.format("kill: %s (type %s) at sector %s x %s y %s by %s in sector %s", tostring(monster.name), tostring(monster.type),
      tostring(monster.sector), tostring(monster.x), tostring(monster.y), player.character, tostring(player.sector)))
  end
  local ev = state.current
  if not ev or not counts(ev.def, player, monster) then return end
  ev.count = ev.count + 1
  local mine = ev.players[player.uid]
  if not mine and participants(ev) < CONFIG.max_contributors then
    mine = { n = 0 }
    ev.players[player.uid] = mine
  end
  if mine then mine.n = mine.n + 1; mine.name = player.character end
  dirty, push_due = true, true
  local percent = ev.count * 100 / ev.def.target
  for _, mark in ipairs(CONFIG.milestones) do
    if percent >= mark and ev.count < ev.def.target and not ev.marks[tostring(mark)] then
      ev.marks[tostring(mark)] = true
      say(ev.def.title .. ": " .. mark .. "% (" .. ev.count .. "/" .. ev.def.target .. "), " .. duration_text(ev.ends - NOW()) .. " left.")
    end
  end
  local milestones = ev.def.reward.milestones
  if milestones then
    local index = milestone_at(milestones, math.min(100, percent))
    if index and not ev.marks["r" .. index] then
      ev.marks["r" .. index] = true
      say(ev.def.title .. " " .. percent_text(milestones[index].at) .. ": reward odds now: " .. odds_text(milestones[index]) .. ".")
    end
  end
  if ev.count >= ev.def.target then finish(true) end
end)

server.on("enter", function(player)
  if state.current then
    for _, line in ipairs(status_lines(player)) do server.message(player, PREFIX .. line) end
  end
  deliver_pending()
  push(player)
end)

-- ---- finding and recording areas (chat) -------------------------------------------------------------------------------------

local function is_admin(player)
  if #CONFIG.admins == 0 then return player.host end
  local name = (player.character or ""):lower()
  for _, admin in ipairs(CONFIG.admins) do if tostring(admin):lower() == name then return true end end
  return false
end

local recording = {}          -- slot -> { id, name, ids = set, circle }

local function areas_containing(player)
  local found = {}
  local seen = {}
  for _, source in ipairs({ recorded, AREAS }) do
    for id, a in pairs(source) do
      if not seen[id] then
        seen[id] = true
        local ids, circles = {}, {}
        add_parts(a, ids, circles)
        if ids[player.sector or 0] then found[#found + 1] = id
        else for _, c in ipairs(circles) do if in_circle(c, player.x, player.y) then found[#found + 1] = id; break end end end
      end
    end
  end
  table.sort(found)
  return found
end

local function where_text(player)
  local text = "You are in sector " .. (player.sector or 0)
  local cell = player.sector and server.sector_cell(player.sector)
  if cell then text = text .. " (grid cell " .. cell.x .. "," .. cell.y .. ")" end
  if (player.x or 0) ~= 0 or (player.y or 0) ~= 0 then text = text .. ", position x " .. player.x .. " y " .. player.y end
  local in_areas = areas_containing(player)
  if #in_areas > 0 then text = text .. ", area " .. table.concat(in_areas, ", ") end
  return text .. "."
end

local function save_areas()
  local ok, err = pcall(server.save, "areas", recorded)
  if ok then
    areas_rev = areas_rev + 1
    pcall(server.save, "areas_rev", areas_rev)
  else
    server.log("cannot save the areas: " .. tostring(err))
  end
end

local function recording_area(rec)
  local ids = sorted_ids(rec.ids)
  local a = { name = rec.name, sectors = {} }
  local i = 1
  while i <= #ids do
    local j = i
    while ids[j + 1] == ids[j] + 1 do j = j + 1 end
    if j - i >= 2 then a.sectors[#a.sectors + 1] = { ids[i], ids[j] }
    else for k = i, j do a.sectors[#a.sectors + 1] = ids[k] end end
    i = j + 1
  end
  if rec.circle then a.circle = rec.circle end
  if rec.near then a.near = rec.near end
  return a
end

local function finish_recording(player, rec)
  recorded[rec.id] = recording_area(rec)
  save_areas()
  local line = snippet(rec.id, recorded[rec.id])
  server.log("area recorded: " .. line)
  server.message(player, "Area " .. rec.id .. " saved (" .. set_size(rec.ids) .. " sectors" .. (rec.circle and ", a circle" or "") .. ").")
  server.message(player, line)
end

local HELP = "!area start <id> [name] | add | remove | circle [radius] | near <n> | stop | cancel | show <id> | list | drop <id>"

local function area_command(player, args)
  if not is_admin(player) then server.message(player, "Only the admins may record areas."); return end
  local cmd, rest = args:match("^(%S*)%s*(.-)%s*$")
  local rec = recording[player.slot]
  if cmd == "start" then
    local id, name = rest:match("^([%w_%-]+)%s*(.-)$")
    if not id then server.message(player, "!area start <id> [name]"); return end
    rec = { id = id:lower(), name = name ~= "" and clean_name(name) or id, ids = {} }
    local old = recorded[rec.id]
    if old then add_parts(old, rec.ids, {}); rec.circle = old.circle; rec.near = old.near; rec.name = old.name or rec.name end
    if (player.sector or 0) ~= 0 then rec.ids[player.sector] = true end
    recording[player.slot] = rec
    server.message(player, "Recording " .. rec.id .. ": every sector you walk into is added. !area stop saves it. " .. set_size(rec.ids) .. " sectors so far.")
  elseif not rec and (cmd == "add" or cmd == "remove" or cmd == "circle" or cmd == "near" or cmd == "stop" or cmd == "cancel") then
    server.message(player, "Not recording: !area start <id> [name] first.")
  elseif cmd == "add" then
    if (player.sector or 0) ~= 0 then rec.ids[player.sector] = true end
    server.message(player, "Sector " .. (player.sector or 0) .. " added, " .. set_size(rec.ids) .. " sectors.")
  elseif cmd == "remove" then
    rec.ids[player.sector or 0] = nil
    server.message(player, "Sector " .. (player.sector or 0) .. " removed, " .. set_size(rec.ids) .. " sectors.")
  elseif cmd == "circle" then
    local radius = tonumber(rest) or (rec.circle and rec.circle.r) or 1500
    if (player.x or 0) == 0 and (player.y or 0) == 0 then
      server.message(player, "The server does not know your position yet (it may lag: move a little, or use the sector-tools mod to read it).")
    else
      rec.circle = { x = player.x, y = player.y, r = math.floor(radius) }
      server.message(player, "Circle at x " .. player.x .. " y " .. player.y .. ", radius " .. math.floor(radius) .. ".")
    end
  elseif cmd == "near" then
    rec.near = math.max(0, math.min(5, math.floor(tonumber(rest) or 0)))
    if rec.near == 0 then rec.near = nil end
    server.message(player, "Neighbours within " .. (rec.near or 0) .. " sector cells " .. (rec.near and "included." or "not included."))
  elseif cmd == "stop" then
    recording[player.slot] = nil
    finish_recording(player, rec)
  elseif cmd == "cancel" then
    recording[player.slot] = nil
    server.message(player, "Recording of " .. rec.id .. " dropped.")
  elseif cmd == "show" then
    local a = area_def(rest:lower())
    server.message(player, a and snippet(rest:lower(), a) or ("No area " .. rest))
  elseif cmd == "drop" then
    if recorded[rest:lower()] then
      recorded[rest:lower()] = nil
      save_areas()
      server.message(player, "Area " .. rest:lower() .. " dropped.")
    else
      server.message(player, "No recorded area " .. rest .. " (areas in the script file are edited there).")
    end
  elseif cmd == "list" then
    local names = {}
    for id in pairs(recorded) do names[#names + 1] = id .. "*" end
    for id in pairs(AREAS) do if not recorded[id] then names[#names + 1] = id end end
    table.sort(names)
    server.message(player, "Areas (* recorded in game): " .. (#names > 0 and table.concat(names, ", ") or "none"))
  else
    server.message(player, HELP)
  end
end

server.on("sector", function(player)
  local rec = recording[player.slot]
  if rec and (player.sector or 0) ~= 0 and not rec.ids[player.sector] then
    rec.ids[player.sector] = true
    server.message(player, "Area " .. rec.id .. ": sector " .. player.sector .. " added (" .. set_size(rec.ids) .. ").")
  end
  push(player)
end)

server.on("leave", function(player) recording[player.slot] = nil end)

server.on("chat", function(player, text)
  if text == "!event" then
    for _, line in ipairs(status_lines(player)) do server.message(player, PREFIX .. line) end
    return true
  elseif text == "!event rewards" or text == "!event odds" then
    local ev = state.current
    if not ev then server.message(player, PREFIX .. "No world event is running."); return true end
    for _, line in ipairs(reward_lines(ev.def)) do server.message(player, PREFIX .. line) end
    local milestones = ev.def.reward.milestones
    if milestones then
      local index = milestone_at(milestones, math.min(100, ev.count * 100 / ev.def.target))
      server.message(player, PREFIX .. (index and ("Now (" .. percent_text(ev.count * 100 / ev.def.target) .. " done): " .. odds_text(milestones[index]))
        or "Not at the first milestone yet."))
    end
    return true
  elseif text == "!sector" or text == "!here" then
    server.message(player, where_text(player))
    return true
  elseif text == "!area" or text:sub(1, 6) == "!area " then
    area_command(player, text:sub(7))
    return true
  end
end)

-- The clients ask for the state when they have said HELLO (and now and then again); at most once a second each.
local last_hello = {}
if channel then
  channel:on(function(player)
    local t = NOW()
    if (last_hello[player.slot] or 0) + 1 > t then return end
    last_hello[player.slot] = t
    push(player)
  end)
end

-- Commands of the Events page (key "control", counted by control_rev; the number done is kept in the state, so each runs once).
local function run_control(cmd)
  if type(cmd) ~= "table" then return false, "no command" end
  if cmd.cmd == "start" then
    local def = all_defs[tostring(cmd.event)]
    if not def then return false, "no such event" end
    if state.current then return false, "an event is already running" end
    if def.area or def.areas or def.sectors then
      local region = region_of(def)
      if region_empty(region) then return false, "its area has no sector yet" end
    end
    local index = 0
    for i, d in ipairs(definitions) do if d == def then index = i end end
    begin_event(def, index > 0 and index or nil)
    return true, "started " .. tostring(def.title or def.id)
  elseif cmd.cmd == "cancel" then
    if not cancel_event() then return false, "no event is running" end
    return true, "cancelled"
  elseif cmd.cmd == "next" then
    if state.current then return false, "an event is running" end
    state.next_start = NOW()
    dirty = true
    return true, "the next event starts within seconds"
  end
  return false, "unknown command"
end

local function poll_control()
  local rev = rev_of("control_rev")
  if rev <= (state.control_rev or 0) then return end
  state.control_rev = rev
  dirty = true
  local ok, done, message = pcall(run_control, load_key("control", nil))
  if not ok then done, message = false, tostring(done) end
  pcall(server.save, "control_result", { rev = rev, ok = done and true or false, message = message or "", at = NOW() })
  server.log("web command " .. rev .. ": " .. tostring(message))
end

local ticks = 0
local function tick()
  local t = NOW()
  ticks = ticks + 1
  do
    local ok, rev = pcall(server.load, "areas_rev", 0)
    if ok and tonumber(rev) and rev ~= areas_rev then
      areas_rev = rev
      local loaded = server.load("areas", {})
      recorded = type(loaded) == "table" and loaded or {}
    end
  end
  poll_events()
  poll_control()
  local ev = state.current
  if ev and t >= ev.ends then finish(false) end
  if not state.current and t >= (state.next_start or 0) and #definitions > 0 and #server.players() >= CONFIG.min_players then
    start_event(next_index())
  end
  deliver_pending()
  if dirty or pending_dirty then save_state() end
  if push_due or (state.current and ticks % 3 == 0) then push_all() end
end

server.every(2, tick)

-- Hot reload and shutdown: everything is written now (and put on the disk), the next version of this script carries on from the storage.
server.on("unload", function(reason)
  for slot in pairs(recording) do server.message(slot, "World events were reloaded: your area recording was dropped, !area start again.") end
  pcall(server.save, "state", state)
  pcall(server.save, "rewardlog", rewardlog)
  pcall(server.save, "pending", pending)
  server.flush()
end)

-- A server that was down while an event ran out closes it now; a stale schedule is pulled in.
server.on("start", function()
  local t = NOW()
  local ev = state.current
  if ev and t >= ev.ends then finish(false) end
  if (state.next_start or 0) > t + CONFIG.cooldown + CONFIG.first_delay then state.next_start = t + CONFIG.first_delay end
  save_state()
end)

-- ---- the public state, the endpoint and the dashboard page ----------------------------------------------------------------------

local function public_state()
  local t, out = NOW(), { time = NOW() }
  local ev = state.current
  if ev then
    local top = {}
    for i, p in ipairs(ranked(ev)) do
      if i > 10 then break end
      top[i] = { name = p.name, kills = p.n }
    end
    out.active = {
      n = ev.n, id = ev.def.id, title = ev.def.title, text = goal_text(ev.def), count = ev.count, target = ev.def.target,
      percent = math.min(100, math.floor(ev.count * 100 / ev.def.target)), left = math.floor(math.max(0, ev.ends - t)), ends = ev.ends,
      area = ev.def.region and ev.def.region.name or nil, participants = participants(ev), top = top, reward = reward_text(ev.def),
    }
  else
    out.next_in = math.floor(math.max(0, (state.next_start or t) - t))
  end
  local function odds_json(def)
    local milestones = def.reward and def.reward.milestones
    if not milestones then return nil end
    local list = {}
    for i, m in ipairs(milestones) do
      local chances = {}
      for j, o in ipairs(m.odds) do chances[j] = { tier = o.id, name = o.name, quality = o.quality, weight = o.weight, percent = o.percent } end
      list[i] = { at = m.at, chances = chances }
    end
    return list
  end
  if out.active then
    out.active.reward_odds = odds_json(ev.def)
    out.active.reward_rules = reward_lines(ev.def)
    out.active.odds_now = (function()
      local m = ev.def.reward.milestones
      return m and milestone_at(m, math.min(100, ev.count * 100 / ev.def.target)) or nil
    end)()
  end
  out.rewards = {}
  for i = 1, math.min(20, #rewardlog) do
    local e = rewardlog[i]
    out.rewards[i] = { time = e.time, event = e.event, name = e.name, tier = e.tier, item = e.item, at = e.at, chance = e.chance }
  end
  out.last = state.last and {
    n = state.last.n, title = state.last.title, success = state.last.success, count = state.last.count, target = state.last.target,
    finished = state.last.finished, top = state.last.top, cancelled = state.last.cancelled,
  } or nil
  out.history = state.history
  out.events = {}
  for i, def in ipairs(definitions) do
    local snap = snapshot(def)
    out.events[i] = { id = def.id, title = def.title, text = goal_text(snap), duration = def.duration, reward_odds = odds_json(snap) }
  end
  return out
end

server.endpoint("state", function() return public_state() end,
  { public = true, description = "the running event, the last result and the history as JSON (character names only)" })

-- For the area editor page of the web UI (admin only, not public): the areas as defined (the script file's and the saved ones, which win)
-- and where the heroes are. The page saves areas into this mod's storage through POST /api/mod-store.
local function area_json(a, source)
  return { name = a.name, sectors = a.sectors or {}, ranges = a.ranges, rect = a.rect, near = a.near, circle = a.circle, source = source }
end

server.endpoint("areas", function()
  local out = {}
  for id, a in pairs(AREAS) do out[id] = area_json(a, "file") end
  for id, a in pairs(recorded) do if type(a) == "table" then out[id] = area_json(a, "saved") end end
  local events = {}
  for i, def in ipairs(definitions) do events[i] = { id = def.id, title = def.title, area = def.area, areas = def.areas } end
  return { areas = out, events = events }
end, { description = "the named areas (script file and saved), for the area editor" })

-- For the Events page of the web UI (admin only): every event with its editable fields, where it comes from and whether it is in the
-- rotation, the areas and rarity tiers to pick from, and the state of the running event and of the last command.
local function editable(def)
  local reward = def.reward or {}
  local milestones
  if type(reward.milestones) == "table" then
    milestones = {}
    for i, m in ipairs(reward.milestones) do milestones[i] = { at = m.at, weights = m.weights } end
  end
  return {
    id = tostring(def.id), title = def.title, target = def.target, duration = def.duration, names = def.names, types = def.types,
    area = def.area, areas = def.areas, sectors = def.sectors, where = def.where, text = def.text, unit = def.unit, min_contribution = def.min_contribution,
    created = def.created,
    reward = { gold = reward.gold, gold_each = reward.gold_each, item = reward.item, top_item = reward.top_item, milestones = milestones, on_fail = reward.on_fail },
  }
end

server.endpoint("events", function()
  local list = {}
  for i, id in ipairs(all_order) do
    local def = all_defs[id]
    list[i] = { id = id, source = def_source[id], enabled = not events_off[id], def = editable(def),
      running = state.current ~= nil and state.current.def.id == id, goal = goal_text(def) }
  end
  for id, why in pairs(event_errors) do list[#list + 1] = { id = id, source = "web", enabled = false, invalid = why, def = stored_events[id] } end
  local areas = {}
  for id, a in pairs(AREAS) do areas[id] = clean_name(a.name or id) end
  for id, a in pairs(recorded) do if type(a) == "table" then areas[id] = clean_name(a.name or id) end end
  local tiers = {}
  for i, tier in ipairs(TIERS) do tiers[i] = { id = tier.id, name = tier.name } end
  local ev = state.current
  local running
  if ev then running = { id = ev.def.id, title = ev.def.title, count = ev.count, target = ev.def.target, left = math.floor(math.max(0, ev.ends - NOW())) } end
  return {
    events = list, areas = areas, tiers = tiers, running = running, next_in = not ev and math.floor(math.max(0, (state.next_start or NOW()) - NOW())) or nil,
    control = load_key("control_result", nil), config = { order = CONFIG.order, cooldown = CONFIG.cooldown, min_players = CONFIG.min_players },
    max_web_events = MAX_WEB_EVENTS,
  }
end, { description = "the events (file and web) with their editable fields, for the Events page" })

server.endpoint("whereabouts", function()
  local list = {}
  for i, player in ipairs(server.players()) do
    list[i] = { name = player.character, sector = player.sector or 0, x = player.x or 0, y = player.y or 0 }
  end
  return { players = list }
end, { description = "where the heroes are (sector and position), for the area editor" })

-- The page head and its style are world_events.html next to this script (server.file).

local function section(title, body)
  return "<section><h2>" .. server.html(title) .. "</h2>" .. body .. "</section>"
end

server.page("", function()
  local h, s = server.html, public_state()
  local parts = {
    server.file("world_events.html"),
    '<h1>World events</h1><main>',
  }
  local a = s.active
  if a then
    local body = { "<b>", h(a.title), "</b><br>", h(a.text), '<div class="bar"><i style="width:', tostring(a.percent), '%"></i></div>',
      string.format("%d / %d (%d%%) &middot; %s left &middot; %d heroes took part", a.count, a.target, a.percent, h(duration_text(a.left)), a.participants) }
    if a.sectors then body[#body + 1] = "<br><small>Counts in sector " .. h(table.concat(a.sectors, ", ")) .. " only</small>" end
    if a.reward ~= "" then body[#body + 1] = "<br><small>Reward: " .. h(a.reward) .. "</small>" end
    parts[#parts + 1] = section("Running now", table.concat(body))
    local rows = {}
    for _, p in ipairs(a.top) do rows[#rows + 1] = "<li><b>" .. h(p.name) .. "</b> " .. p.kills .. "</li>" end
    parts[#parts + 1] = section("Top contributors", #rows > 0 and ("<ol>" .. table.concat(rows) .. "</ol>") or "<small>nobody yet</small>")
  else
    parts[#parts + 1] = section("Running now", "<small>No event right now. The next one starts in about " .. h(duration_text(s.next_in or 0)) .. ".</small>")
  end
  if a and a.reward_odds then
    local rows_o = { "<table><tr><th>from</th>" }
    for _, tier in ipairs(TIERS) do rows_o[#rows_o + 1] = "<th>" .. h(tier.name) .. "</th>" end
    rows_o[#rows_o + 1] = "</tr>"
    for i, m in ipairs(a.reward_odds) do
      local chance = {}
      for _, c in ipairs(m.chances) do chance[c.tier] = c.percent end
      rows_o[#rows_o + 1] = '<tr' .. (i == a.odds_now and ' class="now"' or "") .. "><td>" .. h(percent_text(m.at)) .. "</td>"
      for _, tier in ipairs(TIERS) do
        rows_o[#rows_o + 1] = "<td>" .. (chance[tier.id] and h(percent_text(chance[tier.id])) or "&ndash;") .. "</td>"
      end
      rows_o[#rows_o + 1] = "</tr>"
    end
    rows_o[#rows_o + 1] = "</table>"
    for _, line in ipairs(a.reward_rules or {}) do rows_o[#rows_o + 1] = "<br><small>" .. h(line) .. "</small>" end
    parts[#parts + 1] = section("Reward odds", table.concat(rows_o))
  elseif a then
    local lines_o = {}
    for _, line in ipairs(a.reward_rules or {}) do lines_o[#lines_o + 1] = "<small>" .. h(line) .. "</small>" end
    if #lines_o > 0 then parts[#parts + 1] = section("Reward", table.concat(lines_o, "<br>")) end
  end
  local given = {}
  for _, e in ipairs(s.rewards or {}) do
    given[#given + 1] = string.format("<li><b>%s</b> %s <small>%s%s, rolled at %s</small></li>", h(e.name), h(e.tier),
      e.item and (h(e.item) .. ", ") or "", h(e.event), h(percent_text(e.at)))
  end
  parts[#parts + 1] = section("Recent rewards", #given > 0 and ("<ul>" .. table.concat(given) .. "</ul>") or "<small>none yet</small>")
  local rows = {}
  for _, e in ipairs(s.history) do
    rows[#rows + 1] = string.format('<li><b>%s</b> <span class="%s">%s</span> <small>%d/%d, %d heroes%s</small></li>', h(e.title),
      e.success and "ok" or "bad", e.success and "completed" or "failed", e.count, e.target, e.people or 0, e.best and (", best: " .. h(e.best)) or "")
  end
  parts[#parts + 1] = section("Recent events", #rows > 0 and ("<ul>" .. table.concat(rows) .. "</ul>") or "<small>none yet</small>")
  local upcoming = {}
  for _, e in ipairs(s.events) do upcoming[#upcoming + 1] = "<li><b>" .. h(e.title) .. "</b> <small>" .. h(e.text) .. ", " .. h(duration_text(e.duration)) .. "</small></li>" end
  parts[#parts + 1] = section("In the rotation", "<ul>" .. table.concat(upcoming) .. "</ul>")
  parts[#parts + 1] = "</main></body></html>"
  return table.concat(parts)
end, { public = true, title = "World events" })
