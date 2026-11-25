using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Session;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Tests;

/// <summary>Stopping the server is fast: no waiting for the web UI, the lobby or its retries; only players get a short bounded wait.</summary>
[TestClass]
public class ShutdownTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [TestMethod]
    public async Task StopWithLobbyRetriesAndAnOpenWebStreamReturnsQuickly()
    {
        var config = new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 };
        var host = new GameServerHost(config, new AdmissionRules(null), null);
        var lobby = new LobbyClient("127.0.0.1", FreePort(), host.LobbyRecord(0x1234, 0));   // nobody listens: refused, then back-off
        host.AttachLobby(lobby, host.LobbyRecord(0x1234, 0));
        host.StopToken.Register(lobby.RequestStop);
        lobby.Start();
        var web = new WebUiServer(new WebUiOptions { Hostname = "127.0.0.1", Port = 0 }, host, _ => { });
        await web.StartAsync();
        var runner = new Thread(host.Run) { IsBackground = true };
        runner.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{web.Ports[0]}"), Timeout = TimeSpan.FromSeconds(30) };
        using var stream = await client.GetAsync("/api/chat/stream", HttpCompletionOption.ResponseHeadersRead);   // a browser's open EventSource
        Assert.IsTrue(stream.IsSuccessStatusCode);
        await Task.Delay(2500);                                   // the lobby is in its first back-off by now

        var watch = Stopwatch.StartNew();
        host.Stop();
        Assert.IsTrue(runner.Join(2000), "Run() did not return");
        lobby.Dispose();
        await web.DisposeAsync();
        host.Dispose();
        watch.Stop();
        Assert.IsTrue(watch.ElapsedMilliseconds < 2000, $"shutdown took {watch.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public void ConnectedPlayersGetTheEndOfSessionMessagesBeforeTheyAreClosed()
    {
        var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
        var runner = new Thread(host.Run) { IsBackground = true };
        runner.Start();
        using var player = RawClient.Admit(host.TcpPort, "Hero", 1);
        var watch = Stopwatch.StartNew();
        host.Stop();
        Assert.IsTrue(runner.Join(3000));
        Assert.AreEqual(0x68u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(player.WaitFor(0x009).Payload));
        player.WaitFor(0x008);
        Assert.AreEqual(8u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(player.WaitFor(0x157).Payload));
        Assert.IsTrue(watch.ElapsedMilliseconds < 2500, $"took {watch.ElapsedMilliseconds} ms");
        host.Dispose();
    }

    [TestMethod]
    public void StopWithoutPlayersIsImmediate()
    {
        var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
        var runner = new Thread(host.Run) { IsBackground = true };
        runner.Start();
        Thread.Sleep(100);
        var watch = Stopwatch.StartNew();
        host.Stop();
        Assert.IsTrue(runner.Join(1000));
        host.Dispose();
        Assert.IsTrue(watch.ElapsedMilliseconds < 500, $"took {watch.ElapsedMilliseconds} ms");
    }
}
