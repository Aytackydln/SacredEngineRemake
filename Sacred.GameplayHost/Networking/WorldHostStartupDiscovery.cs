using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using Sacred.Network.Discovery;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>The native client seeds selected session flags from LAN discovery
/// before later 0x01E updates. Empty-server admission need not send that update.</summary>
internal static class WorldHostStartupDiscovery
{
    public static async Task SeedAsync(string installation, IPEndPoint remote, ushort version,
        GoldSessionState session, Action<string> log, CancellationToken cancellationToken)
    {
        if (session.GameFlags is not null) return;
        var port = 2005;
        var configuration = Path.Combine(installation, "Gameserver.cfg");
        if (File.Exists(configuration))
            foreach (var line in File.ReadLines(configuration))
            {
                var at = line.IndexOf(':');
                if (at < 0 || !line[..at].Trim().Equals("NETWORK_PORT_LISTEN", StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(line[(at + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured is >= 1 and <= 65535)
                    port = configured;
            }
        using var listener = new GoldLanDiscoveryListener(port);
        var local = NetworkInterface.GetAllNetworkInterfaces().SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address).ToHashSet();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            listener.Update();
            var announced = GoldLanSessionMatch.Find(remote, listener.Servers.Where(server => server.Version == version), local);
            if (announced is not null)
            {
                session.SeedAdvertisedGameFlags(announced.Flags);
                log($"Host startup session initialized from LAN announcement: {announced.Endpoint}, flags 0x{announced.Flags:X8}, difficulty {session.Difficulty}, advertised occupancy {announced.Players}/{announced.MaximumPlayers}.");
                return;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidDataException($"Host startup needs matching native session flags from LAN UDP {port}; no matching announcement for {remote} arrived.");
    }
}
