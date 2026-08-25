using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Platform;

/// <summary>Captures precisely the current game client area for repeatable visual comparisons.</summary>
internal static class WindowScreenshotCapture
{
    private static readonly nint HwndTopmost = new(-1);
    private static readonly nint HwndNotTopmost = new(-2);
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint ScreenshotWindowFlags =
        SetWindowPosNoSize | SetWindowPosNoMove | SetWindowPosNoActivate;

    public static string Save(Win32Window window, string gameDirectory, string? label)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var width = window.ClientWidth;
        var height = window.ClientHeight;
        var directory = Path.Combine(gameDirectory, "Screenshots", "Remake");
        Directory.CreateDirectory(directory);

        var safeLabel = SanitizeLabel(label);
        var filename = $"{DateTime.Now:yyyyMMdd-HHmmssfff}{safeLabel}.png";
        var path = Path.Combine(directory, filename);
        // PrintWindow cannot retrieve a D3D12 swap-chain image. Put our own
        // interactive window in front, then capture the exact client rectangle.
        // Foreground activation can be rejected when another application owns
        // input. A temporary topmost placement makes the client-area capture
        // deterministic even in that case, and is removed immediately after.
        User32.SetWindowPos(window.Hwnd, HwndTopmost, 0, 0, 0, 0, ScreenshotWindowFlags);
        try
        {
            User32.BringWindowToTop(window.Hwnd);
            User32.SetForegroundWindow(window.Hwnd);
            Thread.Sleep(100);
            var origin = new User32.Point();
            if (!User32.ClientToScreen(window.Hwnd, ref origin))
                throw new InvalidOperationException("Could not resolve the client-area screen position.");

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                origin.X,
                origin.Y,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
            bitmap.Save(path, ImageFormat.Png);
        }
        finally
        {
            User32.SetWindowPos(window.Hwnd, HwndNotTopmost, 0, 0, 0, 0, ScreenshotWindowFlags);
        }
        return path;
    }

    private static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var characters = label.Trim().ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (Array.IndexOf(invalid, characters[index]) >= 0)
                characters[index] = '_';
        }

        return "-" + new string(characters);
    }
}
