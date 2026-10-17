namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>C0/C7 contains a retained 16-byte prefix followed by the versioned saved creature.</summary>
public sealed class SacredSavedHero
{
    public const int PrefixSize = 16;
    private readonly byte[] _payload;
    private SacredSavedHero(byte[] payload, SacredSavedCreature creature) => (_payload, Creature) = (payload, creature);
    public SacredSavedCreature Creature { get; }
    public ReadOnlySpan<byte> Prefix => _payload.AsSpan(0, PrefixSize);
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(PrefixSize + Creature.SerializedSize);
    public byte[] ToPayload() => _payload.ToArray();
    public static SacredSavedHero FromPayload(ReadOnlySpan<byte> payload, byte version)
    {
        SacredSavedStreamBounds.Require(payload, PrefixSize);
        var creature = SacredSavedCreature.FromPayload(payload[PrefixSize..], version);
        return new(payload.ToArray(), creature);
    }
}
