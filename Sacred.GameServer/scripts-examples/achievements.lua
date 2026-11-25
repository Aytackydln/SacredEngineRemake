-- Achievements and a daily login streak, kept on the server (script-data/achievements.json).
-- Copy into the server's scripts folder to use it.
--
--   * Achievements unlock from what the server really sees: creatures defeated, distinct creature types defeated, "giants"
--     (a creature 10 or more levels above the hero), hero deaths, players defeated, hero level, time online, days visited
--     and the login streak. Each can pay a reward (gold pile, or an item and a gold pile when the server has --game-dir).
--   * Login streak: the first time a hero is in the world on a new UTC day it gets a reward that grows with the streak
--     (100 gold per day up to 1000, an item every 7th day). A missed day starts the streak over at 1.
--   * "!ach" / "!streak" in chat; a web page listing every hero's achievements:
--       /mods/achievements/              the page (with WebUi:PublicApi=true also /public/mods/achievements/)
--       /mods/achievements/players       JSON: character, level, streak, unlocked ids
--       /mods/achievements/achievements  JSON: the list of achievements
--   * Sacred Gear clients (gear/examples/achievements) get a toast for every unlock and for the streak, and a panel, over the
--     custom net channel "achievements:sync". Without that the mod works through chat messages alone.
--
-- Configuration (the list of achievements, the streak reward, the roster size, the UTC offset ...) is data in this mod's storage (key
-- "config"), seeded from the DEFAULTS below on the first run and editable live: see "configuration as data" and /mods/achievements/config.
-- Not covered because the script API has no event or data for it: gold earned or held, exploration (the explored map is
-- client-only), quests, items found. Level has no event either: it is read from the player when kills, joins and a 20 s timer
-- come by. Rewards are dropped next to the hero, so they are held until the hero is in the world. Unverified with a real
-- client: that the owner can pick up what drop_gold / drop_item drops (see the server README).
server.describe("Achievements (kills, creature types, levels, online time, deaths) and a daily login streak with gold/item rewards, a web page per hero and a Sacred Gear toast/panel.")

-- ---- configuration as data: storage key "config", live edits, overview -----------------------------------------------------------
-- The tunables below are DEFAULTS only: the first run copies them into this mod's storage (key "config", script-data/achievements.json), and from then on
-- the storage is the truth. Edit it while the server runs (admin): POST /api/mod-store {"mod":"achievements","set":{"config":{...}}} (the whole
-- table; a field you leave out falls back to its default). The write bumps config_rev, the mod notices within 2 s and applies it, no
-- reload. A field that does not validate is ignored with a log line and its default stays. A reload (file watcher, Mods tab,
-- POST /api/reload-mods) keeps everything: the unload handler saves the state. The admin page /mods/achievements/config shows the active
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
    server.render(server.file("mod-config.html"), { mod = "achievements" }),
    "<p>Source: <b>", cfg_from_storage() > 0 and "storage" or "default", "</b> (storage key <code>config</code>, revision ", tostring(CFG.rev), "). Loaded ", h(cfg_stamp(CFG.loaded)),
    ", last reload reason: <b>", h(CFG.reason), "</b>", CFG.applied and (", last live edit applied " .. h(cfg_stamp(CFG.applied))) or "", ".</p>",
    #errors > 0 and ("<p class='err'>Config errors (those fields use their defaults):</p><ul>" .. table.concat(errors) .. "</ul>") or "<p><small>No config errors.</small></p>",
    "<table><tr><th>field</th><th>from</th><th>active value</th></tr>", table.concat(rows), "</table>",
    "<p><small>Edit: POST /api/mod-store {\"mod\":\"achievements\",\"set\":{\"config\":{...}}} (admin); applied within 2 seconds, no reload needed. ",
    "Delete a field inside config to get its default back.</small></p></body></html>",
  })
end, { title = "achievements configuration" })

