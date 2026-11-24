using Sacred.GameServer.Net;
using Sacred.GameServer.Platform;
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
if (config.ParentWindow != 0) Log.Info($"Started by the game (window 0x{config.ParentWindow:X}).");
GameStatus.Notifier = GameStatus.ForWindow(config.ParentWindow);   // Windows only: PostMessage(hwnd, 0x410, code, value) like the original
GameStatus.Post(GameStatus.Started);
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
var announce = config.LanAnnounce;
if (announce && BindPolicy.IsLoopback(config.BindAddress)) { announce = false; Log.Info($"LAN announce is off: the server is bound to {config.BindAddress} only."); }
var lan = announce ? new LanAnnouncer(config, announcedVersion, advertised) : null;
GameServerHost host;
GameStatus.Post(GameStatus.TestingPort, 1);
try { host = new GameServerHost(config, rules, lan); }
catch (System.Net.Sockets.SocketException e)
{
    Log.Info($"ERROR: The TCP port seems to be already in use! ({e.SocketErrorCode})");
    GameStatus.Post(GameStatus.InitFailed);
    return 3;
}
if (config.Auth.Enabled && config.GameDirectory is { } itemDir)
{
    host.ItemFootprints = Sacred.GameServer.Auth.Hero.ItemFootprints.Load(itemDir, out var itemError);
    if (itemError is not null) Log.Info($"WARNING: item tables not read from '{itemDir}' ({itemError}); unseen items are saved as 1x1.");
    else if (host.ItemFootprints is { } tables) Log.Info($"Item tables: {tables.FromRows} types with a Weapon.pak footprint (of {Sacred.GameServer.Auth.Hero.ItemFootprints.ResourceCount}) from '{itemDir}'.");
}
if (config.GameDirectory is { } nameDir)
{
    // Display names for the web API and scripts; read once here, never in the game loop.
    host.Names = Sacred.GameServer.Data.NameService.Load(nameDir, out var nameError);
    if (nameError is not null) Log.Info($"WARNING: game texts not read from '{nameDir}' ({nameError}); names are left out.");
    else if (host.Names is { } names) Log.Info($"Game texts: {names.TypeCount} object types, language '{names.Language}'.");
}

if (config.Scripts.IsEnabled && config.Scripts.DataDirectory.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
{
    // Before the mods' own storage, --scripts-data named one file; now it is a folder with one file per mod.
    Log.Info($"ERROR: --scripts-data is a folder now (one {{mod}}.json per script), not a file: '{config.Scripts.DataDirectory}'. Use e.g. --scripts-data=script-data.");
    return 2;
}
if (config.Scripts.IsEnabled) host.StartScripts();

if (config.UdpPort > 0)
{
    // Sacred Bild players reach the game over its UDP transport (SBT/KCP) when the host answers, else they fall back to TCP.
    try { var udp = host.AddListener(inbox => new SbtListener(config.UdpPort, inbox, () => host.TcpPort, config.BindAddress)); host.SetAdminUdpPort(udp.Port); Log.Info($"UDP transport (Sacred Bild) on port {udp.Port}"); }
    catch (System.Net.Sockets.SocketException e) { Log.Info($"WARNING: UDP port {config.UdpPort} unavailable ({e.SocketErrorCode}), Sacred Bild players use TCP."); }
}

LobbyClient? lobby = null;
if (config.LobbyHost is { } lobbyHost)
{
    var publicIp = advertised.Equals(IPAddress.Loopback) ? 0u : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(advertised.MapToIPv4().GetAddressBytes());
    var record = host.LobbyRecord(announcedVersion, publicIp);
    lobby = new LobbyClient(lobbyHost, config.LobbyPort > 0 ? config.LobbyPort : 7066, record, host.StopToken) { Status = GameStatus.Post };   // ends on the host's stop token
    host.AttachLobby(lobby, record);
    lobby.Start();
}

if (lobby is null) GameStatus.Post(GameStatus.UpAndRunning);          // no lobby to wait for: the listener is open

IAsyncDisposable? webUi = null;
if (WebUiLauncher.Enabled(args)) { webUi = WebUiLauncher.Start(args, host, out var webError); if (webError is not null) return 2; }

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; host.Stop(); });
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; host.Stop(); });
host.Run();
var loopDone = host.ClockMs;
lobby?.Dispose();
var lobbyDone = host.ClockMs;
if (webUi is not null) webUi.DisposeAsync().AsTask().GetAwaiter().GetResult();
var webDone = host.ClockMs;
host.Dispose();
GameStatus.Post(GameStatus.GracefulExit);
if (host.StopRequestedMs >= 0)
    Log.Info($"Shutdown took {host.ClockMs - host.StopRequestedMs} ms (game loop {loopDone - host.StopRequestedMs}, lobby {lobbyDone - loopDone}, web UI {webDone - lobbyDone}, rest {host.ClockMs - webDone}).");
return 0;
