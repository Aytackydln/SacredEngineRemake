-- Loot rewards: now and then a defeated creature leaves an extra piece of equipment for the player who killed it, made for
-- the player's class and level. The rarity is rolled from the table below (weights, not percent). Needs --game-dir (the
-- server generates the item from the game's own tables). Copy into the server's scripts folder to use it.
--
-- Qualities are the game's generation qualities 0..14 (higher = rarer). Adjust the weights to make groups rarer or commoner.
server.describe("Some kills drop an extra item for the killer's class and level; rarity from a weight table, every 250 kills a rare one.")

local drop_chance = 0.02            -- 2 % of kills
local every_n_kills = 250           -- plus one guaranteed rare item every 250 kills

local rarities = {
  { name = "common",    quality = 0,  weight = 600 },
  { name = "uncommon",  quality = 3,  weight = 250 },
  { name = "rare",      quality = 6,  weight = 100 },
  { name = "epic",      quality = 9,  weight = 40 },
  { name = "legendary", quality = 12, weight = 10 },
}

local function roll_rarity(minimum)
  local total = 0
  for _, r in ipairs(rarities) do if r.quality >= (minimum or 0) then total = total + r.weight end end
  local pick = server.random() * total
  for _, r in ipairs(rarities) do
    if r.quality >= (minimum or 0) then
      pick = pick - r.weight
      if pick <= 0 then return r end
    end
  end
  return rarities[#rarities]
end

local function reward(player, rarity, why)
  local item, err = server.drop_item(player, { quality = rarity.quality })
  if not item then
    server.log("no reward for " .. player.name .. ": " .. tostring(err))
    return
  end
  if rarity.quality >= 6 then
    server.broadcast(player.character .. " found a " .. rarity.name .. " " .. (item.name ~= "" and item.name or item.category) .. " " .. why .. "!")
  else
    server.message(player, "A " .. rarity.name .. " item dropped for you " .. why .. ".")
  end
end

server.on("kill", function(player, monster)
  local kills = server.add(player, "loot.kills")
  if kills % every_n_kills == 0 then
    reward(player, roll_rarity(6), "for " .. kills .. " kills")
  elseif server.random() < drop_chance then
    reward(player, roll_rarity(), "as a bonus")
  end
end)
