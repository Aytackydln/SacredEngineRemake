using System.Runtime.InteropServices;

namespace Sacred.GameServer.Platform;

/// <summary>Where the server's status messages go (see <see cref="GameStatus"/>).</summary>
public interface IStatusNotifier
{
    void Post(int code, int value);
}

/// <summary>
/// The status messages GameServer.exe posts to the window of the game that started it ("quiet &lt;HWND&gt;"):
/// <c>PostMessageA(hwnd, 0x410, code, value)</c> (FUN_004C27B0 and its dispatcher FUN_004C3F20). Sacred.exe handles them in
/// FUN_007EF640 (called from its window procedure at 0x813561): each code logs "Gameserver status: ..." and, for the error codes,
/// shows a message to the player. Windows only, and only when the game passed its window handle; everywhere else this does nothing.
/// </summary>
public static class GameStatus
{
    public const uint WindowMessage = 0x410;

    /// <summary>1 started: the process is up (after the OS check and the priority class).</summary>
    public const int Started = 1;
    /// <summary>2 testing port: before each attempt to open the network session; the value counts the attempts (1..5).</summary>
    public const int TestingPort = 2;
    /// <summary>3 doing full reset: the original restarts its session loop (not sent: this server never restarts).</summary>
    public const int FullReset = 3;
    /// <summary>4 up and running: the lobby accepted the game (or, without a lobby, the listener is open).</summary>
    public const int UpAndRunning = 4;
    /// <summary>5 graceful exit.</summary>
    public const int GracefulExit = 5;
    /// <summary>6 network failed: the original quits after losing the lobby (this server keeps retrying, so it is not sent).</summary>
    public const int NetworkFailed = 6;
    /// <summary>7 init failed: the network session could not be opened (port in use).</summary>
    public const int InitFailed = 7;
    /// <summary>8 could not connect to the lobby server.</summary>
    public const int LobbyConnectFailed = 8;
    /// <summary>9 lobby login failed; the value is the error number.</summary>
    public const int LobbyLoginFailed = 9;
    /// <summary>10 backconnect failed (the lobby could not reach the game's port; not reported by sacred-lobby).</summary>
    public const int BackconnectFailed = 10;
    /// <summary>11 wrong OS version (never sent).</summary>
    public const int WrongOs = 11;

    private static IStatusNotifier _notifier = NullNotifier.Instance;

    /// <summary>The destination; replaced at start-up when the game gave its window handle, and by tests.</summary>
    public static IStatusNotifier Notifier { get => _notifier; set => _notifier = value ?? NullNotifier.Instance; }

    public static void Post(int code, int value = 0)
    {
        try { _notifier.Post(code, value); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { }
    }

    /// <summary>The notifier for a window handle: real on Windows with a handle, else a no-op.</summary>
    public static IStatusNotifier ForWindow(long window) =>
        window != 0 && OperatingSystem.IsWindows() ? new WindowNotifier((nint)window) : NullNotifier.Instance;

    private sealed class NullNotifier : IStatusNotifier
    {
        public static readonly NullNotifier Instance = new();
        public void Post(int code, int value) { }
    }

    private sealed class WindowNotifier(nint window) : IStatusNotifier
    {
        // Blittable P/Invoke (no marshalling stub, AOT safe); LibraryImport would need AllowUnsafeBlocks, which this project keeps off.
        [DllImport("user32.dll", EntryPoint = "PostMessageA", ExactSpelling = true)]
        private static extern int PostMessageA(nint window, uint message, nint wParam, nint lParam);

        public void Post(int code, int value)
        {
            if (!OperatingSystem.IsWindows()) return;
            _ = PostMessageA(window, WindowMessage, code, value);
        }
    }
}
