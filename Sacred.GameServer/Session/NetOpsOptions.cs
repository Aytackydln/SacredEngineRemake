using System.Globalization;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Custom net opcodes (<see cref="NetOpsWire"/>): <c>NETOPS_*</c> keys of Gameserver.cfg / the command line, and the environment
/// variables <c>NETOPS_CONTROL</c> / <c>NETOPS_DATA</c> (the ids a server with custom opcodes of its own needs to change; clients are
/// told in the handshake). Defaults 0x1E0 / 0x1E1. Ids may be written as decimal or 0x hex.
/// </summary>
public sealed class NetOpsOptions
{
    public bool Enabled { get; set; } = true;
    public ushort Control { get; private set; } = NetOpsWire.DefaultControl;
    public ushort Data { get; private set; } = NetOpsWire.DefaultData;
    /// <summary>Largest data bytes of one wire message (the fragment size clients are told to use).</summary>
    public int MaxFragment { get; set; } = 4000;
    /// <summary>Largest reassembled message (relayed messages are counted as well).</summary>
    public int MaxMessage { get; set; } = 256 * 1024;
    /// <summary>Messages one sender may have in flight (started, not finished).</summary>
    public int MaxInFlight { get; set; } = 8;
    /// <summary>Per session token bucket on received net ops bytes: refill per second and capacity (on top of the flood guard).</summary>
    public int BytesPerSecond { get; set; } = 64 * 1024;
    public int Burst { get; set; } = 256 * 1024;
    /// <summary>Different channels one client may use.</summary>
    public int MaxChannelsPerClient { get; set; } = 64;
    public int MaxNameLength { get; set; } = 64;
    /// <summary>Bytes of unfinished messages the server holds for one session / for all sessions.</summary>
    public int ReassemblyPerSession { get; set; } = 1024 * 1024;
    public int ReassemblyTotal { get; set; } = 8 * 1024 * 1024;
    /// <summary>A started message without a new fragment for this long is dropped.</summary>
    public int FragmentTimeoutSeconds { get; set; } = 10;
    /// <summary>Lua instructions one channel handler may run (the script budget is the upper limit).</summary>
    public int HandlerBudget { get; set; } = 200_000;

    public NetOpsOptions()
    {
        var control = ParseId(Environment.GetEnvironmentVariable("NETOPS_CONTROL"));
        var data = ParseId(Environment.GetEnvironmentVariable("NETOPS_DATA"));
        SetIds(control ?? Control, data ?? Data);
    }

    /// <summary>Sets both ids; false (and nothing changed) when they are not valid (above 0x1D5, below 0x400, different).</summary>
    public bool SetIds(int control, int data)
    {
        if (control is < 0 or > ushort.MaxValue || data is < 0 or > ushort.MaxValue || !NetOpsWire.ValidIds((ushort)control, (ushort)data)) return false;
        Control = (ushort)control; Data = (ushort)data;
        return true;
    }

    public bool Apply(string key, string value)
    {
        switch (key)
        {
            case "NETOPS": Enabled = Int(value, 1) != 0; return true;
            case "NETOPS_CONTROL": SetIds(ParseId(value) ?? Control, Data); return true;
            case "NETOPS_DATA": SetIds(Control, ParseId(value) ?? Data); return true;
            case "NETOPS_MAX_FRAGMENT": MaxFragment = Math.Clamp(Int(value, MaxFragment), 64, 4000); return true;
            case "NETOPS_MAX_MESSAGE": MaxMessage = Math.Clamp(Int(value, MaxMessage), 1024, 4 * 1024 * 1024); return true;
            case "NETOPS_MAX_IN_FLIGHT": MaxInFlight = Math.Clamp(Int(value, MaxInFlight), 1, 64); return true;
            case "NETOPS_RATE": BytesPerSecond = Math.Clamp(Int(value, BytesPerSecond), 1024, 64 * 1024 * 1024); return true;
            case "NETOPS_BURST": Burst = Math.Clamp(Int(value, Burst), 4096, 64 * 1024 * 1024); return true;
            case "NETOPS_MAX_CHANNELS": MaxChannelsPerClient = Math.Clamp(Int(value, MaxChannelsPerClient), 1, 4096); return true;
            case "NETOPS_MAX_NAME": MaxNameLength = Math.Clamp(Int(value, MaxNameLength), 4, 256); return true;
            case "NETOPS_REASSEMBLY_SESSION": ReassemblyPerSession = Math.Clamp(Int(value, ReassemblyPerSession), 4096, 64 * 1024 * 1024); return true;
            case "NETOPS_REASSEMBLY_TOTAL": ReassemblyTotal = Math.Clamp(Int(value, ReassemblyTotal), 4096, 512 * 1024 * 1024); return true;
            case "NETOPS_FRAGMENT_TIMEOUT": FragmentTimeoutSeconds = Math.Clamp(Int(value, FragmentTimeoutSeconds), 1, 600); return true;
            case "NETOPS_HANDLER_BUDGET": HandlerBudget = Math.Clamp(Int(value, HandlerBudget), 1000, 100_000_000); return true;
        }
        return false;
    }

    public static int? ParseId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        return int.TryParse(hex ? text[2..] : text, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static int Int(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
