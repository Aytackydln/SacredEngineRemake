using System.Buffers.Binary;
using Sacred.GameServer.Config;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Platform;
using Sacred.GameServer.Session;

namespace Sacred.GameServer.Tests;

/// <summary>The 0x410 status messages GameServer.exe posts to the game's window (Platform/GameStatus.cs), through a recording notifier.</summary>
[TestClass]
public class StatusTests
{
    private sealed class Recorder : IStatusNotifier
    {
        public readonly List<(int Code, int Value)> Calls = new();
        public void Post(int code, int value) { lock (Calls) Calls.Add((code, value)); }
        public (int Code, int Value)[] Snapshot() { lock (Calls) return [.. Calls]; }
    }

    private static (GameServerHost, Lobby.LobbyProtocol.GameRecord) HostAndRecord()
    {
        var host = new GameServerHost(new GameServerConfig { TcpPort = 0, LanAnnounce = false, TickMilliseconds = 5 }, new AdmissionRules(null), null);
        return (host, host.LobbyRecord(0x1234, 0));
    }

    [TestMethod]
    public void PostsGoToTheNotifierAndAreNoOpsWithoutAWindow()
    {
        var recorder = new Recorder();
        var previous = GameStatus.Notifier;
        try
        {
            GameStatus.Notifier = recorder;
            GameStatus.Post(GameStatus.Started);
            GameStatus.Post(GameStatus.TestingPort, 1);
            GameStatus.Post(GameStatus.UpAndRunning);
            CollectionAssert.AreEqual(new[] { (1, 0), (2, 1), (4, 0) }, recorder.Snapshot());
            Assert.AreEqual(0x410u, GameStatus.WindowMessage);
        }
        finally { GameStatus.Notifier = previous; }
        GameStatus.ForWindow(0).Post(1, 0);                       // no window: nothing happens, nothing is thrown
        GameStatus.Notifier = null!;                              // null falls back to the no-op notifier
        GameStatus.Post(GameStatus.GracefulExit);
        GameStatus.Notifier = previous;
    }

    [TestMethod]
    public void LobbyLoginReportsUpAndRunning()
    {
        using var fake = new LobbyTests.FakeLobby();
        var (host, record) = HostAndRecord();
        var recorder = new Recorder();
        using var lobby = new LobbyClient("127.0.0.1", fake.Port, record) { Status = recorder.Post };
        lobby.Start();
        fake.WaitFor(LobbyProtocol.ServerLogin);
        var deadline = Environment.TickCount64 + 5000;
        while (!lobby.IsOnline && Environment.TickCount64 < deadline) Thread.Sleep(10);
        Assert.IsTrue(lobby.IsOnline);
        CollectionAssert.AreEqual(new[] { (4, 0) }, recorder.Snapshot());
        host.Dispose();
    }

    [TestMethod]
    public void UnreachableLobbyIsReportedOnceAndARefusedLoginWithItsError()
    {
        var (host, record) = HostAndRecord();
        var closed = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        closed.Start(); var port = ((System.Net.IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
        var recorder = new Recorder();
        using (var lobby = new LobbyClient("127.0.0.1", port, record) { Status = recorder.Post })
        {
            lobby.Start();
            var deadline = Environment.TickCount64 + 6000;
            while (recorder.Snapshot().Length == 0 && Environment.TickCount64 < deadline) Thread.Sleep(20);
            Thread.Sleep(300);
            CollectionAssert.AreEqual(new[] { (8, 0) }, recorder.Snapshot());       // once, although the client keeps retrying
        }

        using var fake = new LobbyTests.FakeLobby();
        var refused = new Recorder();
        using var second = new LobbyClient("127.0.0.1", fake.Port, record) { Status = refused.Post };
        second.Start();
        fake.WaitFor(LobbyProtocol.ServerLogin);
        var result = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(result, 7);                          // result 7 ...
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), LobbyProtocol.ServerLogin);   // ... to the login
        fake.SendLobby(LobbyProtocol.Result, result);
        var until = Environment.TickCount64 + 5000;
        while (refused.Snapshot().Length < 2 && Environment.TickCount64 < until) Thread.Sleep(20);
        CollectionAssert.AreEqual(new[] { (4, 0), (9, 7) }, refused.Snapshot());      // the fake lobby accepted the login first
        host.Dispose();
    }
}
