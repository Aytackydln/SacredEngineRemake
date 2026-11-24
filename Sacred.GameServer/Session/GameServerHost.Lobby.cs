using System.Buffers.Binary;
using Sacred.GameServer.Lobby;

namespace Sacred.GameServer.Session;

/// <summary>
/// Lobby integration of the game loop (Open/Closed Internet). GameServer.exe's lobby thread turns lobby messages into internal
/// messages handled by the main dispatcher: 0x25 public IP (then re-announce, <c>FUN_004ed7e0</c>), 0x34 kick (case 0x4CBBC4),
/// 0x3E shutdown (0x4CBD00, <c>FUN_004d6c50</c>) and 0x29 Closed Net character (0x4CB287, sent to the player as 0x132).
/// </summary>
public sealed partial class GameServerHost
{
    private const uint ReasonLobbyKick = 8;
    private LobbyProtocol.GameRecord _lobbyGame;

    /// <summary>Optional lobby registration; set before <see cref="Run"/>.</summary>
    public LobbyClient? Lobby { get; set; }

    /// <summary>Duplicate lobby identities are only refused while the lobby is connected (FUN_004eb0a0 checks FUN_0048d100).</summary>
    private bool LobbyIdentityCheck => Lobby?.IsOnline == true;

    /// <summary>The record the lobby lists; <paramref name="clientVersion"/> is the build value FIRSTCONTACT is checked against.</summary>
    public LobbyProtocol.GameRecord LobbyRecord(int clientVersion, uint publicAddress) => new(
        _config.ServerName, 0, publicAddress, TcpPort, PlayerCount, _maxPlayers, LobbyFlags, clientVersion, _config.LobbyChannel);

    /// <summary>The server flag word (0x5848) as the lobby sees it: game flags plus 0x80 once the host world is up.</summary>
    private uint LobbyFlags => _gameFlags | (_hostWorldAvailable ? 0x80u : 0u);

    private void PumpLobby()
    {
        if (Lobby is not { } lobby) return;
        AskForCharacters(_clock.ElapsedMilliseconds);
        while (lobby.Events.TryDequeue(out var e))
        {
            switch (e.Kind)
            {
                case LobbyEventKind.Online:
                    Log.Info("LOBBY: game registered.");
                    for (var slot = 1; slot <= MaxSlots; slot++)
                        if (_slots[slot] is { } s) AnnounceToLobby(s);
                    break;
                case LobbyEventKind.Offline:
                    Log.Info("LOBBY: connection lost, reconnecting.");
                    for (var slot = 1; slot <= MaxSlots; slot++)
                        if (_slots[slot] is { } s) s.LobbyAnnounced = false;
                    break;
                case LobbyEventKind.Kick:
                    var victim = SlotByIdentity(e.PermId);
                    if (victim is null) { Log.Info($"Lobby: player #{e.PermId} is not valid!"); break; }
                    Log.Info($"Lobby: kicking #{victim.Slot} {victim.Name}.");
                    RemovePlayer(victim, ReasonLobbyKick, notifyVictim: true);
                    break;
                case LobbyEventKind.Shutdown:
                    Log.Info($"Lobby: game will be shut down! {e.Text}");
                    ShutDownSession();
                    break;
                case LobbyEventKind.Text:
                    Log.Info($"LOBBY: {e.Text}");
                    break;
                case LobbyEventKind.Character:
                    DeliverCharacter(e.PermId, e.Block, e.Data!);
                    break;
            }
        }
        var record = _lobbyGame with { Name = _config.ServerName, Players = PlayerCount, MaxPlayers = _maxPlayers, Flags = LobbyFlags };
        if (record != _lobbyGame) { _lobbyGame = record; lobby.UpdateGame(record); }
    }

    /// <summary>Seeds the change detector with the record the lobby client logs in with.</summary>
    public void AttachLobby(LobbyClient lobby, in LobbyProtocol.GameRecord initial) { Lobby = lobby; _lobbyGame = initial; }

