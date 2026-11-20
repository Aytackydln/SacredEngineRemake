using Sacred.Core.Pax.Hero.Statistics;
using Sacred.Core.Pax.Hero;
using System.Buffers.Binary;
using Sacred.Core.Runtime;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable native statistics snapshot. The automatic-level elapsed
/// counter is mapped; other counters and their wider lifecycle remain separate.</summary>
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
    public uint AutomaticLevelElapsedMilliseconds => BinaryPrimitives.ReadUInt32LittleEndian(
        PrimaryData[SacredHeroStatisticsPrimaryLayout.AutomaticLevelElapsedMillisecondsOffset..]);

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

    /// <summary>Restore the saved startup statistics into a newly initialized local block.</summary>
    public static GoldPlayerStatisticsState FromSavedCharacter(SacredSavedCharacterRecords source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var payload = new byte[GoldPlayerStatisticsLayout.Size];
        source.Profile.Slice(SacredHeroProfileLayout.StatisticsBytesOffset, SacredHeroProfileLayout.StatisticsBytesSize)
            .CopyTo(payload.AsSpan(SacredHeroStatisticsPrimaryLayout.ProfileBytesOffset));
        var start = source.StartInfo;
        if (!start.IsEmpty)
        {
            start.Slice(SacredHeroStartInfoLayout.Word56F0Offset, 4)
                .CopyTo(payload.AsSpan(SacredHeroStatisticsPrimaryLayout.Word56F0Offset));
            start.Slice(SacredHeroStartInfoLayout.Word56F4Offset, 4)
                .CopyTo(payload.AsSpan(SacredHeroStatisticsPrimaryLayout.Word56F4Offset));
            var triplet = source.ObjectFormatVersion < 3 ? SacredHeroStartInfoLayout.LegacyStatisticsTripletOffset :
                SacredHeroStartInfoLayout.StatisticsTripletOffset;
            start.Slice(triplet, 12).CopyTo(payload.AsSpan(SacredHeroStatisticsPrimaryLayout.StatisticsTripletOffset));
            var seconds = BinaryPrimitives.ReadUInt32LittleEndian(start[SacredHeroStartInfoLayout.AutomaticLevelElapsedSecondsOffset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(SacredHeroStatisticsPrimaryLayout.AutomaticLevelElapsedMillisecondsOffset), unchecked(seconds * 1000u));
            start.Slice(SacredHeroStartInfoLayout.Word5710Offset, 2)
                .CopyTo(payload.AsSpan(SacredHeroStatisticsPrimaryLayout.Word5710Offset));
        }
        if (source.StatisticsExtension is { } extension)
            extension.Data.CopyTo(payload.AsSpan(GoldPlayerStatisticsLayout.ExtensionOffset));
        return new(payload);
    }

    public byte[] ToPayload() => _payload.ToArray();
}
