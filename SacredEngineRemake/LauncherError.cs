using System;
using System.Runtime.InteropServices;

namespace SacredRemake;

internal static class LauncherError
{
    private const uint MbIconError = 0x10;

    internal static void Show(string message, bool terminalMode)
    {
        if (terminalMode)
        {
            Console.Error.WriteLine(message);
            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
            return;
        }

        MessageBox(nint.Zero, message, "Sacred Engine Remake", MbIconError);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);
}
