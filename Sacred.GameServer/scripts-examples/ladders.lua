-- Ladders and a dashboard page, as a mod with its own storage (script-data/ladders.json). The game server keeps none of this.
-- Copy into the server's scripts folder to use it.
--
--   /mods/ladders/              the dashboard: cards for the top heroes, the best hunter per creature, recent deaths
--   /mods/ladders/top?board=kills&n=10   JSON rows of a ladder (kills, level, online)
--   /mods/ladders/by-creature            CSV: creature, best hunter, kills
-- With WebUi:PublicApi=true the same addresses also work under /public/mods/ladders/... for anybody (embeds, overlays).
-- "!top" in chat shows the kill ladder to the player who asked.

server.describe("Ladders (kills, level, time online), best hunter per creature and recent deaths, as a dashboard page and JSON/CSV endpoints.")

local size = 10
local keep_deaths = 20

-- Creatures seen so far, by display name: several creature types share one name (Thief, Brigand, ... come in variants),
-- so kills are counted per name. Kept in this mod's storage.
local creatures = server.load("creatures", {})

local function remember_level(player)
  if player.level > server.get(player, "level", 0) then server.set(player, "level", player.level) end
end

server.on("kill", function(player, monster)
  server.add(player, "kills")
  local creature = monster.name or ("creature type " .. monster.type)
  server.add(player, "kills." .. creature)
  if not creatures[creature] then
    creatures[creature] = true
    server.save("creatures", creatures)
  end
  remember_level(player)
end)
server.on("enter", remember_level)
server.on("leave", remember_level)

-- Recently died heroes: a short list in this mod's storage.
server.on("death", function(player, by)
  local deaths = server.load("deaths", {})
  local cause = (by.player and by.player.character) or by.name or (by.type and ("creature type " .. by.type)) or "unknown"
  table.insert(deaths, 1, { name = player.character, level = player.level, by = cause, time = server.time() })
  while #deaths > keep_deaths do table.remove(deaths) end
  server.save("deaths", deaths)
end)

-- Online time: every minute each connected hero gets 60 seconds (a crash loses at most one minute).
server.every(60, function()
  for _, player in ipairs(server.players()) do server.add(player, "online", 60) end
end)

local boards = {
  kills = { key = "kills", scale = 1 },
  level = { key = "level", scale = 1 },
  online = { key = "online", scale = 3600 },
}

local function rows(board, n)
  local result = {}
  for i, row in ipairs(server.top(board.key, n or size)) do result[i] = { rank = i, name = row.name, value = row.value / board.scale } end
  return result
end

local function hunters()
  local result = {}
  for creature in pairs(creatures) do
    local best = server.top("kills." .. creature, 1)[1]
    if best then result[#result + 1] = { creature = creature, hunter = best.name, kills = best.value } end
  end
  table.sort(result, function(a, b) return a.kills > b.kills end)
  return result
end

server.on("chat", function(player, text)
  if text ~= "!top" then return end
  for i, row in ipairs(server.top("kills", 5)) do server.message(player, i .. ". " .. row.name .. ": " .. row.value .. " kills") end
  return true
end)

-- ---- endpoints ----

server.endpoint("top", function(request)
  local board = boards[request.query.board or "kills"]
  if not board then return "unknown board", "text/plain", 404 end
  return rows(board, math.min(tonumber(request.query.n) or size, 100))
end, { public = true, description = "ladder rows as JSON: ?board=kills|level|online&n=10" })

server.endpoint("by-creature", function()
  return server.csv(hunters(), { "creature", "hunter", "kills" }), "text/csv"
end, { public = true, description = "best hunter per creature as CSV" })

-- ---- the dashboard page: built on the server, every name passes through server.html ----

-- The page itself (head, style, headline) is ladders.html next to this script; the cards replace its <!--cards--> marker.
local shell = server.file("ladders.html")

local function card(title, items, line)
  if #items == 0 then return "<section><h2>" .. server.html(title) .. "</h2><small>nobody yet</small></section>" end
  local parts = { "<section><h2>", server.html(title), "</h2><ol>" }
  for _, item in ipairs(items) do parts[#parts + 1] = line(item) end
  parts[#parts + 1] = "</ol></section>"
  return table.concat(parts)
end

server.page("", function()
  local h = server.html
  local cards = table.concat({
    card("Most creatures defeated", rows(boards.kills), function(r) return "<li><b>" .. h(r.name) .. "</b> " .. r.value .. "</li>" end),
    card("Highest level", rows(boards.level), function(r) return "<li><b>" .. h(r.name) .. "</b> level " .. r.value .. "</li>" end),
    card("Longest online", rows(boards.online), function(r) return "<li><b>" .. h(r.name) .. "</b> " .. string.format("%.1f h", r.value) .. "</li>" end),
    card("Best hunter per creature", hunters(), function(r) return "<li>" .. h(r.creature) .. ": <b>" .. h(r.hunter) .. "</b> (" .. r.kills .. ")</li>" end),
    card("Recently died", server.load("deaths", {}), function(d)
      return "<li><b>" .. h(d.name) .. "</b> <small>level " .. d.level .. ", by " .. h(d.by) .. "</small></li>" end),
  })
  return (shell:gsub("<!%-%-cards%-%->", function() return cards end))
end, { public = true, title = "Ladders dashboard" })
