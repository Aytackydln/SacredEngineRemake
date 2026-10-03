using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sacred.Engine.Platform;

namespace SacredRemake;

internal static class LauncherError
{
    private const uint MbIconError = 0x10;
    private const uint MbTopmost = 0x40000;

    internal static void Show(string message, bool terminalMode)
    {
        LauncherErrorLog.Write(message);

        if (terminalMode)
        {
            Console.Error.WriteLine(message);
            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
            return;
        }

        // Gamescope needs the dialog associated with the game's top-level window.
        var owner = GetActiveWindow();
        if (owner == 0)
        {
            using var process = Process.GetCurrentProcess();
            owner = process.MainWindowHandle;
        }

        if (owner != 0)
        {
            ShowDialog(owner, message);
            return;
        }

        // Startup may fail before the game creates a window. Give the dialog a
        // normal game window as its owner without initializing the renderer.
        using var errorWindow = new Win32Window("Sacred Engine Remake", 1280, 800);
        ShowDialog(errorWindow.Hwnd, message);
    }

    private static void ShowDialog(nint owner, string message)
    {
        if (MessageBox(owner, message, "Sacred Engine Remake", MbIconError | MbTopmost) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not display the game error dialog.");
    }

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", ExactSpelling = true,
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);
}
