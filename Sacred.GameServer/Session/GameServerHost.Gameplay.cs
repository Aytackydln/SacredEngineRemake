using System.Buffers.Binary;
using Sacred.GameServer.Web;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Session/gameplay messages of the original dispatcher <c>FUN_004c3f20</c> (GameServer.exe). Case addresses are given per handler.
/// </summary>
public sealed partial class GameServerHost
{
    // Service codes sent with opcode 0x009 (GoldLobbyControlCodec.ErrorOpcode).
    private const uint NotAllowed = 0x64, KickDone = 0x65, HostLeft = 0x68;
    // Kick/leave reasons (opcode 0x157 / 0x150 payload).
    private const uint ReasonLeft = 0, ReasonSessionEnded = 0x10;
    private const uint FlagInitialMask = 0x1708D, FlagWritableMask = 0xFFFE8F72;

    private uint _gameFlags;
    private bool _locked, _paused, _hostWorldAvailable;
    private uint _sectorCount;
    private int _maxPlayers;
    private readonly byte[]?[] _worldBanks = new byte[4][];   // 0x1B1..0x1B4, last upload of the host
    private readonly int[] _worldBankLength = new int[4];

    public uint GameFlags => _gameFlags;
    public bool IsLocked => _locked;
    public bool HostWorldAvailable => _hostWorldAvailable;
    public string SessionName => _config.ServerName;

    private void InitializeSession()
    {
        _gameFlags = _config.GameFlags();
        _maxPlayers = _config.MaxPlayers;
    }

    private PlayerSession? HostSession()
    {
        for (var slot = 1; slot <= MaxSlots; slot++) if (_slots[slot] is { IsHost: true } host) return host;
        return null;
    }

