-- Kill milestones: announces to everybody when a player has defeated 50, 100, 200, ... creatures of one kind, and answers
-- "!kills" in chat with the player's own count. Copy into the server's scripts folder (default ./scripts) to use it.
--
-- Counts are kept per hero across sessions (server.add stores them in script-data.json).
server.describe("Announces kill milestones (50, 100, 200, ...) per creature and answers !kills in chat.")

local milestones = { [50] = true, [100] = true, [200] = true, [500] = true, [1000] = true, [2000] = true, [5000] = true }

-- monster.name is the game's own name ("Orc Warrior") when the server runs with --game-dir; without the game's texts the
-- type number is used.
local function defeated(monster, count)
  if monster.name then return monster.name .. " " .. count .. " times" end
  return count .. " creatures of type " .. monster.type
end

server.on("kill", function(player, monster)
  local total = server.add(player, "kills")
  local of_kind = server.add(player, "kills." .. monster.type)
  if milestones[of_kind] then
    server.broadcast(player.character .. " has defeated " .. defeated(monster, of_kind) .. "!")
  end
  if total % 1000 == 0 then
    server.broadcast(player.character .. " has defeated " .. total .. " creatures in total!")
  end
end)

server.on("chat", function(player, text)
  if text == "!kills" then
    server.message(player, "You have defeated " .. server.get(player, "kills", 0) .. " creatures.")
    return true -- not shown to the others
  end
end)

server.on("enter", function(player)
  server.message(player, "Welcome, " .. player.character .. "! Type !kills to see your kill count.")
end)
