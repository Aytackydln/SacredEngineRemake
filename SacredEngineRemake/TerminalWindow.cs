using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SacredRemake;

internal static class TerminalWindow
{
    private const uint AttachParentProcess = unchecked((uint)-1);

    internal static void Open()
    {
        if (GetConsoleWindow() == 0 &&
            !AttachConsole(AttachParentProcess) &&
            !AllocConsole())
        {
            return;
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