-- Defaults of the configuration (see "configuration as data" above: they seed the storage key "config" on the first run).
--   utc_offset_hours   shifts the day change (UTC by default)
--   roster_limit       heroes shown on the page and kept in the page's roster
--   streak_*           the daily login reward: gold per streak day up to streak_gold_max_days; every streak_item_every-th day also an
--                      item, quality streak_item_quality + streak / streak_item_every, at most streak_item_max_quality (0 = no item)
--   reward_tries       a reward that cannot be dropped is retried this often (every 20 s, while the hero is in the world), then dropped
--   item_fallback_gold gold given instead of an item when the server cannot drop items (no --game-dir)
--   achievements       the list: stat = which counter of the hero decides ("ach.<stat>": kills level species giants deaths pvp online
--                      days best), target, reward = gold and/or item (a generation quality 0..14), shout = tell everybody in chat.
--                      Ids are the keys of what heroes unlocked: renaming one makes it unlockable again, removing one hides it.
local DEFAULTS = {
  utc_offset_hours = 0,
  roster_limit = 500,
  streak_gold_per_day = 100,
  streak_gold_max_days = 10,
  streak_item_every = 7,
  streak_item_quality = 4,
  streak_item_max_quality = 14,
  reward_tries = 8,
  item_fallback_gold = 1000,
  achievements = {
    { id = "kills_1",     name = "First Blood",         desc = "Defeat a creature.",                    stat = "kills",   target = 1,     reward = { gold = 50 } },
    { id = "kills_100",   name = "Monster Hunter",      desc = "Defeat 100 creatures.",                 stat = "kills",   target = 100,   reward = { gold = 250 } },
    { id = "kills_1000",  name = "Slayer",              desc = "Defeat 1,000 creatures.",               stat = "kills",   target = 1000,  reward = { gold = 1000, item = 3 }, shout = true },
    { id = "kills_5000",  name = "Exterminator",        desc = "Defeat 5,000 creatures.",               stat = "kills",   target = 5000,  reward = { gold = 3000, item = 7 }, shout = true },
    { id = "kills_20000", name = "Legend of the Wilds", desc = "Defeat 20,000 creatures.",              stat = "kills",   target = 20000, reward = { gold = 10000, item = 10 }, shout = true },
    { id = "level_10",    name = "Apprentice",          desc = "Reach hero level 10.",                  stat = "level",   target = 10,    reward = { gold = 100 } },
    { id = "level_25",    name = "Veteran",             desc = "Reach hero level 25.",                  stat = "level",   target = 25,    reward = { gold = 400 } },
    { id = "level_50",    name = "Champion",            desc = "Reach hero level 50.",                  stat = "level",   target = 50,    reward = { gold = 1500, item = 4 }, shout = true },
    { id = "level_100",   name = "Hero of Ancaria",     desc = "Reach hero level 100.",                 stat = "level",   target = 100,   reward = { gold = 5000, item = 7 }, shout = true },
    { id = "level_150",   name = "Demigod",             desc = "Reach hero level 150.",                 stat = "level",   target = 150,   reward = { gold = 10000, item = 10 }, shout = true },
    { id = "species_10",  name = "Curious",             desc = "Defeat 10 different kinds of creature.", stat = "species", target = 10,   reward = { gold = 150 } },
    { id = "species_50",  name = "Bestiary Scholar",    desc = "Defeat 50 different kinds of creature.", stat = "species", target = 50,   reward = { gold = 800, item = 3 } },
    { id = "species_150", name = "Master of Beasts",    desc = "Defeat 150 different kinds of creature.", stat = "species", target = 150, reward = { gold = 3000, item = 8 }, shout = true },
    { id = "giants_1",    name = "Giant Slayer",        desc = "Defeat a creature 10 or more levels above your hero.", stat = "giants", target = 1,  reward = { gold = 300 } },
    { id = "giants_25",   name = "Bane of Titans",      desc = "Defeat 25 such creatures.",             stat = "giants",  target = 25,    reward = { gold = 2500, item = 6 }, shout = true },
    { id = "deaths_1",    name = "A Taste of Death",    desc = "Die for the first time.",               stat = "deaths",  target = 1,     reward = { gold = 25 } },
    { id = "deaths_10",   name = "Persistent",          desc = "Die 10 times and get up again.",        stat = "deaths",  target = 10,    reward = { gold = 200 } },
    { id = "deaths_50",   name = "Phoenix",             desc = "Die 50 times and get up again.",        stat = "deaths",  target = 50,    reward = { gold = 1000 } },
    { id = "pvp_1",       name = "Duelist",             desc = "Defeat another player's hero.",         stat = "pvp",     target = 1,     reward = { gold = 200 } },
    { id = "pvp_10",      name = "Gladiator",           desc = "Defeat 10 heroes of other players.",    stat = "pvp",     target = 10,    reward = { gold = 2000, item = 5 }, shout = true },
    { id = "online_1h",   name = "Settled In",          desc = "Spend 1 hour on this server.",          stat = "online",  target = 3600,   reward = { gold = 100 } },
    { id = "online_10h",  name = "Regular",             desc = "Spend 10 hours on this server.",        stat = "online",  target = 36000,  reward = { gold = 800 } },
    { id = "online_100h", name = "Part of the Furniture", desc = "Spend 100 hours on this server.",     stat = "online",  target = 360000, reward = { gold = 6000, item = 8 }, shout = true },
    { id = "days_3",      name = "Returning Visitor",   desc = "Visit on 3 different days.",            stat = "days",    target = 3,     reward = { gold = 150 } },
    { id = "days_14",     name = "Regular Guest",       desc = "Visit on 14 different days.",           stat = "days",    target = 14,    reward = { gold = 700 } },
    { id = "days_60",     name = "Fixture",             desc = "Visit on 60 different days.",           stat = "days",    target = 60,    reward = { gold = 3000, item = 6 }, shout = true },
    { id = "streak_3",    name = "On a Roll",           desc = "Log in 3 days in a row.",               stat = "best",    target = 3,     reward = { gold = 200 } },
    { id = "streak_7",    name = "Week Warrior",        desc = "Log in 7 days in a row.",               stat = "best",    target = 7,     reward = { gold = 700, item = 4 } },
    { id = "streak_14",   name = "Fortnight",           desc = "Log in 14 days in a row.",              stat = "best",    target = 14,    reward = { gold = 2000, item = 6 }, shout = true },
    { id = "streak_30",   name = "Devoted",             desc = "Log in 30 days in a row.",              stat = "best",    target = 30,    reward = { gold = 5000, item = 9 }, shout = true },
    { id = "streak_100",  name = "Unbreakable",         desc = "Log in 100 days in a row.",             stat = "best",    target = 100,   reward = { gold = 20000, item = 12 }, shout = true },

  },
}

