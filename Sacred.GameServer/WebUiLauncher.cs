using System.Runtime.CompilerServices;
using Sacred.GameServer.Session;
using Sacred.GameServer.Web;

namespace Sacred.GameServer;

/// <summary>
/// Starts the optional web UI. Kestrel and everything it pulls in are only touched inside <see cref="Start"/> (not inlined), so a
/// server run without <c>--webserver</c> never loads them; the headless core pays nothing but the binary size.
/// </summary>
internal static class WebUiLauncher
{
    /// <summary>Cheap check that avoids loading configuration code: the flag, a bare WebUi switch, or any WebUi setting from file/environment.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Enabled(string[] args) => WebUiOptions.From(WebUiOptions.BuildConfiguration(args)).Enabled;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IAsyncDisposable? Start(string[] args, GameServerHost host, out string? error)
    {
        error = null;
        var options = WebUiOptions.From(WebUiOptions.BuildConfiguration(args));
        if (WebUiServer.Validate(options) is { } problem) { error = problem; Log.Info("ERROR: " + problem); return null; }
        try
        {
            host.TrackHeroes = true;                                         // player data for /api/heroes and /public/heroes
            var server = new WebUiServer(options, host, Log.Info);
            server.StartAsync().GetAwaiter().GetResult();
            return server;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Log.Info($"WARNING: web UI not started: {e.Message}");
            return null;
        }
    }
}
