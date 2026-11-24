using System.Net;
using System.Globalization;

namespace Sacred.GameServer.Config;

/// <summary>Pre-configured game mode (Gameserver.cfg NETWORK_PRECONFIG_MODE). Numeric order follows the original menu; the flag mapping is a best effort.</summary>
public enum PreconfigMode { None = 0, FreeGame = 1, PlayerKiller = 2, Campaign = 3, CampaignUnderworld = 4 }

/// <summary>
/// Settings of the original dedicated server (Gameserver.cfg, KEY=VALUE) plus remake-only extras.
/// Command line options use the same names as <c>--network-port-tcp=2005</c> (case-insensitive, '_' or '-').
/// </summary>
public sealed class GameServerConfig
{
    /// <summary>Game TCP port (original NETWORK_PORT_TCP, default 2006 at 0x4F5BAC).</summary>
    public int TcpPort { get; set; } = 2006;
    /// <summary>UDP port the clients listen on for LAN announcements (original NETWORK_PORT_LISTEN, default 2005 at 0x4F59FC).</summary>
    public int ListenPort { get; set; } = 2005;
    public int LobbyPort { get; set; }
    /// <summary>UDP port of the Sacred Bild transport (SBT/KCP); 0 turns it off. Sacred Bild's default is 2105.</summary>
    public int UdpPort { get; set; } = 2105;
    public string? Password { get; set; }
    public PreconfigMode Mode { get; set; }
    public int Level { get; set; }
    public int MaxPlayers { get; set; } = 16;
    public bool LadderExport { get; set; }

    // Remake extras
    public string ServerName { get; set; } = "Sacred Gameserver";
    public string? GameDirectory { get; set; }
    public string? PublicAddress { get; set; }
    public bool AcceptAnyData { get; set; }
    public bool LanAnnounce { get; set; } = true;
    public int TickMilliseconds { get; set; } = 20;
    public int MaxPasswordTries { get; set; } = 3;
    /// <summary>Per-message wire trace in the log.</summary>
    public bool Trace { get; set; }
    /// <summary>Window handle of the game that started the server (decimal argument "quiet &lt;HWND&gt;"), 0 = started by hand.</summary>
    public long ParentWindow { get; set; }
    /// <summary>Address the TCP and UDP listeners bind (BIND_ADDRESS / NETWORK_BIND / --bind); null = every interface like the original.</summary>
    public IPAddress? BindAddress { get; set; }
    /// <summary>Client data version announced on LAN and to the lobby when no game data is read (--accept-any-data).</summary>
    public ushort? AnnounceVersion { get; set; }
    public string? LobbyHost { get; set; }
    /// <summary>The lobby is a Closed Internet lobby (every channel closed, e.g. sacred-lobby `closed_net: true`): fetch each player's hero from it.
    /// Implied for any lobby channel other than 5.</summary>
    public bool ClosedNet { get; set; }
    /// <summary>Lobby channel of message 12 (GameServer.exe always sends 5, Open Net).</summary>
    public int LobbyChannel { get; set; } = 5;
    /// <summary>Optional authoritative mode (server-owned state, validated client values); see Auth/.</summary>
    public Auth.AuthoritativeOptions Auth { get; } = new();
    /// <summary>Flood protection (on with authoritative mode unless FLOOD_PROTECTION says otherwise); see Session/FloodGuard.cs.</summary>
    public Session.FloodOptions Flood { get; } = new();
    /// <summary>Custom net opcodes for mod channels (Wire/NetOpsWire.cs, Session/GameServerHost.Net.cs).</summary>
    public Session.NetOpsOptions NetOps { get; } = new();
    /// <summary>Server-side Lua scripts (Scripting/); on when the scripts folder exists.</summary>
    public Scripting.ScriptOptions Scripts { get; } = new();

