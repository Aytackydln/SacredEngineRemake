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
  max_spawn_n = 40,         -- hooks: most creatures one spawn action may ask for (for each hero, or in all)
  max_item_n = 10,          -- hooks: most items one item action drops for each hero
  max_spawn_per_minute = 120, -- hooks: most creatures all hooks together may ask the clients for in a minute
  announce_hooks = true,    -- hooks: say in chat when one fires (what, around whom)
}

-- Named AREAS: sets of sectors an event can be tied to (`area = "silver_creek"` in an event, or `areas = {...}` for several).
-- Sector ids are slots of the game's sector table, NOT coordinates: neighbours in the world are not neighbours in the numbers. To
-- find the numbers: stand in a place and type !sector (or !here), or set debug_kills = true and read the log, or record an area by
-- walking with !area start, or use the area editor page of the web UI. An area is the UNION of its parts:
--   sectors   sector ids and inclusive ranges: { 2909, 2910, { 2915, 2920 } }   (`ranges` = { { 2915, 2920 } } is the same)
--   rect      { x1, y1, x2, y2 }: every sector of those cells of the 128 x 128 sector grid (needs --game-dir)
--   near      n: plus every sector within n grid cells of the ones above (needs --game-dir)
--   regions   { 3, 5 }: every sector of those game regions (needs --game-dir; `server.regions()` lists them with their names, the area editor and
--             the Events page have a picker). The sector -> region mapping is read from the game files, so server and clients agree.
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

