using System.Collections.Generic;
using System.Numerics;
using Sacred.Network.Discovery;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Game-thread diagnostics and deferred requests from the multiplayer debug window.</summary>
internal sealed class GameServerDebugPanelState
{
    public bool Visible { get; set; }
    public string Endpoint = "127.0.0.1:2006";
    public int DiscoveryPort = 2005;
    public bool RefreshDiscoveryRequested { get; set; }
    public string DiscoveryStatus { get; set; } = "Open this panel to discover LAN games.";
    public IReadOnlyList<GoldLanAnnouncement> Servers { get; set; } = [];
    public int Selection { get; set; }
    public string GameDirectory { get; set; } = string.Empty;
    public IReadOnlyList<GameServerSavedHeroChoice> Heroes { get; set; } = [];
    public string HeroStatus { get; set; } = "Saved characters have not been loaded.";
    public bool RefreshHeroesRequested { get; set; } = true;
    public bool JoinRequested { get; set; }
    public bool DisconnectRequested { get; set; }
    public bool Connected { get; set; }
    public bool Joining { get; set; }
    public bool WorldReady { get; set; }
    public byte PlayerSlot { get; set; }
    public int EventsReceived { get; set; }
    public string JoinStatus { get; set; } = "Disconnected. Select a saved hero and join the running game.";
    public string ConnectionStatus { get; set; } = "offline";
    public string WorldStatus { get; set; } = "unbound";
    public string ActionStatus { get; set; } = string.Empty;
    public GoldWorldReplica? Replicas { get; set; }
    public IReadOnlyDictionary<ushort, int> OpcodeCounts { get; set; } = new Dictionary<ushort, int>();
    public Queue<string> DiagnosticsRequested { get; } = new();
    public Vector2 WalkTarget;
    public bool NavigateRequested { get; set; }
    public bool StopNavigationRequested { get; set; }
    public string MovementStatus { get; set; } = "Movement waits for the local actor.";
    public string WalkControl = string.Empty;
    public float WalkSpeed = 2;
    public bool Patrol;
    public bool WalkRequested { get; set; }
    public bool StopWalkRequested { get; set; }
    public bool Walking { get; set; }
    public string WalkStatus { get; set; } = "No walk requested.";
}

internal sealed record GameServerSavedHeroChoice(int Selection, string Label, string? Error = null);