    public static GameServerConfig Load(string[] args)
    {
        var config = new GameServerConfig();
        var cfgPath = "Gameserver.cfg";
        foreach (var arg in args)
            if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase)) cfgPath = arg[9..];
        var explicitCfg = args.Any(a => a.StartsWith("--config=", StringComparison.OrdinalIgnoreCase));
        if (!explicitCfg && !File.Exists(cfgPath) && GameDirectoryArgument(args) is { } gameDir)
            cfgPath = Path.Combine(gameDir, "Gameserver.cfg"); // the original reads it next to GameServer.exe
        if (File.Exists(cfgPath))
            foreach (var line in File.ReadLines(cfgPath)) config.ApplyLine(line);
        foreach (var arg in args)
        {
            if (arg.Equals("tincatlog", StringComparison.OrdinalIgnoreCase)) config.Trace = true;          // GameServer.exe: DAT_006340f5
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase)) continue;
            config.ApplyLine(arg[2..]);
        }
        config.ParentWindow = CommandLine.WindowHandle(args);
        return config;
    }

    private static string? GameDirectoryArgument(string[] args)
    {
        foreach (var arg in args)
            foreach (var key in new[] { "--game-dir=", "--game_dir=", "--game-directory=", "--game_directory=" })
                if (arg.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return arg[key.Length..];
        return null;
    }

    public void ApplyLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] is '#' or ';' or '/') return;
        // Command line uses KEY=VALUE; Gameserver.cfg lines are KEY:VALUE in the shipped file. Take whichever comes first.
        var equals = line.IndexOf('='); var colon = line.IndexOf(':');
        var split = equals < 0 ? colon : colon < 0 ? equals : Math.Min(equals, colon);
        var key = (split < 0 ? line : line[..split]).Trim().Replace('-', '_').ToUpperInvariant();
        var value = split < 0 ? "1" : line[(split + 1)..].Trim();
        switch (key)
        {
            case "BIND_ADDRESS" or "NETWORK_BIND" or "BIND": BindAddress = IPAddress.TryParse(value, out var bind) ? bind : BindAddress; break;
            case "NETWORK_PORT_TCP" or "PORT": TcpPort = Int(value, TcpPort); break;
            case "NETWORK_PORT_LISTEN" or "LISTEN_PORT": ListenPort = Int(value, ListenPort); break;
            case "NETWORK_LOBBYPORT": LobbyPort = Int(value, LobbyPort); break;
            case "UDP_PORT" or "NETWORK_PORT_UDP": UdpPort = Math.Clamp(Int(value, UdpPort), 0, 65535); break;
            case "NETWORK_PASSWORD" or "PASSWORD": Password = value.Length == 0 ? null : value; break;
            case "NETWORK_PRECONFIG_MODE": Mode = (PreconfigMode)Math.Clamp(Int(value, 0), 0, 4); break;
            case "NETWORK_PRECONFIG_LEVEL": Level = Math.Clamp(Int(value, 0), 0, 5); break;
            case "NETWORK_PRECONFIG_MAXPLAYERS": MaxPlayers = Math.Clamp(Int(value, 16), 1, 16); break;
            case "LADDER_EXPORT": LadderExport = Int(value, 0) != 0; break;
            case "SERVER_NAME" or "NAME": ServerName = value; break;
            case "GAME_DIR" or "GAME_DIRECTORY": GameDirectory = value; break;
            case "PUBLIC_ADDRESS": PublicAddress = value; break;
            case "ACCEPT_ANY_DATA": AcceptAnyData = Int(value, 1) != 0; break;
            case "LAN_ANNOUNCE": LanAnnounce = Int(value, 1) != 0; break;
            case "TICK_MS": TickMilliseconds = Math.Clamp(Int(value, 20), 5, 100); break;
            case "MAX_PASSWORD_TRIES": MaxPasswordTries = Math.Clamp(Int(value, 3), 1, 100); break;
            case "LOBBY_HOST" or "NETWORK_LOBBY": LobbyHost = value.Length == 0 ? null : value; break;
            case "LOBBY_CHANNEL": LobbyChannel = Int(value, LobbyChannel); break;
            case "CLOSED_NET": ClosedNet = Int(value, 1) != 0; break;
            case "TRACE": Trace = Int(value, 1) != 0; break;
            case "ANNOUNCE_VERSION" or "VERSION": AnnounceVersion = (ushort)Math.Clamp(Int(value, 0), 0, ushort.MaxValue); break;
            default: if (!Auth.Apply(key, value) && !Flood.Apply(key, value) && !NetOps.Apply(key, value)) Scripts.Apply(key, value); break;
        }
    }

    private static int Int(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    /// <summary>The session game-flags word advertised in the LAN announcement and opcode 0x1E.</summary>
    public uint GameFlags()
    {
        // FUN_004eda60 starts every session as 0x20 (free game; | 1 while the server window is shown, which a headless
        // server is not: "quiet"). The preconfig setters 0x4EDA80/0x4EDAD0/0x4EDB00 replace the mode bits.
        uint flags = Mode switch
        {
            PreconfigMode.PlayerKiller => 0x40,
            PreconfigMode.Campaign => 0x10,
            PreconfigMode.CampaignUnderworld => 0x40000,
            _ => 0x20,
        };
        if (Password is not null) flags |= 0x2; // server init (0x4E8C9C): password protected
        flags |= Level switch { 1 => 0x100u, 2 => 0x200u, 3 => 0x400u, 4 => 0x800u, _ => 0u };
        return flags;
    }
}