    private void AnnounceToLobby(PlayerSession s)
    {
        // FUN_004ed690: only players with a lobby identity; the first message says "announce", later ones "re-announce".
        if (Lobby is not { IsOnline: true } lobby || s.LobbyIdentity is 0 or -1) return;
        lobby.PlayerJoined(s.LobbyIdentity, s.CharacterBlock, first: !s.LobbyAnnounced);
        s.LobbyAnnounced = true;
    }

    private void LobbyPlayerRemoved(PlayerSession s)
    {
        // FUN_004ea3e0: message 46 for an announced player.
        if (s.LobbyAnnounced && Lobby is { IsOnline: true } lobby) lobby.PlayerLeft(s.LobbyIdentity);
        s.LobbyAnnounced = false;
    }

    private PlayerSession? SlotByIdentity(int permId)
    {
        for (var slot = 1; slot <= MaxSlots; slot++) if (_slots[slot] is { } s && s.LobbyIdentity == permId) return s;
        return null;
    }

    /// <summary>
    /// Closed Net (any lobby channel other than 5, GameServer.exe's Open Net channel, or <c>--closed-net</c> for a lobby whose
    /// channel 5 is closed too): the character lives at the lobby, so once
    /// the player is announced (message 45) its block is requested with message 19; the answer (PublicData 11) arrives as internal
    /// 0x29 and goes to the player as 0x132. Open Net players keep their local hero.
    /// </summary>
    private void RequestCharacter(PlayerSession s)
    {
        if (Lobby is not { IsOnline: true } lobby || (!_config.ClosedNet && _config.LobbyChannel == LobbyProtocol.OpenNetChannel)) return;
        if (s.LobbyIdentity is 0 or -1 || s.CharacterBlock is < 1 or > 8) return;
        // sacred-lobby pushes the block right after message 45 (as the stock GameServer.exe expects); lobbies that do not get
        // asked with message 19 when nothing arrived within CharacterAskDelayMs.
        s.CharacterAskDueMs = _clock.ElapsedMilliseconds + CharacterAskDelayMs;
    }

    private const int CharacterAskDelayMs = 2000;

    private void AskForCharacters(long now)
    {
        if (Lobby is not { IsOnline: true } lobby) return;
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if (_slots[slot] is not { CharacterAskDueMs: > 0 } s || now < s.CharacterAskDueMs) continue;
            s.CharacterAskDueMs = 0;
            if (s.CharacterReceived) continue;
            lobby.RequestCharacter(s.LobbyIdentity, s.CharacterBlock);
            Log.Info($"Requesting the character (permID={s.LobbyIdentity}, block={s.CharacterBlock}) of {s.Describe()} from the lobby.");
        }
    }

    /// <summary>Internal 0x29 (case 0x4CB287): a Closed Net character block for an admitted player is passed on as 0x132.</summary>
    private void DeliverCharacter(int permId, ushort block, byte[] data)
    {
        // The original ignores the profile block 10, block-0 control answers (4 or 0x20 bytes) and anything up to the
        // 0x230-byte preview + sizes; the player must match both perm id and selected block.
        if (block == 10 || data.Length <= 0x230) return;
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if (_slots[slot] is not { } s || s.LobbyIdentity != permId || s.CharacterBlock != block) continue;
            // Delivered once per admission: the lobby pushes the block unasked (as the stock GameServer.exe expects) and may answer our
            // own request as well.
            if (s.CharacterReceived) return;
            s.CharacterReceived = true; s.CharacterAskDueMs = 0;
            if (_config.Auth.Enabled) RememberBaseline(s, data);
            SendMessage(s, 0x132, 0, 0, data);
            Log.Info($"Character savegame received from lobby (permID={permId}, block={block}, size={data.Length}), sent to player #{slot}.");
            return;
        }
    }

    /// <summary>FUN_004d6c50: 0x009 0x68 to all, opcode 0x008 to every connected player, then everybody is removed (reason 8).</summary>
    private void ShutDownSession()
    {
        SendServiceCodeToAll(HostLeft, 0);
        for (var slot = 1; slot <= MaxSlots; slot++) if (_slots[slot] is { } s) SendMessage(s, 0x008, 0, 0, default);
        for (var slot = MaxSlots; slot >= 1; slot--) if (_slots[slot] is { } s) RemovePlayer(s, ReasonLobbyKick, notifyVictim: true);
    }
}
