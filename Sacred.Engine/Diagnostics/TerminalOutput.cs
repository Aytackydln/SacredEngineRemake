using System;
using System.Collections.Generic;

namespace Sacred.Engine.Diagnostics;

/// <summary>Keeps a bounded, thread-safe transcript for the in-game terminal.</summary>
internal static class TerminalOutput
{
    private const int MaximumLines = 1000;
    private static readonly object Sync = new();
    private static readonly Queue<string> Lines = new();
    private static long _version;

    public static void Append(string message)
    {
        lock (Sync)
        {
            foreach (var line in message.Replace("\r", "").Split('\n'))
            {
                Lines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
                if (Lines.Count > MaximumLines)
                    Lines.Dequeue();
            }
            _version++;
        }
    }

    public static (long Version, string[] Lines) Snapshot()
    {
        lock (Sync)
            return (_version, Lines.ToArray());
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Lines.Clear();
            _version++;
        }
    }
}