    /// <summary>Gameplay routing (after admission and password).</summary>
    private void Route(PlayerSession s, in GoldMessageView m)
    {
        if (_config.Auth.Enabled) { if (!AuthoritativeFilter(s, m)) return; }
        else if (TrackHeroes) CaptureHeroState(s, m);
        if (_scripts is not null && ScriptMessage(s, m)) return;
        if (m.Opcode > OpcodeTable.NativeLast && _extensions.TryGetValue(m.Opcode, out var extension)) { extension(s, m); return; }
        if (WorldMessage(s, m)) return;
        var mode = Routing.Mode(m.Opcode);
        if (mode != RouteMode.None) { Forward(s, m, mode, Routing.IncludeSender(m.Opcode)); return; }
        if (Routing.IsConsumed(m.Opcode)) return;
        if (Routing.IsPending(m.Opcode)) return;
        if (Routing.IsInternal(m.Opcode)) { Log.Info($"#{s.Slot} sent internal server command 0x{m.Opcode:X3}, ignored"); return; }
        switch (m.Opcode)
        {
            case 0x003: PrivateChat(s, m); return;                  // 0x4CA8AA
            case 0x005: PublicChat(s, m); return;                   // 0x4CA771
            case 0x009:                                             // 0x4CBD9D: value to everybody
                if (m.Payload.Length >= 4) SendServiceCodeToAll(BinaryPrimitives.ReadUInt32LittleEndian(m.Payload), s.Slot);
                return;
            case 0x00A:                                             // 0x4CBDAF: value back to the sender
                if (m.Payload.Length >= 4) SendServiceCode(s, BinaryPrimitives.ReadUInt32LittleEndian(m.Payload));
                return;
            case 0x00B: return;                                     // keep-alive pulse (no-op case group 0x4CC5BD)
            case 0x00F: OnPeerReady(s); return;                     // 0x4CA1E1
            case 0x01B: OnPlayerData(s, m); return;                 // 0x4CAC65 -> FUN_004d2ee0
            case 0x021: OnConnectionSpeed(s, m); return;            // 0x4CAE40
            case 0x148: OnSectorEntry(s, m); return;                // 0x4C7202 -> FUN_004cf0d0
            case 0x153: OnReferenceRequest(s, m); return;           // 0x4C48B5 -> FUN_004d6110
            case 0x154: OnTriggerRequest(s, m); return;             // 0x4C4B10
            case 0x14C: OnSectorMessage(s, m); return;              // 0x4C70D7
            case 0x022: OnSessionName(s, m); return;                // 0x4CAF67
            case 0x015: case 0x016: return;                         // upload begin (0x4C9ABD): state only, data follows
            case 0x017: case 0x018: WorldStart(s, m); return;       // 0x4C9F72
            case 0x019: case 0x01A: SetLock(s, m); return;          // 0x4CA469 / 0x4CA3D9
            case 0x01E: SetGameFlags(s, m); return;                 // 0x4CAC9F
            case 0x023: SetMaxPlayers(s, m); return;                // 0x4CAE76
            case 0x041: SendProbe(s); return;                       // 0x4CC447
            case 0x042: return;                                     // 0x4CC4A0 (probe acknowledgement flag)
            case 0x043: AnswerProbe(s, m); return;                  // 0x4CC4B9
            case 0x044: case 0x045: ProbeResult(s, m); return;      // 0x4CC4FD / 0x4CC522
            case 0x134: case 0x135: Pause(s, m); return;            // 0x4C4830 / 0x4C47AB
            case 0x004: OnGroupMessage(s, m); return;               // 0x4CA9A5
            case 0x139: OnStatisticsRequest(s, m); return;          // 0x4C6C60
            case 0x180: OnDirectedRelay(s, m); return;              // 0x4C69C0
            case 0x198: OnTrade(s, m); return;                      // 0x4C850E: trade state machine (GameServerHost.Trade.cs)
            case 0x01D: OnGameStart(s); return;                     // 0x4CAD99
            case 0x17D: ForwardKill(s, m); return;                  // 0x4C647E: tails 0x4C97C4 (mode 6) / 0x4C81D1 (mode 8)
            case 0x156: case 0x157: Kick(s, m); return;             // 0x4C4A5B
            case 0x150: case 0x01C: OnQuit(s, m); return;           // 0x4CA4F9 -> FUN_004d6ac0 (graceful quit / leave)
            case 0x1B1: case 0x1B2: case 0x1B3: case 0x1B4: StoreWorldBank(s, m); return; // 0x4C4CBB..0x4C4CF4
            case 0x1B5: SendWorldBanks(s); return;                  // 0x4C4C86
        }
        if (!OpcodeTable.IsNative(m.Opcode) && m.Opcode < _seenUnhandled.Length && !_seenUnhandled[m.Opcode])
        {
            _seenUnhandled[m.Opcode] = true;
            Log.Info($"unhandled opcode 0x{m.Opcode:X3} ({m.Payload.Length} bytes) from {s.Describe()}");
        }
    }

