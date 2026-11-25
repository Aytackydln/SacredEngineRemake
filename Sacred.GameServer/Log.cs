using System.Text;

namespace Sacred.GameServer;

/// <summary>Minimal logger: console plus Gameserver.log (original file name). Not used on per-packet paths.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? _file;

    public static void Open(string path)
    {
        try { _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true }; }
        catch (IOException) { _file = null; }
        catch (UnauthorizedAccessException) { _file = null; }
    }

    /// <summary>Per-message wire trace (TRACE=1 / --trace). Off by default; only formats strings when enabled.</summary>
    public static bool TraceEnabled { get; set; }

    /// <summary>Logs one Gold message: direction, player, opcode, sender/origin, length and up to 32 payload bytes.</summary>
    public static void Trace(string direction, int slot, ushort opcode, byte sender, byte origin, ReadOnlySpan<byte> payload)
    {
        if (!TraceEnabled) return;
        var shown = payload.Length > 32 ? payload[..32] : payload;
        Info($"{direction} #{slot} 0x{opcode:X3} s{sender} o{origin} len {payload.Length}: {Convert.ToHexString(shown)}{(payload.Length > 32 ? "..." : "")}");
    }

    public static void Info(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        lock (Gate)
        {
            Console.Out.WriteLine(line);
            _file?.WriteLine(line);
        }
    }
}