local STATS = { kills = true, level = true, species = true, giants = true, deaths = true, pvp = true, online = true, days = true, best = true }

local function achievement(d)
  if type(d) ~= "table" or type(d.id) ~= "string" or d.id == "" then error("needs an id", 0) end
  if type(d.name) ~= "string" or type(d.desc) ~= "string" then error(d.id .. " needs a name and a desc", 0) end
  if not STATS[d.stat] then error(d.id .. ": stat must be one of kills level species giants deaths pvp online days best", 0) end
  if type(d.target) ~= "number" or d.target < 1 then error(d.id .. ": target must be a number of at least 1", 0) end
  local reward
  if d.reward ~= nil then
    if type(d.reward) ~= "table" then error(d.id .. ": reward must be a table", 0) end
    reward = {}
    if d.reward.gold ~= nil then reward.gold = r_num(0, 100000000, true)(d.reward.gold) end
    if d.reward.item ~= nil then reward.item = r_num(0, 14, true)(d.reward.item) end
  end
  return { id = d.id, name = d.name, desc = d.desc, stat = d.stat, target = d.target, reward = reward, shout = d.shout == true }
end

local RULES = {
  utc_offset_hours = r_num(-14, 14), roster_limit = r_num(10, 5000, true), streak_gold_per_day = r_num(0, 1000000, true),
  streak_gold_max_days = r_num(1, 1000, true), streak_item_every = r_num(0, 365, true), streak_item_quality = r_num(0, 14, true),
  streak_item_max_quality = r_num(0, 14, true), reward_tries = r_num(1, 100, true), item_fallback_gold = r_num(1, 100000000, true),
  achievements = r_list(achievement, 1),
}

