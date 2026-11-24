using Sacred.GameServer.Net;

namespace Sacred.GameServer.Session;

public enum SessionState { Connected, LoggedIn, SlotAssigned, Playing, Closed }

/// <summary>Per client state, owned by the game loop thread.</summary>
public sealed class PlayerSession
{
    public required IClientLink Connection { get; init; }
    public uint TinCatId;
    public byte Slot;
    public SessionState State;
    public int LobbyIdentity;
    public string Name = "";
    public ushort TxSequence;
    public ushort RxSequence;
    public bool PasswordAccepted;
    public int PasswordTries;
    public bool IsHost;          // +0x72
    public bool InGame;          // +0x75, set by 0x17/0x18
    public uint LatencyMs;
    public bool ProbePending;
    public long LastReceiveMs, LastKeepAliveMs;
    public ushort CharacterBlock;    // +0x88, FIRSTCONTACT byte 6 (Closed Net character slot)
    public bool LobbyAnnounced;      // +0x0B, message 45 sent
    /// <summary>This player's 834-byte record of the 0x1B roster (FUN_004d3030), built from FIRSTCONTACT and updated by client 0x1B.</summary>
    public readonly byte[] RosterEntry = new byte[GameServerHost.RosterEntryBytes];
    public bool Ready;               // +0x7A, set by 0x0F
    public ushort CurrentSector;     // sector the player stands in (word +0 of its last 0x148)
    public long CharacterAskDueMs;   // Closed Net: ask the lobby (message 19) at this time unless the block was pushed by then
    public FloodState Flood;         // flood protection buckets (Session/FloodGuard.cs)
    public int Violations;           // authoritative mode: rejected client values
    public long HeroSaveDueMs, NextPeriodicSaveMs;   // authoritative mode: debounced / periodic hero save to the lobby (0 = none)
    public bool CharacterReceived;   // +0x8D, Closed Net character delivered

    public string Describe() => Slot != 0 ? $"#{Slot} {Name}" : $"{Connection.RemoteEndPoint}";
}
