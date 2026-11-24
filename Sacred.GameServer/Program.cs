using System.Net;
using System.Runtime.InteropServices;
using Sacred.GameServer;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lan;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Net.Sbt;
using Sacred.GameServer.Session;
using Sacred.GameServer.Web;
using Sacred.Network.Admission;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.Write(CommandLine.HelpText());
    return 0;
}

if (CommandLine.Validate(args) is { } commandLineError) { Console.Error.WriteLine(commandLineError); return 2; }   // before anything is bound
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
if (config.Auth.Enabled && config.GameDirectory is { } itemDir)
{
    host.ItemFootprints = Sacred.GameServer.Auth.Hero.ItemFootprints.Load(itemDir, out var itemError);
    if (itemError is not null) Log.Info($"WARNING: item tables not read from '{itemDir}' ({itemError}); unseen items are saved as 1x1.");
    else if (host.ItemFootprints is { } tables) Log.Info($"Item tables: {tables.FromRows} types with a Weapon.pak footprint (of {Sacred.GameServer.Auth.Hero.ItemFootprints.ResourceCount}) from '{itemDir}'.");
}

if (config.UdpPort > 0)
{
    // Sacred Bild players reach the game over its UDP transport (SBT/KCP) when the host answers, else they fall back to TCP.
    try { var udp = host.AddListener(inbox => new SbtListener(config.UdpPort, inbox, () => host.TcpPort)); host.SetAdminUdpPort(udp.Port); Log.Info($"UDP transport (Sacred Bild) on port {udp.Port}"); }
    catch (System.Net.Sockets.SocketException e) { Log.Info($"WARNING: UDP port {config.UdpPort} unavailable ({e.SocketErrorCode}), Sacred Bild players use TCP."); }
}

LobbyClient? lobby = null;
if (config.LobbyHost is { } lobbyHost)
{
    var publicIp = advertised.Equals(IPAddress.Loopback) ? 0u : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(advertised.MapToIPv4().GetAddressBytes());
    var record = host.LobbyRecord(announcedVersion, publicIp);
    lobby = new LobbyClient(lobbyHost, config.LobbyPort > 0 ? config.LobbyPort : 7066, record, host.StopToken);   // ends on the host's stop token
    host.AttachLobby(lobby, record);
    lobby.Start();
}

IAsyncDisposable? webUi = null;
if (WebUiLauncher.Enabled(args)) { webUi = WebUiLauncher.Start(args, host, out var webError); if (webError is not null) return 2; }

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; host.Stop(); });
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; host.Stop(); });
host.Run();
lobby?.Dispose();
if (webUi is not null) webUi.DisposeAsync().AsTask().GetAwaiter().GetResult();
host.Dispose();
return 0;
