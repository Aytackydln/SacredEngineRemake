using System;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Platform;

/// <summary>Represents the normal window bounds and whether the window is maximized.</summary>
public readonly record struct WindowedBounds(int X, int Y, int Width, int Height, bool Maximized);

internal static class WindowedBoundsPersistence
{
    private const int VirtualScreenLeftMetric = 76;
    private const int VirtualScreenTopMetric = 77;
    private const int VirtualScreenWidthMetric = 78;
    private const int VirtualScreenHeightMetric = 79;
    private const int MinimumVisibleWindowEdge = 64;
    private static readonly nint PerMonitorDpiAwareV2 = new(-4);

    public static void EnablePerMonitorDpiAwareness()
    {
        if (User32.SetProcessDpiAwarenessContext(PerMonitorDpiAwareV2))
            EngineLog.WriteLine("Window DPI awareness: per-monitor v2.");
    }

    public static WindowedBounds ConstrainToVirtualDesktop(WindowedBounds bounds)
    {
        var left = User32.GetSystemMetrics(VirtualScreenLeftMetric);
        var top = User32.GetSystemMetrics(VirtualScreenTopMetric);
        var width = Math.Max(1, User32.GetSystemMetrics(VirtualScreenWidthMetric));
        var height = Math.Max(1, User32.GetSystemMetrics(VirtualScreenHeightMetric));
        var right = left + width;
        var bottom = top + height;
        var x = Math.Clamp(bounds.X, left - bounds.Width + MinimumVisibleWindowEdge, right - MinimumVisibleWindowEdge);
        var y = Math.Clamp(bounds.Y, top, bottom - MinimumVisibleWindowEdge);
        return bounds with { X = x, Y = y };
    }

    public static bool TryCapture(nint hwnd, out WindowedBounds bounds)
    {
        var maximized = User32.IsZoomed(hwnd);
        if (maximized || !User32.GetWindowRect(hwnd, out var rect))
        {
            bounds = default;
            return false;
        }

        bounds = new WindowedBounds(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top),
            maximized);
        return true;
    }
}