local defs, by_stat = {}, {}
config_start(DEFAULTS, RULES, function()
  defs, by_stat = {}, {}
  local seen = {}
  for _, d in ipairs(CONFIG.achievements) do
    if seen[d.id] then cfg_log("achievement id " .. d.id .. " is listed twice: the second one is ignored")
    else
      seen[d.id] = true
      defs[#defs + 1] = d
      by_stat[d.stat] = by_stat[d.stat] or {}
      table.insert(by_stat[d.stat], d)
    end
  end
end)

-- ---- helpers ----

local function stat(player, name) return tonumber(server.get(player, "ach." .. name, 0)) or 0 end

local function unlocked(player)
  local u = server.get(player, "ach.u", nil)
  if type(u) ~= "table" then return {} end
  return u
end

local function count(t) local n = 0; for _ in pairs(t) do n = n + 1 end; return n end

local function today() return math.floor((server.time() + CONFIG.utc_offset_hours * 3600) / 86400) end

local function reward_text(r)
  if not r then return nil end
  local parts = {}
  if r.gold then parts[#parts + 1] = r.gold .. " gold" end
  if r.item then parts[#parts + 1] = "an item" end
  return table.concat(parts, " and ")
end

-- The streak reward: gold per day up to a cap; every streak_item_every-th day also an item (quality grows with the streak).
local function streak_reward(streak)
  local r = { gold = CONFIG.streak_gold_per_day * math.min(streak, CONFIG.streak_gold_max_days) }
  local every = CONFIG.streak_item_every
  if every > 0 and streak % every == 0 then
    r.item = math.min(CONFIG.streak_item_max_quality, CONFIG.streak_item_quality + math.floor(streak / every))
  end
  return r
end

-- The channel to Sacred Gear clients: nil when the server has no custom net messages (never an error).
local channel
do
  local ok, ch = pcall(net.channel, "achievements:sync", { server_only = true })
  if ok then channel = ch end
end

local function push(player, payload)
  if not channel then return end
  local ok, supported = pcall(net.supports, player)
  if ok and supported then pcall(channel.send, channel, player, payload) end
end

-- ---- the roster for the page: character names only, kept for heroes that are offline too ----

local roster = server.load("roster", {})

local function sync_roster(player)
  local entry = {
    name = player.character, level = math.max(player.level or 0, stat(player, "level")), kills = stat(player, "kills"),
    streak = stat(player, "streak"), best = stat(player, "best"), days = stat(player, "days"),
    u = unlocked(player), seen = math.floor(server.time()),
  }
  entry.count = count(entry.u)
  roster[player.uid] = entry
end

local function save_roster()
  if count(roster) > CONFIG.roster_limit then   -- drop the heroes seen longest ago
    local ids = {}
    for id in pairs(roster) do ids[#ids + 1] = id end
    table.sort(ids, function(a, b) return (roster[a].seen or 0) > (roster[b].seen or 0) end)
    for i = CONFIG.roster_limit + 1, #ids do roster[ids[i]] = nil end
  end
  server.save("roster", roster)
end

-- ---- rewards: dropped next to the hero, so held until the hero is in the world ----

local function queue_reward(player, reward, why)
  if not reward then return end
  local pending = server.get(player, "ach.pending", nil)
  if type(pending) ~= "table" then pending = {} end
  if reward.gold and reward.gold > 0 then pending[#pending + 1] = { gold = reward.gold, why = why } end
  if reward.item then pending[#pending + 1] = { item = reward.item, fallback = CONFIG.item_fallback_gold, why = why } end
  server.set(player, "ach.pending", pending)
end

local function flush(player)
  if not player.in_game then return end
  local pending = server.get(player, "ach.pending", nil)
  if type(pending) ~= "table" or #pending == 0 then return end
  local rest = {}
  for _, r in ipairs(pending) do
    local done = false
    if r.gold then
      local ok, pile = pcall(server.drop_gold, player, r.gold)
      done = ok and pile ~= nil
    elseif r.item then
      local ok, item = pcall(server.drop_item, player, { quality = r.item })
      if ok and item then done = true
      else   -- no item possible (no --game-dir, ...): the gold instead
        local ok2, pile = pcall(server.drop_gold, player, r.fallback or CONFIG.item_fallback_gold)
        done = ok2 and pile ~= nil
      end
    else done = true end
    if not done then
      r.tries = (r.tries or 0) + 1
      if r.tries < CONFIG.reward_tries then rest[#rest + 1] = r else server.log("achievements: gave up on a reward for " .. player.character .. " (" .. tostring(r.why) .. ")") end
    end
  end
  server.set(player, "ach.pending", #rest > 0 and rest or nil)
end

-- ---- unlocking ----

local function state(player)
  local u = unlocked(player)
  local list = {}
  for i, d in ipairs(defs) do
    list[i] = { id = d.id, name = d.name, desc = d.desc, have = math.min(stat(player, d.stat), d.target), need = d.target,
      done = u[d.id], reward = reward_text(d.reward) }
  end
  local streak = stat(player, "streak")
  local last, day = stat(player, "last_day"), today()
  local claimed = last == day
  -- the reward of the next login: tomorrow's when today's is claimed, else today's (the streak goes on only after yesterday)
  local upcoming = (claimed or last == day - 1) and streak + 1 or 1
  return { op = "state", ach = list, streak = streak, best = stat(player, "best"), days = stat(player, "days"), claimed = claimed,
    next = reward_text(streak_reward(upcoming)) }
end

local function unlock(player, d)
  local u = unlocked(player)
  if u[d.id] then return end
  u[d.id] = math.floor(server.time())
  server.set(player, "ach.u", u)
  local text = "Achievement unlocked: " .. d.name .. " - " .. d.desc
  local reward = reward_text(d.reward)
  server.message(player, reward and (text .. " Reward: " .. reward .. ".") or text)
  if d.shout then server.broadcast(player.character .. " earned the achievement \"" .. d.name .. "\"!") end
  queue_reward(player, d.reward, d.name)
  sync_roster(player)
  save_roster()
  push(player, { op = "unlock", id = d.id, name = d.name, desc = d.desc, reward = reward })
  push(player, state(player))
end

local function check(player, which)
  local value = stat(player, which)
  for _, d in ipairs(by_stat[which] or {}) do
    if value >= d.target then unlock(player, d) end
  end
end

local function note_level(player)
  local level = player.level or 0
  if level > stat(player, "level") then
    server.set(player, "ach.level", level)
    check(player, "level")
  end
end

-- ---- the daily streak ----

local function check_login(player)
  if not player.in_game then return end
  local day = today()
  local last = stat(player, "last_day")
  if last >= day then return end
  local streak = (last == day - 1) and (stat(player, "streak") + 1) or 1
  server.set(player, "ach.last_day", day)
  server.set(player, "ach.streak", streak)
  if streak > stat(player, "best") then server.set(player, "ach.best", streak) end
  server.add(player, "ach.days")
  local reward = streak_reward(streak)
  server.message(player, "Daily login: day " .. streak .. " of your streak. Reward: " .. reward_text(reward) .. ".")
  queue_reward(player, reward, "login streak day " .. streak)
  check(player, "days")
  check(player, "best")
  sync_roster(player)
  save_roster()
  push(player, { op = "streak", streak = streak, best = stat(player, "best"), reward = reward_text(reward) })
  push(player, state(player))
end

local function tend(player)
  note_level(player)
  check_login(player)
  flush(player)
end

-- ---- events ----

server.on("enter", tend)
server.on("join", function(player) note_level(player) end)
server.on("leave", function(player)
  note_level(player)
  sync_roster(player)
  save_roster()
end)

server.on("kill", function(player, monster)
  server.add(player, "ach.kills")
  check(player, "kills")
  if monster.type and monster.type > 0 then
    local seen = server.get(player, "ach.seen", nil)
    if type(seen) ~= "table" then seen = {} end
    local key = tostring(monster.type)
    if not seen[key] then
      seen[key] = true
      server.set(player, "ach.seen", seen)
      server.set(player, "ach.species", count(seen))
      check(player, "species")
    end
  end
  if monster.level and monster.level > 0 and (player.level or 0) > 0 and monster.level >= player.level + 10 then
    server.add(player, "ach.giants")
    check(player, "giants")
  end
  note_level(player)
  flush(player)
end)

server.on("death", function(player, by)
  server.add(player, "ach.deaths")
  check(player, "deaths")
  if by and by.player then
    server.add(by.player, "ach.pvp")
    check(by.player, "pvp")
  end
end)

-- Online time (a crash loses at most a minute), level changes, a new day for heroes who stay past midnight, held rewards.
server.every(60, function()
  for _, player in ipairs(server.players()) do
    if player.in_game then
      server.add(player, "ach.online", 60)
      check(player, "online")
    end
  end
end)
server.every(20, function()
  for _, player in ipairs(server.players()) do tend(player) end
end)

-- ---- chat ----

server.on("chat", function(player, text)
  if text == "!ach" or text == "!achievements" then
    local u = unlocked(player)
    local mine = {}
    for _, d in ipairs(defs) do if u[d.id] then mine[#mine + 1] = d end end
    server.message(player, "Achievements: " .. #mine .. " of " .. #defs .. " unlocked.")
    table.sort(mine, function(a, b) return u[a.id] > u[b.id] end)
    for i = 1, math.min(3, #mine) do server.message(player, "  " .. mine[i].name .. " - " .. mine[i].desc) end
    for _, d in ipairs(defs) do
      if not u[d.id] then
        server.message(player, "Next: " .. d.name .. " - " .. d.desc .. " (" .. math.floor(stat(player, d.stat)) .. "/" .. d.target .. ")")
        break
      end
    end
    return true
  elseif text == "!streak" then
    local s = state(player)
    server.message(player, "Login streak: " .. s.streak .. " day(s), best " .. s.best .. ", " .. s.days .. " day(s) visited. "
      .. (s.claimed and "Today's reward is claimed. Tomorrow: " or "Today's reward: ") .. s.next .. ".")
    return true
  end
end)

-- Hot reload and shutdown: the roster (online heroes are only synced when the page is read) and the config bookkeeping are saved. Per-hero
-- counters, unlocks and the held rewards (the retry queue, "ach.pending") are written as they change, so they are in the storage already.
server.on("unload", function(reason)
  for _, player in ipairs(server.players()) do pcall(sync_roster, player) end
  pcall(save_roster)
  config_unload(reason)
end)

-- ---- Sacred Gear clients ----

if channel then
  channel:on(function(player, payload)
    if type(payload) == "table" and payload.op == "get" then push(player, state(player)) end
  end)
end

-- ---- endpoints and the page: every name passes through server.html ----

local function board()
  for _, player in ipairs(server.players()) do sync_roster(player) end   -- online heroes are current (not saved here)
  local rows = {}
  for _, entry in pairs(roster) do rows[#rows + 1] = entry end
  table.sort(rows, function(a, b)
    if (a.count or 0) ~= (b.count or 0) then return (a.count or 0) > (b.count or 0) end
    return tostring(a.name) < tostring(b.name)
  end)
  return rows
end

server.endpoint("players", function()
  local result = {}
  for i, entry in ipairs(board()) do
    local ids = {}
    for id in pairs(entry.u or {}) do ids[#ids + 1] = id end
    table.sort(ids)
    result[i] = { character = entry.name, level = entry.level, kills = entry.kills, streak = entry.streak, best_streak = entry.best,
      days = entry.days, count = entry.count or #ids, achievements = ids }
  end
  return result
end, { public = true, description = "every hero's achievements as JSON (character names only)" })

server.endpoint("achievements", function()
  local result = {}
  for i, d in ipairs(defs) do result[i] = { id = d.id, name = d.name, description = d.desc, reward = reward_text(d.reward) } end
  return result
end, { public = true, description = "the list of achievements as JSON" })

-- The page head and its style are achievements.html next to this script (server.file).

server.page("", function()
  local h = server.html
  local parts = {
    server.file("achievements.html"),
    '<h1>Sacred achievements</h1>',
    '<p class="note">', tostring(#defs), ' achievements. Hover a badge for what it asks; the streak counts days in a row on which the hero entered the world (UTC).</p><main>',
  }
  local rows = board()
  if #rows == 0 then parts[#parts + 1] = "<section><small>nobody yet</small></section>" end
  for _, entry in ipairs(rows) do
    local u = entry.u or {}
    parts[#parts + 1] = "<section><h2>" .. h(entry.name) .. "</h2><small>level " .. tostring(math.floor(entry.level or 0)) .. ", "
      .. tostring(math.floor(entry.kills or 0)) .. " kills, " .. (entry.count or 0) .. "/" .. #defs .. " achievements, streak "
      .. tostring(math.floor(entry.streak or 0)) .. " (best " .. tostring(math.floor(entry.best or 0)) .. ")</small><div class=\"chips\">"
    for _, d in ipairs(defs) do
      parts[#parts + 1] = '<span class="chip' .. (u[d.id] and " on" or "") .. '" title="' .. h(d.desc) .. '">' .. h(d.name) .. "</span>"
    end
    parts[#parts + 1] = "</div></section>"
  end
  parts[#parts + 1] = "</main>" .. config_footer() .. "</body></html>"
  return table.concat(parts)
end, { public = true, title = "Achievements" })
