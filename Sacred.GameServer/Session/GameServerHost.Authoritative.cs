using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// Optional authoritative mode (<c>--authoritative</c>): the server validates what clients report instead of relaying it blindly.
/// Everything here is reached only when <see cref="Auth.AuthoritativeOptions.Enabled"/> is set, so normal relay games stay
/// byte-for-byte like the original. Stage 1:
/// <list type="bullet">
/// <item>game difficulty (flag bits 0x100..0x800) stays what the server was configured with, the host's 0x01E cannot change it;</item>
/// <item>0x17A damage must come from the sender's own hero or an object it owns and is clamped to <c>AUTH_MAX_DAMAGE</c>;</item>
/// <item>0x198 trade messages must carry the sender's own slot (the original ignores a mismatch, we count it as a violation);</item>
/// <item>violations are counted per player and kick at <c>AUTH_MAX_VIOLATIONS</c>;</item>
/// <item>anti-dupe: the hero is saved to the lobby when the player leaves, periodically and shortly after trades, pick-ups and
/// equipment and inventory changes, from the server's own view of the hero (GameServerHost.HeroState.cs, Auth/Hero/HeroWriter)
/// or an <see cref="IHeroSerializer"/> that replaces it;</item>
/// <item>item states no legitimate item has are refused (<see cref="ItemGuard"/>), a hero above the level cap is removed
/// (<see cref="HeroGuard"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServerHost
{
    private const uint DifficultyBits = 0xF00, ReasonCheat = 8;
    private byte[]? _heroBuffer;

    /// <summary>Replaces the built-in hero serializer (baseline block from the lobby + the server's live state, HeroWriter).</summary>
    public IHeroSerializer? HeroSerializer { get; set; }

    /// <summary>Number of hero blocks uploaded to the lobby (diagnostics and tests).</summary>
    public int HeroSaves { get; private set; }

    /// <summary>A player leaves: last save, then its hero state is dropped.</summary>
    private void AuthoritativeLeave(PlayerSession s)
    {
        SaveHero(s, "leave");
        ForgetHeroState(s.Slot);
    }

    /// <summary>Returns true when the message may continue through the normal routing.</summary>
    private bool AuthoritativeFilter(PlayerSession s, in GoldMessageView m)
    {
        var options = _config.Auth;
        if (m.Opcode is 0x169 or 0x16B or 0x16C or 0x16E or 0x16F && !PlausibleState(s, m)) return false;
        CaptureHeroState(s, m);
        switch (m.Opcode)
        {
            case 0x17A:
            {
                Span<byte> clamped = stackalloc byte[DamageGuard.PayloadBytes];
                var attacker = m.Payload.Length == DamageGuard.PayloadBytes
                    ? BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[DamageGuard.AttackerOffset..]) & 0x7FFFFFFF : 0;
                var owner = attacker != 0 && _world.TryGet(attacker, out var entry) ? entry.Owner : -1;
                switch (DamageGuard.Check(m.Payload, s.Slot, owner, options, clamped))
                {
                    case GuardVerdict.Reject: Violation(s, $"damage 0x17A from a foreign attacker 0x{attacker:X}"); return false;
                    case GuardVerdict.Clamped:
                        Violation(s, "damage above the limit", kick: false);
                        Forward(s, new GoldMessageView(m.Opcode, m.Sender, m.Origin, m.Sequence, clamped, m.Raw), RouteMode.ObjectVisible, false);
                        return false;
                }
                return true;
            }
            case 0x198:
                // [partner slot][sub command]: the first word names the partner, not the sender (case 0x4C850E)
                MarkHeroChanged(s);
                return true;
            case 0x141: case 0x142: case 0x11D: case 0x11E: case 0x13E: case 0x13F: case 0x122:   // pick-up, equipment, inventory
                MarkHeroChanged(s);
                return true;
            case QuestRecords.Opcode:                                                                // quest pair (GameServerHost.Quests.cs)
                CaptureQuestRecord(s, m);
                return true;
            case 0x01E when options.LockDifficulty && s.IsHost && m.Payload.Length >= 4:
            {
                Span<byte> payload = stackalloc byte[4];
                var word = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
                BinaryPrimitives.WriteUInt32LittleEndian(payload, (word & ~DifficultyBits) | (_gameFlags & DifficultyBits));
                SetGameFlags(s, new GoldMessageView(m.Opcode, m.Sender, m.Origin, m.Sequence, payload, m.Raw));
                return false;
            }
        }
        return true;
    }

    /// <summary>Item and hero plausibility (Auth/ItemGuard.cs, Auth/HeroGuard.cs). An impossible item state is dropped (not stored
    /// in the object world, not relayed, so never saved) and counted as a violation; an impossible hero removes the player.</summary>
    private bool PlausibleState(PlayerSession s, in GoldMessageView m)
    {
        var prefix = m.Opcode is 0x16C or 0x16E ? 8 : 24;
        if (m.Payload.Length <= prefix) return true;
        var reference = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload) & 0x7FFFFFFF;
        var body = m.Payload[prefix..];
        if (reference == s.Slot)
        {
            if (HeroGuard.Check(body, m.Opcode == 0x16B, _config.Auth) is not { } hero) return true;
            Violation(s, hero, kick: false);
            if (s.State != SessionState.Closed) RemovePlayer(s, ReasonCheat, notifyVictim: true);
            return false;
        }
        if (m.Opcode is 0x16F or 0x16E && StrictRefuses(s, reference, body)) return false;
        if (!_config.Auth.CheckItems || ItemGuard.Check(body, ItemFootprints) is not { } why) return true;
        RejectedItems++;
        Violation(s, $"0x{m.Opcode:X3} 0x{reference:X}: {why}");
        return false;
    }

    /// <summary>Item states refused by <see cref="ItemGuard"/> (diagnostics and tests).</summary>
    public int RejectedItems { get; private set; }

    private void Violation(PlayerSession s, string why, bool kick = true)
    {
        s.Violations++;
        Log.Info($"AUTH: {s.Describe()}: {why} (violation {s.Violations})");
        var limit = _config.Auth.MaxViolations;
        if (kick && limit > 0 && s.Violations >= limit && s.State != SessionState.Closed) RemovePlayer(s, ReasonCheat, notifyVictim: true);
    }

    // ---- anti-dupe hero saves ---------------------------------------------------------------------------------------

    private void MarkHeroChanged(PlayerSession s)
    {
        if (s.HeroSaveDueMs == 0) s.HeroSaveDueMs = _clock.ElapsedMilliseconds + _config.Auth.SaveDebounceMilliseconds + 1;
    }

    private void PumpAuthoritative(long now)
    {
        var interval = _config.Auth.SaveIntervalSeconds * 1000L;
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if (_slots[slot] is not { InGame: true } s) continue;
            if (s.HeroSaveDueMs != 0 && now >= s.HeroSaveDueMs) SaveHero(s, "changed");
            else if (interval > 0)
            {
                if (s.NextPeriodicSaveMs == 0) s.NextPeriodicSaveMs = now + interval;
                else if (now >= s.NextPeriodicSaveMs) SaveHero(s, "periodic");
            }
        }
    }

    /// <summary>Uploads the player's hero (message 18) when it has a lobby identity and character block and a serializer is set.</summary>
    private void SaveHero(PlayerSession s, string reason)
    {
        s.HeroSaveDueMs = 0;
        s.NextPeriodicSaveMs = _clock.ElapsedMilliseconds + _config.Auth.SaveIntervalSeconds * 1000L;
        if (Lobby is not { IsOnline: true } lobby) return;
        if (s.LobbyIdentity is 0 or -1 || s.CharacterBlock is < 1 or > 8 || !s.CharacterReceived) return;
        var buffer = _heroBuffer ??= new byte[LobbyProtocol.MaxBlock];
        var length = HeroSerializer is { } serializer ? serializer.Serialize(s, buffer) : SerializeHero(s, buffer);
        if (length <= 0 || !lobby.UploadCharacter(s.LobbyIdentity, s.CharacterBlock, buffer.AsSpan(0, length))) return;
        HeroSaves++;
        Log.Info($"AUTH: hero of {s.Describe()} saved to the lobby ({reason}, {length} bytes).");
    }
}