    /// <summary>
    /// FUN_004d3ea0. Without a server-side object world the sector/object/party filters (modes 3..8) cannot be evaluated, so they
    /// widen to "in game": recipients may receive updates they would not have received from the original.
    /// </summary>
    private void Forward(PlayerSession s, in GoldMessageView m, RouteMode mode, bool includeSender)
    {
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if (_slots[slot] is not { } target || target.State < SessionState.SlotAssigned) continue;
            if (target == s && !includeSender) continue;
            if (mode != RouteMode.Connected && !target.InGame) continue;
            SendMessage(target, m.Opcode, 0, m.Origin == 0 ? s.Slot : m.Origin, m.Payload);
        }
    }

    // ---- chat -------------------------------------------------------------------------------------------------------

    private const int ChatTextBytes = 0xFE, ChatPayloadBytes = 0x104;

    /// <summary>Client 0x005 = [u32][UTF-16 text]; the server sends 0x005 with a 0x104-byte payload ([u32 0][text, max 0xFE bytes]) to all others.</summary>
    private void PublicChat(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 4) return;
        Span<byte> payload = stackalloc byte[ChatPayloadBytes];
        BuildChat(payload, 0, m.Payload[4..]);
        RecordChat(ChatKind.Public, s, -1, m.Payload[4..]);
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } target && target != s && target.State >= SessionState.SlotAssigned)
                SendMessage(target, 0x005, 0, s.Slot, payload);
    }

    /// <summary>Client 0x003 = [u32 target slot][text]; server sends 0x003 [u32 target][text] to that slot.</summary>
    private void PrivateChat(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 4) return;
        var target = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
        if (target is 0 or > MaxSlots || _slots[target] is not { } recipient || recipient.State < SessionState.SlotAssigned) return;
        Span<byte> payload = stackalloc byte[ChatPayloadBytes];
        BuildChat(payload, target, m.Payload[4..]);
        RecordChat(ChatKind.Private, s, (int)target, m.Payload[4..]);
        SendMessage(recipient, 0x003, 0, s.Slot, payload);
    }

    private static void BuildChat(Span<byte> payload, uint word, ReadOnlySpan<byte> text)
    {
        payload.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(payload, word);
        var length = 0;
        while (length + 1 < text.Length && length < ChatTextBytes && (text[length] | text[length + 1]) != 0) length += 2;
        text[..length].CopyTo(payload[4..]);
    }

    // ---- service codes (0x009) --------------------------------------------------------------------------------------

    /// <summary>
    /// 0x022 (case 0x4CAF67): the host renames the session (UTF-16) while in the lobby screen. Refused with service code 0x64
    /// (0x4C4A6D) when the sender is not the host, the name is locked (flag 0x08) or the host is already in the game; in an
    /// Internet game the original refuses it as well (run 10: the Open Net host's 0x022 got 0x009 0x64 before the 0x023
    /// echo), so the lobby's game name stays.
    /// </summary>
    private void OnSessionName(PlayerSession s, in GoldMessageView m)
    {
        if (!s.IsHost || (_gameFlags & 0x08) != 0 || s.InGame || !s.PasswordAccepted || Lobby is not null)
        {
            SendServiceCode(s, 0x64);
            return;
        }
        var payload = m.Payload;
        if ((payload.Length & ~1) == 0) return;
        var text = System.Text.Encoding.Unicode.GetString(payload[..(payload.Length & ~1)]);
        var end = text.IndexOf('\0');
        _config.ServerName = end >= 0 ? text[..end] : text;
        Log.Info($"Session name is now '{_config.ServerName}'.");   // roster record 0 and the LAN/lobby record pick it up
    }

    private void SendServiceCode(PlayerSession s, uint code)
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, code);
        SendMessage(s, 0x009, 0, 0, payload);
    }

    private void SendServiceCodeToAll(uint code, byte origin, PlayerSession? except = null)
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, code);
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } target && target != except && target.State >= SessionState.SlotAssigned)
                SendMessage(target, 0x009, 0, origin, payload);
    }

    private void SendAll(ushort opcode, byte origin, ReadOnlySpan<byte> payload, PlayerSession? except = null)
    {
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } target && target != except && target.State >= SessionState.SlotAssigned)
                SendMessage(target, opcode, 0, origin, payload);
    }

    // ---- world start ------------------------------------------------------------------------------------------------

    /// <summary>
    /// 0x18 (host finished its upload, payload u32 sector table size): mark in game, remember the sector count, set "host world
    /// available" (server flag 0x80 -> admission flag 4) and broadcast an empty 0x18 with the host as origin to everybody.
    /// 0x17 (participant finished): mark in game and approve with an empty 0x17 to the participant. The original first streams
    /// its server-side objects to the participant (FUN_004cd040); that object world is not implemented yet.
    /// </summary>
    private void WorldStart(PlayerSession s, in GoldMessageView m)
    {
        s.InGame = true;
        if (m.Opcode == 0x018)
        {
            if (!s.IsHost) { SendServiceCode(s, NotAllowed); return; }
            _sectorCount = m.Payload.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) : 0;
            _hostWorldAvailable = true;
            SendAll(0x018, s.Slot, default);
            for (var slot = 1; slot <= MaxSlots; slot++)             // 0x4CA0A4: 1000 refs to every other connected player
                if (_slots[slot] is { IsHost: false } other && other.State >= SessionState.SlotAssigned) GrantReferences(other, JoinerReferenceGrant);
            Log.Info($"Host: The world has {_sectorCount} sectors. #{s.Slot} is in the game, joiners may follow.");
            if (_scripts is not null) ScriptPlayerEvent("enter", s);
            return;
        }
        StreamWorldTo(s);
        SendMessage(s, 0x017, 0, s.Slot, default);
        Log.Info($"#{s.Slot} {s.Name} entered the world ({_world.Count} objects)");
        if (_scripts is not null) ScriptPlayerEvent("enter", s);
    }

    private void StoreWorldBank(PlayerSession s, in GoldMessageView m)
    {
        if (!s.IsHost) return;
        var index = m.Opcode - 0x1B1;
        var bank = _worldBanks[index];
        if (bank is null || bank.Length < m.Payload.Length) _worldBanks[index] = bank = new byte[Math.Max(m.Payload.Length, 4096)];
        m.Payload.CopyTo(bank);
        _worldBankLength[index] = m.Payload.Length;
    }

    /// <summary>0x1B5: the original serializes its four current banks in the order 0x1B2, 0x1B4, 0x1B3, 0x1B1. We replay the host's last upload.</summary>
    private void SendWorldBanks(PlayerSession s)
    {
        ReadOnlySpan<ushort> order = [0x1B2, 0x1B4, 0x1B3, 0x1B1];
        foreach (var opcode in order)
            if (_worldBanks[opcode - 0x1B1] is { } bank)
                SendMessage(s, opcode, 0, 0, bank.AsSpan(0, _worldBankLength[opcode - 0x1B1]));
    }

    // ---- host commands ----------------------------------------------------------------------------------------------

    private bool RequireHost(PlayerSession s)
    {
        if (s.IsHost) return true;
        SendServiceCode(s, NotAllowed);
        return false;
    }

    private void SetLock(PlayerSession s, in GoldMessageView m)
    {
        if (!RequireHost(s)) return;
        _locked = m.Opcode == 0x01A;
        SendAll(m.Opcode, 0, m.Payload);
        Log.Info($"Session is {(_locked ? "locked" : "unlocked")} (requested by #{s.Slot}).");
    }

    private void SetGameFlags(PlayerSession s, in GoldMessageView m)
    {
        if (!RequireHost(s) || s.InGame || m.Payload.Length < 4) { if (s.IsHost) SendServiceCode(s, NotAllowed); return; }
        _gameFlags = (BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) & FlagWritableMask) | (_gameFlags & FlagInitialMask);
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, _gameFlags);
        SendAll(0x01E, 0, payload);
        Log.Info("Gameflags changed by host.");
    }

    /// <summary>0x01D (case 0x4CAD99): the host starts a campaign game; only before it entered the world and with a campaign flag
    /// (0x40010), else service code 0x64. Sets flag 0x8000 and broadcasts 0x01E [flags] to everybody.</summary>
    private void OnGameStart(PlayerSession s)
    {
        if (!s.IsHost || s.InGame || (_gameFlags & 0x40010) == 0) { SendServiceCode(s, NotAllowed); return; }
        _gameFlags |= 0x8000;
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, _gameFlags);
        SendAll(0x01E, 0, payload);
    }

    /// <summary>
    /// 0x17D [creature ref][killer word][..] (case 0x4C647E): the original applies the kill to its own object world (native event
    /// 0x208, experience and loot messages; not modelled, TODO) and then forwards the message unchanged: mode 8 (sector visible and
    /// in the object's mask) for a creature (reference above 0x10), mode 6 (object visible) when a hero died (reference = slot).
    /// </summary>
    private void ForwardKill(PlayerSession s, in GoldMessageView m)
    {
        var reference = m.Payload.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) : 0;
        Forward(s, m, reference > 0x10 ? RouteMode.SectorAndObject : RouteMode.ObjectVisible, includeSender: false);
    }

    private PlayerSession? ConnectedSlot(uint slot) =>
        slot is >= 1 and <= MaxSlots && _slots[slot] is { } p && p.State >= SessionState.SlotAssigned ? p : null;

    /// <summary>
    /// 0x180 [target slot][...] (case 0x4C69C0): the message goes unchanged to the player in that slot (1..16), origin = the sender.
    /// </summary>
    private void OnDirectedRelay(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 4) return;
        if (ConnectedSlot(BinaryPrimitives.ReadUInt32LittleEndian(m.Payload)) is { } target) SendMessage(target, m.Opcode, 0, s.Slot, m.Payload);
    }

    /// <summary>
    /// 0x004 [slot or lobby id][...] (case 0x4CA9A5): the original resolves the player (a value above 16 is a lobby id, matched against
    /// +0x90), looks up that player's group in its statistics manager and sends the message unchanged to every member except the
    /// sender (origin = sender). TODO: the group membership lives in the statistics manager (not modelled), so only the named player
    /// itself receives the message here.
    /// </summary>
    private void OnGroupMessage(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 4) return;
        var who = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
        PlayerSession? target = null;
        if (who is >= 1 and <= MaxSlots) target = ConnectedSlot(who);
        else if (who > MaxSlots)
            for (var slot = 1; slot <= MaxSlots && target is null; slot++)
                if (_slots[slot] is { } candidate && candidate.LobbyIdentity == (int)who) target = candidate;
        if (target is not null && target != s) SendMessage(target, m.Opcode, 0, s.Slot, m.Payload);
    }

    /// <summary>
    /// 0x139 [player slot] (case 0x4C6C60): the original answers the requester with the request itself (origin = the asked player) and
    /// then with a 0x13A of 0x48 bytes built from that player's statistics manager. TODO (0x4C6CD6): the 0x13A needs the statistics
    /// block of the asked player (see 0x14F); only the first answer is sent here.
    /// </summary>
    private void OnStatisticsRequest(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 4) return;
        var asked = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
        SendMessage(s, m.Opcode, 0, (byte)asked, m.Payload);
    }

    private void SetMaxPlayers(PlayerSession s, in GoldMessageView m)
    {
        if (!RequireHost(s) || s.InGame || m.Payload.Length < 4) { if (s.IsHost) SendServiceCode(s, NotAllowed); return; }
        var value = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(m.Payload), MaxSlots);
        if ((_gameFlags & 0x40010) != 0 && value > 4) value = 4; // campaign games: at most four players
        if (value != 0) _maxPlayers = value;
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)value);
        SendAll(0x023, 0, payload);
        Log.Info($"Max. number of player is set to {value}.");
    }

    private void Pause(PlayerSession s, in GoldMessageView m)
    {
        var pause = m.Opcode == 0x135;
        if (_paused == pause) return;
        _paused = pause;
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } target && target != s && target.State >= SessionState.SlotAssigned)
                SendMessage(target, m.Opcode, 0, s.Slot, m.Payload);
        Log.Info(pause ? $"Session paused (requested by #{s.Slot})." : $"Session continued (requested by #{s.Slot}).");
    }

    /// <summary>0x156/0x157 = [u32 target slot][u32 reason], host only. Replies 0x009 0x65 on success, 0x64 otherwise.</summary>
    private void Kick(PlayerSession s, in GoldMessageView m)
    {
        if (!RequireHost(s) || m.Payload.Length < 8) return;
        var target = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
        var reason = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[4..]);
        if (target is 0 or > MaxSlots || _slots[target] is not { } victim) return;
        SendServiceCode(s, KickDone);
        Log.Info($"#{target} kicked from the game. (Reason {reason})");
        RemovePlayer(victim, reason, notifyVictim: true);
    }

    /// <summary>
    /// FUN_004d6ac0: 0x157 [reason] to the player, 0x150 [reason] (origin = player) to the others; a leaving host hands over to the
    /// first in-game player (FUN_004cdc50: 0x00D to the new host). Without a candidate the session ends: everybody is kicked with
    /// reason 0x10.
    /// </summary>
    private void RemovePlayer(PlayerSession s, uint reason, bool notifyVictim, bool closeConnection = true, bool echoToLeaver = false)
    {
        if (notifyVictim)
        {
            Span<byte> kick = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(kick, reason);
            SendMessage(s, 0x157, 0, 0, kick);
            s.Connection.Flush();
        }
        CancelTradeOf(s);
        if (_scripts is not null) ScriptPlayerEvent("leave", s);
        if (_config.Auth.Enabled) AuthoritativeLeave(s);
        else if (TrackHeroes) ForgetHeroState(s.Slot);
        var slot = s.Slot;
        if (slot != 0 && _slots[slot] == s) RecordChat(ChatKind.System, s, -1, reason == ReasonLeft ? "left" : $"removed (reason {reason})");
        var wasHost = s.IsHost;
        s.IsHost = false;
        if (slot != 0 && _slots[slot] == s)
        {
            _slots[slot] = null; PlayerCount--; LobbyPlayerRemoved(s); _world.Forget(slot);
            ReleaseReferences(slot); LeaveAllSectors(slot);
        }
        Span<byte> departure = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(departure, reason);
        // Mode 1 of the original's broadcast (except = 0x11) reaches the leaver too: its own 0x150 copy, origin = its slot.
        if (echoToLeaver && slot != 0 && !s.Connection.IsClosed) SendMessage(s, 0x150, 0, slot, departure);
        s.State = SessionState.Closed;
        if (slot != 0) SendAll(0x150, slot, departure);
        if (wasHost) TransferHost();
        if (closeConnection) s.Connection.Close();   // a graceful 0x150 leaves the close to the client, like the original
    }

    /// <summary>
    /// Client 0x150 and 0x01C [u32 reason] (case 0x4CA4F9, one handler): the player leaves. The original calls
    /// FUN_004d6ac0(slot, 1, opcode != 0x1C, reason): 0x150 [reason] (origin = player) to everybody connected, no 0x157 to the
    /// leaver and the connection is left to the client to drop (0x150, the ESC menu's "Quit"); 0x1C additionally sends 0x157 to
    /// the leaver, tells the others except the leaver and closes the connection itself. The leave path runs once per player
    /// (flag +0x7D), so the TCP close that follows changes nothing. TODO (address 0x4CA4F9): with reason 7 the original also
    /// resets the leaver's creature object in its own world (FUN_00510f0c/FUN_004eab00), which this server does not model.
    /// </summary>
    private void OnQuit(PlayerSession s, in GoldMessageView m)
    {
        if (s.State == SessionState.Closed || s.Slot == 0 || _slots[s.Slot] != s) return;
        var reason = m.Payload.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) : 0;
        var withKick = m.Opcode == 0x01C;
        Log.Info($"{s.Describe()} left the game (opcode 0x{m.Opcode:X3}, reason {reason}).");
        RemovePlayer(s, reason, notifyVictim: withKick, closeConnection: withKick, echoToLeaver: !withKick);
    }

    private void TransferHost()
    {
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { InGame: true } candidate)
            {
                candidate.IsHost = true;
                SendMessage(candidate, 0x00D, 0, 0, default);
                Log.Info($"host role moved to #{slot}");
                return;
            }
        _locked = false; _hostWorldAvailable = false;
        _world.Clear();
        Array.Clear(_worldBankLength);
        _poolCount = -1; ResetSectors();                              // a new session starts with fresh refs and sectors
        for (var slot = 1; slot <= MaxSlots; slot++)
            if (_slots[slot] is { } other) RemovePlayer(other, ReasonSessionEnded, notifyVictim: true);
        if (PlayerCount == 0) Log.Info("Clientdata reseted.");
    }

    // ---- time probes ------------------------------------------------------------------------------------------------

    private uint ServerMilliseconds => (uint)_clock.ElapsedMilliseconds;

    /// <summary>0x41 -> 0x43 [0][server ms][server ms][last latency].</summary>
    private void SendProbe(PlayerSession s)
    {
        s.ProbePending = true;
        Span<byte> payload = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], ServerMilliseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[8..], ServerMilliseconds);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[12..], s.LatencyMs);
        SendMessage(s, 0x043, 0, 0, payload);
    }

    /// <summary>0x43 from a client -> 0x44 [word0][word1][server ms].</summary>
    private void AnswerProbe(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 8) return;
        Span<byte> payload = stackalloc byte[12];
        m.Payload[..8].CopyTo(payload);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[8..], ServerMilliseconds);
        SendMessage(s, 0x044, 0, 0, payload);
    }

    private void ProbeResult(PlayerSession s, in GoldMessageView m)
    {
        if (m.Opcode == 0x044 && m.Payload.Length >= 8) { s.LatencyMs = ServerMilliseconds - BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[4..]); s.ProbePending = false; }
        else if (m.Opcode == 0x045 && m.Payload.Length >= 4) s.LatencyMs = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
    }
}
