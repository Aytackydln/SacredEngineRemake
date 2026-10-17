using Sacred.Core.Pax.Hero.Statistics;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable native statistics snapshot. Counters and gameplay calculations remain uninterpreted.</summary>
public sealed class GoldPlayerStatisticsState
{
    private readonly byte[] _payload;
    private GoldPlayerStatisticsState(byte[] payload)
    {
        _payload = payload;
        Extension = SacredHeroStatisticsExtension.FromPayload(payload.AsSpan(GoldPlayerStatisticsLayout.ExtensionOffset));
    }
    /// <summary>Native +0x30A4..+0x5713 bytes, kept without naming unverified counters.</summary>
    public ReadOnlySpan<byte> PrimaryData => _payload.AsSpan(GoldPlayerStatisticsLayout.PrimaryOffset, GoldPlayerStatisticsLayout.PrimarySize);
    public SacredHeroStatisticsExtension Extension { get; }

    public static GoldPlayerStatisticsState FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldPlayerStatisticsLayout.Size)
            throw new InvalidDataException("Gold player statistics requires exactly 9,940 bytes.");
        return new(payload.ToArray());
    }

    public static GoldPlayerStatisticsState Create(ReadOnlySpan<byte> primaryData, SacredHeroStatisticsExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (primaryData.Length != GoldPlayerStatisticsLayout.PrimarySize)
            throw new ArgumentException("Gold primary statistics block requires exactly 9,840 bytes.", nameof(primaryData));
        var payload = new byte[GoldPlayerStatisticsLayout.Size];
        primaryData.CopyTo(payload);
        extension.Data.CopyTo(payload.AsSpan(GoldPlayerStatisticsLayout.ExtensionOffset));
        return new(payload);
    }

    public byte[] ToPayload() => _payload.ToArray();
}
