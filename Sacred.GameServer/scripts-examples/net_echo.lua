-- Echo over custom net messages: a Sacred Gear mod (gear.net.channel('example:echo')) sends a table or a string to the server and
-- gets it back in capitals. Copy into the server's scripts folder (default ./scripts) to try it.
server.describe("Answers messages of Sacred Gear mods on the channel example:echo (a net.channel demo).")

local echo = net.channel("example:echo")
echo:on(function(player, payload)
  if type(payload) == "table" then
    payload.from = player.name
    echo:send(player, payload)
  else
    echo:send(player, string.upper(payload))
  end
end)