-- ---- hooks: what happens at a moment of an event ----------------------------------------------------------------------------------
-- A hook is a TRIGGER with a list of ACTIONS:
--   triggers  at = percent of the goal | at_time = seconds into the event | on = "start" / "success" / "fail" |
--             kills = { creature = "Orc Shaman" (a name or a type number), count = 5, per_player = false }: the Nth such creature
--             defeated in the event (event-wide, or by one hero) -- the hero who killed it is the "killer"
--   actions   { action = "spawn", n, per_player, kinds = { { type = "Orc Warrior", weight = 3 }, ... }, radius, target, text }
--             { action = "item", spec = { quality, category, level } (or { type = "Healing Potion" } where the Sacred Gear mod runs it), n, target }
--             { action = "message", text, target }      { action = "gold", amount, target }
--             { action = "boss", name, kinds = { { type = "Orc Warrior" } } | near = "Orc", health, damage, taken, deflect, reflect, regen, radius,
--               reward = { gold, item = { quality, category, level } }, text, window, target }: a WORLD BOSS. Everybody's client shows its name and
--               health bar (`worldevents:boss`); the target hero's game is asked to make it (message `worldevents:spawn` with a `boss`): Sacred Gear
--               can only create creatures in a game of its own (single player / LAN host), so in a dedicated server game nobody can make it
--               yet; the server only announces it and calls it defeated when a hero kills a creature of its kind (`kinds[1].type` / `near`)
--               within `window` seconds (default 1800); the reward (gold, an item by rarity) goes to that hero. The server has no creature and no
--               health of the boss: the bar shows its name and "Hunt" without numbers.
--   targets   killer (the hero who made the triggering kill) | host | random (a hero in the event's area, anybody online for an event
--             without area) | owner (the owner of the area's sector, the only hero there, else the one with most kills) |
--             players (every hero in the area, everybody for an event without area) | global (everybody)
-- Hooks fire ONCE per event (kept with the running event, so a restart never repeats them; progress that jumps past several makes all
-- of them fire), are limited (see CONFIG: creatures per wave and per minute) and are shown on the dashboard, in !event hooks and in the
-- client's tooltip. This part checks them (never throws).

local region_name        -- defined with the areas below (the name of a game region)
local HOOK_LIMITS = { hooks = 12, actions = 6, kinds = 8 }
local HOOK_TARGETS = { killer = true, host = true, random = true, owner = true, players = true, global = true }
local HOOK_ACTIONS = { spawn = true, item = true, message = true, gold = true, boss = true }
local HOOK_DEFAULT_TARGET = { spawn = "owner", item = "killer", message = "global", gold = "killer", boss = "owner" }
-- What a boss may be made of (the same limits as the Sacred Gear mod): name = { lowest, highest, default }.
local BOSS_LIMITS = { health = { 1, 1000, 10 }, damage = { 0, 50, 2 }, taken = { 0.05, 10, 1 }, deflect = { 0, 0.9, 0 }, reflect = { 0, 1, 0 }, regen = { 0, 0.2, 0 } }

local function hk_int(v, low, high)
  v = tonumber(v)
  if not v or v ~= v then return nil end
  v = math.floor(v)
  if v < low or v > high then return nil end
  return v
end

local function hk_clamp(v, low, high, default)
  v = tonumber(v)
  if not v or v ~= v then return default end
  return math.max(low, math.min(high, math.floor(v)))
end

local function hk_text(v, limit)
  return (tostring(v or ""):gsub("[%c\\]", " "):sub(1, limit))
end

-- A creature kind ({ type = name or number, weight }) or nil.
local function hk_kind(raw)
  if type(raw) ~= "table" then return nil end
  local t = raw.type
  if type(t) == "number" then t = hk_int(t, 1, 4294967295)
  elseif type(t) == "string" and t:match("%S") then t = hk_text(t, 48):match("^%s*(.-)%s*$")
  else t = nil end
  if t == nil or t == "" then return nil end
  return { type = t, weight = hk_int(raw.weight, 1, 1000) or 1 }
end

local function hk_num(v, low, high, default)
  v = tonumber(v)
  if not v or v ~= v then return default end
  return math.max(low, math.min(high, v))
end

-- The boss of a `boss` action, checked (never throws): name, kinds / near, multipliers, reward, window.
local function hk_boss(raw)
  local b = { kinds = {} }
  for _, k in ipairs(type(raw.kinds) == "table" and raw.kinds or {}) do
    local entry = hk_kind(k)
    if entry and #b.kinds < HOOK_LIMITS.kinds then b.kinds[#b.kinds + 1] = entry end
  end
  if type(raw.near) == "string" and raw.near:match("%S") then b.near = hk_text(raw.near, 48):match("^%s*(.-)%s*$") end
  if #b.kinds == 0 and not b.near then return nil, "a boss needs a creature kind (kinds) or creatures to promote (near)" end
  local first = b.kinds[1] and tostring(b.kinds[1].type) or b.near
  b.name = hk_text(raw.name ~= nil and raw.name or first, 40):match("^%s*(.-)%s*$")
  if b.name == "" then b.name = hk_text(first, 40) end
  b.radius = hk_clamp(raw.radius, 50, 3000, 400)
  b.window = hk_clamp(raw.window, 1, 86400, 1800)
  for key, range in pairs(BOSS_LIMITS) do b[key] = hk_num(raw[key], range[1], range[2], range[3]) end
  if type(raw.reward) == "table" then
    local r = { gold = hk_int(raw.reward.gold, 1, 2000000000) }
    if type(raw.reward.item) == "table" then
      local spec, item = raw.reward.item, {}
      if spec.quality ~= nil then item.quality = hk_int(spec.quality, 0, 14) end
      if type(spec.category) == "string" and spec.category ~= "" then item.category = hk_text(spec.category, 20):lower() end
      if spec.level ~= nil then item.level = hk_int(spec.level, 1, 200) end
      if next(item) ~= nil then r.item = item end
    end
    if raw.reward.text ~= nil and tostring(raw.reward.text):match("%S") then r.text = hk_text(raw.reward.text, 120) end
    if r.gold or r.item then b.reward = r end
  end
  return b
end

local function hk_action(raw, hook_no, action_no)
  if type(raw) ~= "table" then return nil, "not an object" end
  local kind = raw.action
  if not HOOK_ACTIONS[kind] then return nil, "unknown action '" .. tostring(kind) .. "'" end
  local a = { action = kind, target = HOOK_TARGETS[raw.target] and raw.target or HOOK_DEFAULT_TARGET[kind] }
  if kind == "spawn" then
    a.n = hk_clamp(raw.n, 1, CONFIG.max_spawn_n, 1)
    a.per_player = raw.per_player ~= false
    a.radius = hk_clamp(raw.radius, 50, 3000, 400)
    a.kinds = {}
    for _, k in ipairs(type(raw.kinds) == "table" and raw.kinds or {}) do
      local kind_entry = hk_kind(k)
      if kind_entry and #a.kinds < HOOK_LIMITS.kinds then a.kinds[#a.kinds + 1] = kind_entry end
    end
    if #a.kinds == 0 then return nil, "a spawn needs at least one creature kind" end
  elseif kind == "boss" then
    -- the cleaned action keeps the shape of the raw one (the definition is cleaned again when it is copied, exported or edited)
    local boss, why = hk_boss(raw)
    if not boss then return nil, why end
    for k, v in pairs(boss) do a[k] = v end
    a.n = 1
  elseif kind == "item" then
    local spec = type(raw.spec) == "table" and raw.spec or {}
    a.spec = {}
    if type(spec.type) == "string" and spec.type:match("%S") then a.spec.type = hk_text(spec.type, 48)
    elseif type(spec.type) == "number" then a.spec.type = hk_int(spec.type, 1, 4294967295) end
    if spec.quality ~= nil then a.spec.quality = hk_int(spec.quality, 0, 14) end
    if type(spec.category) == "string" and spec.category ~= "" then a.spec.category = hk_text(spec.category, 20):lower() end
    if spec.level ~= nil then a.spec.level = hk_int(spec.level, 1, 200) end
    if next(a.spec) == nil then return nil, "an item needs a type or a quality / category / level" end
    a.n = hk_clamp(raw.n, 1, CONFIG.max_item_n, 1)
  elseif kind == "gold" then
    a.amount = hk_clamp(raw.amount, 1, 2000000000, nil)
    if not a.amount then return nil, "gold needs an amount" end
  end
  if raw.text ~= nil and tostring(raw.text):match("%S") then a.text = hk_text(raw.text, 160) end
  if kind == "message" and not a.text then return nil, "a message needs a text" end
  return a
end

-- The hooks of an event definition (config or web), checked and cleaned; refused ones are logged. nil when there are none.
local function hooks_of(raw, event_id)
  if type(raw) ~= "table" then return nil end
  local out = {}
  for i, h in ipairs(raw) do
    if #out >= HOOK_LIMITS.hooks then server.log("event " .. tostring(event_id) .. ": more than " .. HOOK_LIMITS.hooks .. " hooks, the rest is ignored"); break end
    local hook, why = {}, nil
    if type(h) ~= "table" then why = "not an object"
    else
      if h.on == "start" or h.on == "success" or h.on == "fail" then hook.on = h.on
      elseif type(h.kills) == "table" then
        local creature = h.kills.creature
        if type(creature) == "string" and creature:match("%S") then creature = hk_text(creature, 48):match("^%s*(.-)%s*$")
        elseif type(creature) == "number" then creature = hk_int(creature, 1, 4294967295) else creature = nil end
        local count = hk_int(h.kills.count, 1, 100000)
        if creature and count then hook.kills = { creature = creature, count = count, per_player = h.kills.per_player == true, region = hk_int(h.kills.region, 1, 255) }
        else why = "kills needs a creature and a count" end
      elseif tonumber(h.at_time) then
        hook.at_time = hk_int(h.at_time, 1, 30 * 86400)
        if not hook.at_time then why = "at_time must be 1 second to 30 days" end
      elseif tonumber(h.at) then
        local at = tonumber(h.at)
        if at > 0 and at <= 100 then hook.at = at else why = "at must be a percent above 0 up to 100" end
      else why = "no trigger (at, at_time, on or kills)" end
      if not why then
        hook.actions = {}
        for j, raw_action in ipairs(type(h.actions) == "table" and h.actions or {}) do
          if #hook.actions >= HOOK_LIMITS.actions then break end
          local action, action_why = hk_action(raw_action, i, j)
          if action then hook.actions[#hook.actions + 1] = action
          else server.log("event " .. tostring(event_id) .. ": hook #" .. i .. " action #" .. j .. " refused: " .. tostring(action_why)) end
        end
        if #hook.actions == 0 then why = "no valid action" end
        if h.label ~= nil and tostring(h.label):match("%S") then hook.label = hk_text(h.label, 60) end
      end
    end
    if why then server.log("event " .. tostring(event_id) .. ": hook #" .. i .. " refused: " .. why)
    else out[#out + 1] = hook end
  end
  if #out == 0 then return nil end
  return out
end

-- A definition as it is stored with a running event (so editing this file does not change an event in progress).
local function snapshot(def)
  local reward = type(def.reward) == "table" and def.reward or {}
  return {
    id = tostring(def.id), title = tostring(def.title or def.id), goal = "kill",
    target = math.max(1, math.floor(tonumber(def.target) or 100)), duration = math.max(1, tonumber(def.duration) or 3600),
    names = copy_list(def.names, lower_of), types = copy_list(def.types, number_of), where = def.where, hooks = hooks_of(def.hooks, def.id),
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
  local wanted_regions = def.regions
  if tonumber(wanted_regions) then wanted_regions = { wanted_regions } end
  for _, region_id in ipairs(type(wanted_regions) == "table" and wanted_regions or {}) do
    local list = tonumber(region_id) and server.region_sectors and server.region_sectors(math.floor(region_id)) or {}
    if #list == 0 then server.log("region " .. tostring(region_id) .. " has no sectors (unknown region, or the server has no --game-dir)") end
    for _, sector in ipairs(list) do own[sector] = true end
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
local region_names
function region_name(id)
  if not region_names then
    region_names = {}
    for _, r in ipairs(server.regions and server.regions() or {}) do region_names[r.id] = r.name end
  end
  return region_names[id] or ("region " .. tostring(id))
end

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
  local game_regions = def.regions
  if tonumber(game_regions) then game_regions = { game_regions } end
  if type(game_regions) == "table" and #game_regions > 0 then
    add_parts({ regions = game_regions }, ids, circles)
    for _, id in ipairs(game_regions) do names[#names + 1] = clean_name(region_name(math.floor(tonumber(id) or 0))) end
  end
  if #wanted == 0 and not def.sectors and not (type(game_regions) == "table" and #game_regions > 0) then return nil end
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
  return { seq = 0, index = 0, history = {}, bosses = {}, next_start = NOW() + CONFIG.first_delay, control_rev = (function() local ok, v = pcall(server.load, "control_rev", 0); return ok and tonumber(v) or 0 end)() }
end

local function load_state()
  local ok, value = pcall(server.load, "state", nil)
  if not ok or type(value) ~= "table" then return fresh() end
  value.seq = tonumber(value.seq) or 0
  value.index = tonumber(value.index) or 0
  value.history = type(value.history) == "table" and value.history or {}
  value.bosses = type(value.bosses) == "table" and value.bosses or {}
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
    regions = words_of(raw.regions, function(v) return int_in(v, 1, 255) end, 16),
    where = (raw.where == "victim" or raw.where == "player" or raw.where == "either") and raw.where or nil,
    text = type(raw.text) == "string" and raw.text:match("%S") and text_of(raw.text, 200) or nil,
    unit = type(raw.unit) == "string" and raw.unit:match("%S") and text_of(raw.unit, 30) or nil,
    min_contribution = int_in(raw.min_contribution, 1, 1000000) or 1, hooks = hooks_of(raw.hooks, id),
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

-- The next hook that has not fired, in one short line for the client's tooltip ("At 30%: 12 Orc Warrior appear around the owner").
local next_hook_text

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
      odds = odds_now, odds_at = odds_at, odds_next = odds_next, odds_next_at = odds_next_at, hook = next_hook_text(ev),
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

-- ---- hooks: describing, targeting and running them ------------------------------------------------------------------------------

local spawn_channel = net and net.channel("worldevents:spawn", { server_only = true })
local spawn_window = {}          -- { t, n } of the creatures asked for in the last minute

local TARGET_TEXT = {
  killer = "the hero who made the kill", host = "the host", random = "a random hero in the area", owner = "the owner of the area (or the only hero there)",
  players = "every hero in the area", global = "everybody",
}

local function kind_text(kinds)
  local parts = {}
  for _, k in ipairs(kinds) do parts[#parts + 1] = tostring(k.type) .. (k.weight > 1 and (" x" .. k.weight) or "") end
  return table.concat(parts, " / ")
end

local function target_text(target, bound)
  if not bound and (target == "random" or target == "owner" or target == "players") then
    return ({ random = "a random hero online", owner = "the only hero (or the one with most kills)", players = "every hero online" })[target]
  end
  return TARGET_TEXT[target]
end

local function action_text(a, bound)
  local who = target_text(a.target, bound)
  if a.action == "spawn" then
    return string.format("%d of %s appear around %s%s", a.n, kind_text(a.kinds), who, a.per_player and " (that many for each)" or " (shared out)")
  elseif a.action == "boss" then
    return string.format("the boss %s (x%s health, x%s damage) %s for %s", a.name, tostring(a.health), tostring(a.damage),
      a.near and ("rises from the nearest " .. a.near) or "appears", who)
  elseif a.action == "item" then
    local spec = a.spec
    local what = spec.type and tostring(spec.type) or ("an item" .. (spec.quality and (" of quality " .. spec.quality) or "") .. (spec.category and (", " .. spec.category) or "")
      .. (spec.level and (", level " .. spec.level) or ""))
    return string.format("%s%s drops for %s", a.n > 1 and (a.n .. " x ") or "", what, who)
  elseif a.action == "gold" then
    return string.format("%d gold drops for %s", a.amount, who)
  end
  return string.format("%s is told to %s", who, '"' .. tostring(a.text) .. '"')
end

local function trigger_text(h)
  if h.on == "start" then return "When the event starts"
  elseif h.on == "success" then return "When the goal is reached"
  elseif h.on == "fail" then return "When the time runs out"
  elseif h.kills then
    return string.format("When %s has defeated %d x %s%s", h.kills.per_player and "a hero" or "the realm", h.kills.count, tostring(h.kills.creature),
      h.kills.region and (" in " .. region_name(h.kills.region)) or "")
  elseif h.at_time then return "After " .. duration_text(h.at_time)
  end
  return "At " .. percent_text(h.at) .. " of the goal"
end

-- The hooks of a definition as lines of text, for the page, the JSON and !event hooks.
local function hook_lines(def)
  local lines = {}
  for i, h in ipairs(def.hooks or {}) do
    local parts = {}
    for _, a in ipairs(h.actions) do parts[#parts + 1] = action_text(a, def.region ~= nil) end
    lines[i] = trigger_text(h) .. ": " .. table.concat(parts, "; ") .. "."
  end
  return lines
end

-- Heroes in the world: those in the event's area, for an event without area everybody.
local function zone_players(def)
  local out = {}
  for _, p in ipairs(server.players()) do
    if p.in_game and (not def.region or contains(def.region, p.sector, p.x, p.y)) then out[#out + 1] = p end
  end
  return out
end

local function pick_targets(ev, target, ctx)
  local def = ev.def
  if target == "killer" then
    return (ctx.killer and ctx.killer.in_game ~= false) and { ctx.killer } or {}
  elseif target == "host" then
    for _, p in ipairs(server.players()) do if p.host and p.in_game then return { p } end end
    return {}
  elseif target == "global" then
    local out = {}
    for _, p in ipairs(server.players()) do if p.in_game then out[#out + 1] = p end end
    return out
  end
  local zone = zone_players(def)
  if target == "players" then return zone end
  if #zone == 0 then return {} end
  if target == "random" then
    local pool = zone
    if not def.region then
      local took = {}
      for _, p in ipairs(zone) do if ev.players[p.uid] then took[#took + 1] = p end end
      if #took > 0 then pool = took end
    end
    return { pool[server.random(#pool)] }
  end
  -- owner: the only hero there; else the one whose game owns its sector; else the one with the most kills (lowest slot on a tie)
  if #zone == 1 then return { zone[1] } end
  for _, p in ipairs(zone) do
    if (p.sector or 0) ~= 0 and server.sector_owner and server.sector_owner(p.sector) == p.slot then return { p } end
  end
  local best
  for _, p in ipairs(zone) do
    local n = ev.players[p.uid] and ev.players[p.uid].n or 0
    local bn = best and (ev.players[best.uid] and ev.players[best.uid].n or 0) or -1
    if not best or n > bn or (n == bn and p.slot < best.slot) then best = p end
  end
  return { best }
end

-- Takes `n` creatures from the per-minute budget (CONFIG.max_spawn_per_minute): how many may be asked for now.
local function spawn_budget(n)
  local now, used, keep = NOW(), 0, {}
  for _, e in ipairs(spawn_window) do
    if now - e.t < 60 then keep[#keep + 1] = e; used = used + e.n end
  end
  spawn_window = keep
  local left = CONFIG.max_spawn_per_minute - used
  if left <= 0 then return 0 end
  n = math.min(n, left)
  keep[#keep + 1] = { t = now, n = n }
  return n
end

local function send_spawn(ev, key, index, player, action, n)
  if n <= 0 then return false end
  n = spawn_budget(n)
  if n <= 0 then server.log("hook " .. key .. ": the spawn budget of " .. CONFIG.max_spawn_per_minute .. " creatures a minute is used up, no wave for " .. player.name); return false end
  if not (spawn_channel and net.supports(player)) then
    server.log("hook " .. key .. ": " .. player.name .. "'s game has no world-events mod (or the server has no net channel), no wave")
    return false
  end
  local message = { v = 1, id = ev.n .. ":" .. key .. ":" .. index .. ":" .. tostring(player.uid):sub(1, 8), event = ev.def.title, n = n, radius = action.radius,
    kinds = action.kinds, text = action.text }
  local ok, err = pcall(spawn_channel.send, spawn_channel, player, message)
  if not ok then server.log("hook " .. key .. ": cannot send the wave: " .. tostring(err)); return false end
  return true
end

-- ---- world bosses ------------------------------------------------------------------------------------------------------------------
-- The server cannot make creatures: it announces a boss on `worldevents:boss` (name, no health numbers), asks the target hero's game to make it
-- (`worldevents:spawn` with a `boss`, only a game that can create creatures does) and calls the boss defeated when a hero kills a creature of its
-- kind. The bosses are kept in state.bosses (a restart or a reload carries on).

local boss_channel = net and net.channel("worldevents:boss", { server_only = true })

local function boss_message(b, st, by)
  return { v = 1, id = b.id, st = st, name = b.name, event = b.event, text = b.text, by = by, reward = b.reward_text }
end

local function boss_send(player, b, st, by)
  if not boss_channel or not net.supports(player) then return end
  local ok, err = pcall(boss_channel.send, boss_channel, player, boss_message(b, st, by))
  if not ok then server.log("boss " .. tostring(b.id) .. ": cannot send: " .. tostring(err)) end
end

local function boss_send_all(b, st, by)
  for _, p in ipairs(server.players()) do if p.in_game then boss_send(p, b, st, by) end end
end

local function boss_reward_text(r)
  if not r then return nil end
  local parts = {}
  if r.gold then parts[#parts + 1] = r.gold .. " gold" end
  if r.item then parts[#parts + 1] = "an item" .. (r.item.category and (" (" .. r.item.category .. ")") or "") end
  return table.concat(parts, ", ")
end

if server.events then
  server.events.declare("worldevent.boss.spawned", { doc = "A world boss was announced (a boss action of an event).", args = "id, name, event, reward?, source = 'server'" })
  server.events.declare("worldevent.boss.defeated", { doc = "A hero defeated the boss (gone = true: its time ran out).", args = "id, name, event, reward?, by?, gone?, source = 'server'" })
end

local function boss_emit(name, b, extra)
  if not server.emit then return end
  local e = { id = b.id, name = b.name, event = b.event, reward = b.reward_text, source = "server" }
  for k, v in pairs(extra or {}) do e[k] = v end
  local ok, err = pcall(server.emit, name, e)
  if not ok then server.log("hook " .. name .. ": " .. tostring(err)) end
end

local function boss_start(ev, key, index, action)
  state.bosses = state.bosses or {}
  local def = action
  local first = def.kinds[1] and def.kinds[1].type or def.near
  local b = { id = ev.n .. ":" .. key .. ":" .. index, name = def.name, event = ev.def.title, text = action.text, match = first, started = NOW(),
    expires = NOW() + def.window, reward = def.reward, reward_text = boss_reward_text(def.reward) }
  for i = #state.bosses, 1, -1 do if state.bosses[i].id == b.id then table.remove(state.bosses, i) end end
  if #state.bosses >= 8 then table.remove(state.bosses, 1) end
  state.bosses[#state.bosses + 1] = b
  dirty = true
  boss_send_all(b, "active")
  boss_emit("worldevent.boss.spawned", b)
  say(string.format("%s: the boss %s has appeared!%s", ev.def.title, b.name, b.reward_text and (" Reward: " .. b.reward_text .. ".") or ""))
  return b
end

local function boss_end(i, st, player)
  local b = table.remove(state.bosses, i)
  dirty = true
  boss_send_all(b, st, player and player.character or nil)
  boss_emit("worldevent.boss.defeated", b, { by = player and player.character or nil, gone = st == "gone" or nil })
  if st == "defeated" and player then
    say(string.format("%s defeated the boss %s!", player.character or player.name or "A hero", b.name))
    local r = b.reward
    if r then
      if r.gold then
        local ok, pile, err = pcall(server.drop_gold, player, r.gold)
        if not (ok and pile) then server.log("boss " .. b.id .. ": no gold for " .. tostring(player.name) .. ": " .. tostring(ok and err or pile)) end
      end
      if r.item then
        local ok, item, err = pcall(server.drop_item, player, r.item)
        if not (ok and item) then server.log("boss " .. b.id .. ": no item for " .. tostring(player.name) .. ": " .. tostring(ok and err or item)) end
      end
      if r.text then server.message(player, PREFIX .. r.text) end
    end
  elseif st == "gone" then
    say(string.format("The boss %s is gone.", b.name))
  end
end

-- A hero killed `monster`: the first boss of that kind is defeated.
local function boss_kill(player, monster)
  for i, b in ipairs(state.bosses or {}) do
    local match = false
    if type(b.match) == "number" then match = monster.type == b.match
    elseif monster.name and type(b.match) == "string" then
      local function norm(text) return (tostring(text):lower():gsub("[_%s]+", " ")) end
      match = norm(monster.name):find(norm(b.match):gsub("%.%a+$", ""), 1, true) ~= nil
    end
    if match then boss_end(i, "defeated", player); return end
  end
end

local function boss_tick()
  local t = NOW()
  for i = #(state.bosses or {}), 1, -1 do
    if t >= state.bosses[i].expires then boss_end(i, "gone", nil) end
  end
end

-- A hero who joins sees the bosses that are on.
local function boss_greet(player)
  for _, b in ipairs(state.bosses or {}) do boss_send(player, b, "active") end
end

local function run_action(ev, key, index, action, ctx)
  local targets = pick_targets(ev, action.target, ctx)
  if action.action == "message" then
    if action.target == "global" then say(action.text)
    else for _, p in ipairs(targets) do server.message(p, PREFIX .. action.text) end end
    return
  end
  if #targets == 0 and action.action ~= "boss" then server.log("hook " .. key .. ": no " .. action.target .. " target for " .. action.action); return end
  if action.action == "boss" then
    local b = boss_start(ev, key, index, action)
    -- the target's game is asked to make the creature (only a game that can create creatures does; see above)
    for _, p in ipairs(targets) do
      if spawn_channel and net.supports(p) and spawn_budget(1) > 0 then
        local message = { v = 1, id = b.id .. ":" .. tostring(p.uid):sub(1, 8), event = ev.def.title, n = 1, radius = action.radius, text = action.text,
          boss = { name = action.name, kinds = action.kinds, near = action.near, radius = action.radius, health = action.health, damage = action.damage,
            taken = action.taken, deflect = action.deflect, reflect = action.reflect, regen = action.regen, reward = action.reward } }
        local ok, err = pcall(spawn_channel.send, spawn_channel, p, message)
        if not ok then server.log("hook " .. key .. ": cannot send the boss: " .. tostring(err)) end
      end
    end
    return
  end
  if action.action == "spawn" then
    local base, extra = action.n, 0
    if not action.per_player then base, extra = math.floor(action.n / #targets), action.n % #targets end
    for i, p in ipairs(targets) do
      local n = action.per_player and action.n or (base + (i <= extra and 1 or 0))
      send_spawn(ev, key, index, p, action, n)
    end
    if action.text then
      for _, p in ipairs(targets) do server.message(p, PREFIX .. action.text) end
    end
  elseif action.action == "item" then
    if action.spec.type then
      server.log("hook " .. key .. ": an item by type name is dropped by the Sacred Gear mod in a game of its own (single player); the server drops rarity / category specs")
      return
    end
    for _, p in ipairs(targets) do
      for _ = 1, action.n do
        local ok, item, err = pcall(server.drop_item, p, action.spec)
        if not (ok and item) then server.log("hook " .. key .. ": no item for " .. p.name .. ": " .. tostring(ok and err or item)); break end
      end
      if action.text then server.message(p, PREFIX .. action.text) end
    end
  elseif action.action == "gold" then
    for _, p in ipairs(targets) do
      local ok, pile, err = pcall(server.drop_gold, p, action.amount)
      if not (ok and pile) then server.log("hook " .. key .. ": no gold for " .. p.name .. ": " .. tostring(ok and err or pile)) end
      if action.text then server.message(p, PREFIX .. action.text) end
    end
  end
end

-- Fires one hook once. Never throws.
-- Events for other mods (server.emit; the same names and payloads as the Sacred Gear world events mod publishes in the game):
-- worldevent.started / progress / completed / failed / hook.
if server.events then
  for _, e in ipairs({
    { "worldevent.started", "A world event began." },
    { "worldevent.progress", "The realm's count went up (previous)." },
    { "worldevent.completed", "The goal was reached." },
    { "worldevent.failed", "The time ran out (cancelled = true: an admin ended it)." },
    { "worldevent.hook", "A trigger of the event fired an action (action, text)." },
  }) do
    server.events.declare(e[1], { doc = e[2], args = "id, title, text, unit, area?, count, target, percent, here, source = 'server', previous?, cancelled?, action?" })
  end
end

local function we_hook(name, ev, extra)
  if not server.emit then return end
  local def = ev.def
  local e = { id = ev.n, title = def.title, text = goal_text(def), unit = unit_of(def), area = def.region and def.region.name or nil,
    count = ev.count, target = def.target, percent = math.min(100, math.floor(ev.count * 100 / def.target)), here = false, source = "server" }
  for k, v in pairs(extra or {}) do e[k] = v end
  local ok, err = pcall(server.emit, name, e)
  if not ok then server.log("hook " .. name .. ": " .. tostring(err)) end
end

local function fire_hook(ev, number, hook, ctx)
  local key = "h" .. number
  ev.fired = ev.fired or {}
  if ev.fired[key] then return end
  ev.fired[key] = true
  dirty = true
  local parts = {}
  for i, action in ipairs(hook.actions) do
    local ok, err = pcall(run_action, ev, key, i, action, ctx)
    if not ok then server.log("hook " .. key .. " action " .. i .. " failed: " .. tostring(err)) end
    we_hook("worldevent.hook", ev, { action = action.action, text = action.text })
    parts[#parts + 1] = action_text(action, ev.def.region ~= nil)
  end
  if CONFIG.announce_hooks then say(ev.def.title .. ": " .. trigger_text(hook):lower() .. " -> " .. table.concat(parts, "; ") .. ".") end
end

local function run_hooks(ev, test, ctx)
  for number, hook in ipairs(ev.def.hooks or {}) do
    if not (ev.fired and ev.fired["h" .. number]) and test(hook) then fire_hook(ev, number, hook, ctx or {}) end
  end
end

local function hooks_progress(ev, killer)
  if not ev.def.hooks then return end
  local percent = ev.count * 100 / ev.def.target
  local elapsed = NOW() - ev.started
  run_hooks(ev, function(h) return (h.at and percent >= h.at) or (h.at_time and elapsed >= h.at_time) end, { killer = killer })
end

local function hooks_on(ev, which)
  if not ev.def.hooks then return end
  run_hooks(ev, function(h) return h.on == which end, {})
end

-- A kill that counts for the event: the "kills" triggers count creatures of their kind.
local function hooks_kill(ev, player, monster)
  if not ev.def.hooks then return end
  ev.hk = ev.hk or {}
  for number, hook in ipairs(ev.def.hooks) do
    local k = hook.kills
    local key = "h" .. number
    if k and not (ev.fired and ev.fired[key]) then
      local match = false
      if k.region then
        local at = monster.sector or player.sector
        if not (at and server.sector_region and server.sector_region(at) == k.region) then goto continue end
      end
      if type(k.creature) == "number" then match = monster.type == k.creature
      elseif monster.name then match = monster.name:lower():find(tostring(k.creature):lower(), 1, true) ~= nil end
      if match then
        local counters = ev.hk[key] or {}
        ev.hk[key] = counters
        local who = k.per_player and tostring(player.uid) or "all"
        counters[who] = (counters[who] or 0) + 1
        dirty = true
        if counters[who] >= k.count then fire_hook(ev, number, hook, { killer = player }) end
      end
    end
    ::continue::
  end
end

next_hook_text = function(ev)
  local lines = hook_lines(ev.def)
  for i, h in ipairs(ev.def.hooks or {}) do
    if not (ev.fired and ev.fired["h" .. i]) and (h.at or h.at_time) then
      local text = trigger_text(h) .. ": " .. action_text(h.actions[1], ev.def.region ~= nil)
      return (#text > 150) and (text:sub(1, 147) .. "...") or text
    end
  end
  return nil
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
  pcall(hooks_on, ev, success and "success" or "fail")
  we_hook(success and "worldevent.completed" or "worldevent.failed", ev)
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
  state.current = { n = state.seq, def = snap, started = t, ends = t + snap.duration, count = 0, players = {}, marks = {}, fired = {}, hk = {} }
  say(snap.title .. " begins: " .. goal_text(snap) .. " within " .. duration_text(snap.duration) .. "! Reward: "
    .. (reward_text(snap) ~= "" and reward_text(snap) or "glory") .. ". Type !event for the progress.")
  we_hook("worldevent.started", state.current)
  pcall(hooks_on, state.current, "start")
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
  we_hook("worldevent.failed", ev, { cancelled = true })
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
  if state.bosses and #state.bosses > 0 then pcall(boss_kill, player, monster) end
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
  we_hook("worldevent.progress", ev, { previous = ev.count - 1 })
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
  local hooked, hook_err = pcall(function() hooks_kill(ev, player, monster); hooks_progress(ev, player) end)
  if not hooked then server.log("hooks failed: " .. tostring(hook_err)) end
  if ev.count >= ev.def.target then finish(true) end
end)

server.on("enter", function(player)
  pcall(boss_greet, player)
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
  elseif text == "!event hooks" then
    local ev = state.current
    if not ev then server.message(player, PREFIX .. "No world event is running."); return true end
    local lines = hook_lines(ev.def)
    if #lines == 0 then server.message(player, PREFIX .. "This event has no hooks.") end
    for i, line in ipairs(lines) do server.message(player, PREFIX .. (ev.fired and ev.fired["h" .. i] and "[done] " or "") .. line) end
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
  pcall(boss_tick)
  local ev = state.current
  if ev and t >= ev.ends then finish(false) end
  if state.current then pcall(hooks_progress, state.current, nil) end
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
      hooks = (function()
        local out, lines = {}, hook_lines(ev.def)
        for i, text in ipairs(lines) do out[i] = { text = text, fired = ev.fired and ev.fired["h" .. i] == true } end
        return out
      end)(),
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
    out.events[i] = { id = def.id, title = def.title, text = goal_text(snap), duration = def.duration, reward_odds = odds_json(snap), hooks = hook_lines(snap) }
  end
  return out
end

server.endpoint("state", function() return public_state() end,
  { public = true, description = "the running event, the last result and the history as JSON (character names only)" })

-- For the area editor page of the web UI (admin only, not public): the areas as defined (the script file's and the saved ones, which win)
-- and where the heroes are. The page saves areas into this mod's storage through POST /api/mod-store.
local function area_json(a, source)
  return { name = a.name, sectors = a.sectors or {}, ranges = a.ranges, rect = a.rect, near = a.near, circle = a.circle, regions = a.regions, source = source }
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
    area = def.area, areas = def.areas, sectors = def.sectors, regions = def.regions, where = def.where, text = def.text, unit = def.unit, min_contribution = def.min_contribution,
    created = def.created, hooks = def.hooks,
    reward = { gold = reward.gold, gold_each = reward.gold_each, item = reward.item, top_item = reward.top_item, milestones = milestones, on_fail = reward.on_fail },
  }
end

-- The events in the format of the Sacred Gear mod `world-events` for a single player / LAN game without a server (its events.json): save
-- this answer as `events.json` in the mod's folder. Rules and hooks are public anyway (dashboard), so this endpoint is public too.
server.endpoint("export", function()
  local events = {}
  for _, id in ipairs(all_order) do
    local def = all_defs[id]
    if def and not events_off[id] then
      events[#events + 1] = {
        id = tostring(def.id), title = def.title, goal = "kill", target = def.target, duration = def.duration, names = def.names, types = def.types,
        sectors = def.sectors, regions = def.regions, unit = def.unit, text = def.text, area = def.region and def.region.name or nil, hooks = def.hooks,
      }
    end
  end
  return { v = 1, config = { first_delay = CONFIG.first_delay, cooldown = CONFIG.cooldown, result_hold = CONFIG.result_hold, order = CONFIG.order == "random" and "random" or "sequence" },
    events = events }
end, { public = true, description = "the events in the format of the world-events client mod (events.json for games without a server)" })

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
  local game_regions = {}
  for i, r in ipairs(server.regions and server.regions() or {}) do game_regions[i] = { id = r.id, name = clean_name(r.name), sectors = r.sectors } end
  local ev = state.current
  local running
  if ev then running = { id = ev.def.id, title = ev.def.title, count = ev.count, target = ev.def.target, left = math.floor(math.max(0, ev.ends - NOW())) } end
  return {
    events = list, areas = areas, regions = game_regions, tiers = tiers, running = running, next_in = not ev and math.floor(math.max(0, (state.next_start or NOW()) - NOW())) or nil,
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

local style = [[
body{margin:0;font:15px/1.4 system-ui,sans-serif;background:#15110d;color:#efe6d6}h1{margin:0;padding:1rem 1.2rem;font-size:1.3rem;color:#e0a944}
main{display:grid;grid-template-columns:repeat(auto-fill,minmax(19rem,1fr));gap:1rem;padding:0 1.2rem 1.2rem}
section{background:#241d16;border:1px solid #3b3025;border-radius:10px;padding:.8rem 1rem}h2{margin:0 0 .5rem;font-size:1rem;color:#f2c36b}
ol,ul{margin:0;padding-left:1.3rem}li{margin:.15rem 0}b{color:#fff}small{color:#a39683}
.bar{height:1.2rem;background:#0e0b08;border:1px solid #5a4a36;border-radius:6px;overflow:hidden;margin:.4rem 0}
.bar i{display:block;height:100%;background:linear-gradient(#e8b84a,#b07a1e)}.ok{color:#8fd08f}.bad{color:#e08f8f}
table{border-collapse:collapse;width:100%}th,td{text-align:right;padding:.15rem .4rem;border-bottom:1px solid #3b3025}th:first-child,td:first-child{text-align:left}
th{color:#a39683;font-weight:normal}tr.now td{background:#3b2f1a;color:#fff;font-weight:bold}
ul.hooks li.done{opacity:.55}]]

local function section(title, body)
  return "<section><h2>" .. server.html(title) .. "</h2>" .. body .. "</section>"
end

server.page("", function()
  local h, s = server.html, public_state()
  local parts = {
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">',
    '<meta http-equiv="refresh" content="10"><title>World events</title><style>', style, '</style></head><body><h1>World events</h1><main>',
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
  if a and a.hooks and #a.hooks > 0 then
    local rows_h = {}
    for _, hook in ipairs(a.hooks) do
      rows_h[#rows_h + 1] = "<li" .. (hook.fired and ' class="done"' or "") .. ">" .. h(hook.text) .. (hook.fired and " <small>(done)</small>" or "") .. "</li>"
    end
    parts[#parts + 1] = section("Hooks: what happens on the way", "<ul class=\"hooks\">" .. table.concat(rows_h) .. "</ul>")
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
