using System.Buffers.Binary;

namespace Sacred.GameServer.Auth;

public enum GuardVerdict : byte { Allow, Clamped, Reject }

/// <summary>
/// Plausibility checks for the damage message 0x17A (24 bytes: [victim ref][damage][resource][attacker ref][impact enabled][impact data],
/// layout in Sacred.Network <c>GoldCombatLayout</c>). Pure, allocation free.
/// </summary>
public static class DamageGuard
{
    public const int PayloadBytes = 24, DamageOffset = 4, AttackerOffset = 12;
    private const uint ReferenceMask = 0x7FFFFFFF;

    /// <param name="attackerOwner">Owner slot of the attacker in the server's object world, or -1 when unknown.</param>
    /// <param name="clamped">Payload to forward when the verdict is <see cref="GuardVerdict.Clamped"/> (a copy with the damage limited).</param>
    public static GuardVerdict Check(ReadOnlySpan<byte> payload, byte sender, int attackerOwner, AuthoritativeOptions options, Span<byte> clamped)
    {
        if (payload.Length != PayloadBytes) return GuardVerdict.Reject;
        var attacker = BinaryPrimitives.ReadUInt32LittleEndian(payload[AttackerOffset..]) & ReferenceMask;
        // Heroes carry their slot as object reference; everything else must be owned by the sender (its AI/minions), unless the
        // server cannot tell (not mirrored yet) and strictness is off. Attacker 0 is script/trap damage.
        if (attacker is >= 1 and <= 16) { if (attacker != sender) return GuardVerdict.Reject; }
        else if (attacker != 0)
        {
            if (attackerOwner < 0) { if (options.StrictAttackers) return GuardVerdict.Reject; }
            else if (attackerOwner != sender) return GuardVerdict.Reject;
        }
        var damage = BinaryPrimitives.ReadUInt32LittleEndian(payload[DamageOffset..]);
        if (damage <= options.MaxDamage) return GuardVerdict.Allow;
        payload.CopyTo(clamped);
        BinaryPrimitives.WriteUInt32LittleEndian(clamped[DamageOffset..], options.MaxDamage);
        return GuardVerdict.Clamped;
    }
}
