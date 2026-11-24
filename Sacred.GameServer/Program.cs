using System.Net;
using System.Runtime.InteropServices;
using Sacred.GameServer;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lan;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Net.Sbt;
using Sacred.GameServer.Session;
using Sacred.Network.Admission;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        SacredGameServer - headless drop-in replacement for Sacred's GameServer.exe

        Reads Gameserver.cfg (NETWORK_PORT_TCP, NETWORK_PORT_LISTEN, NETWORK_PASSWORD, NETWORK_PRECONFIG_MODE,
        NETWORK_PRECONFIG_LEVEL, NETWORK_PRECONFIG_MAXPLAYERS, LADDER_EXPORT). Options use the same keys:
          --network-port-tcp=2006 --network-port-listen=2005 --network-password=secret   (Gameserver.cfg in the working or game directory)
          --game-dir=/srv/sacred    directory with Sacred.exe, tincat2.dll and the Pak/Bin data (required unless --accept-any-data)
          --accept-any-data         do not verify client version/data fingerprints (development only)
          --announce-version=N      client version to announce when no game data is read
          --trace                   log every received and sent message (opcode, sender, origin, first 32 bytes)
          --network-lobby=lobby.example.org --network-lobbyport=7066   register the game in an Open Internet lobby
          --lobby-channel=5         lobby channel of the game (GameServer.exe uses 5)
          --closed-net              the lobby is a Closed Internet lobby: fetch every player's hero from it (implied for channels other than 5)
          --udp-port=2105           Sacred Bild UDP transport (KCP) for players running Sacred Bild; 0 turns it off
          --server-name="My realm"  --public-address=203.0.113.5  --lan-announce=0  --tick-ms=20  --config=path
        """);
    return 0;
}

var config = GameServerConfig.Load(args);
Log.Open("Gameserver.log");
Log.TraceEnabled = config.Trace;
GoldLanCompatibility? compatibility = null;
if (!config.AcceptAnyData)
{
    if (config.GameDirectory is null) { Log.Info("ERROR: --game-dir is required (or pass --accept-any-data)."); return 2; }
    try { compatibility = GoldLanCompatibility.Read(config.GameDirectory); }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
    { Log.Info($"ERROR: cannot read game data from '{config.GameDirectory}': {e.Message}"); return 2; }
}
else if (config.GameDirectory is not null && config.AnnounceVersion is null)
{
    // Fingerprints are not enforced, but the client still hides games whose version differs from its own (red "!").
    try { compatibility = GoldLanCompatibility.Read(config.GameDirectory); }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { compatibility = null; }
}
var announcedVersion = config.AnnounceVersion ?? compatibility?.Version ?? 0;
if (announcedVersion == 0) Log.Info("WARNING: no client version known (pass --game-dir or --announce-version); clients will see the game as incompatible.");
var rules = new AdmissionRules(config.AcceptAnyData ? null : compatibility);
var advertised = config.PublicAddress is { } text && IPAddress.TryParse(text, out var parsed) ? parsed : IPAddress.Loopback;
var lan = config.LanAnnounce ? new LanAnnouncer(config, announcedVersion, advertised) : null;
GameServerHost host;
try { host = new GameServerHost(config, rules, lan); }
catch (System.Net.Sockets.SocketException e) { Log.Info($"ERROR: The TCP port seems to be already in use! ({e.SocketErrorCode})"); return 3; }

if (config.UdpPort > 0)
{
    // Sacred Bild players reach the game over its UDP transport (SBT/KCP) when the host answers, else they fall back to TCP.
    try { var udp = host.AddListener(inbox => new SbtListener(config.UdpPort, inbox, () => host.TcpPort)); Log.Info($"UDP transport (Sacred Bild) on port {udp.Port}"); }
    catch (System.Net.Sockets.SocketException e) { Log.Info($"WARNING: UDP port {config.UdpPort} unavailable ({e.SocketErrorCode}), Sacred Bild players use TCP."); }
}

LobbyClient? lobby = null;
if (config.LobbyHost is { } lobbyHost)
{
    var publicIp = advertised.Equals(IPAddress.Loopback) ? 0u : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(advertised.MapToIPv4().GetAddressBytes());
    var record = host.LobbyRecord(announcedVersion, publicIp);
    lobby = new LobbyClient(lobbyHost, config.LobbyPort > 0 ? config.LobbyPort : 7066, record);
    host.AttachLobby(lobby, record);
    lobby.Start();
}

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; host.Stop(); });
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; host.Stop(); });
host.Run();
lobby?.Dispose();
host.Dispose();
return 0;
