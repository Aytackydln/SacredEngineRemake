using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Core.Pax.Hero.Statistics;

namespace Sacred.Core.Pax.Hero;

/// <summary>Complete source records required for participant preparation. This is not a runtime actor or network upload.</summary>
public sealed class SacredSavedCharacterRecords
{
    private readonly byte[] _profile;
    private readonly byte[]? _startInfo;
    private SacredSavedCharacterRecords(SacredSavedHero hero, SacredSavedObjectList objects, SacredSavedInventory inventory,
        SacredSavedInventory? shared, byte[] profile, byte[]? startInfo, SacredHeroStatisticsExtension? statisticsExtension)
        => (Hero, Objects, Inventory, SharedInventory, _profile, _startInfo, StatisticsExtension) =
            (hero, objects, inventory, shared, profile, startInfo, statisticsExtension);
    public SacredSavedHero Hero { get; }
    public SacredSavedObjectList Objects { get; }
    public SacredSavedInventory Inventory { get; }
    public SacredSavedInventory? SharedInventory { get; }
    public ReadOnlySpan<byte> Profile => _profile;
    public ReadOnlySpan<byte> StartInfo => _startInfo;
    public SacredHeroStatisticsExtension? StatisticsExtension { get; }
    public byte ObjectFormatVersion { get; private init; }
    public static SacredSavedCharacterRecords FromArchive(SacredHeroPaxArchive archive, Func<uint, byte> resolveCategory, int maximumDecodedBytes, int maximumObjects)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(resolveCategory);
        SacredSavedHero? hero = null;
        SacredSavedObjectList? objects = null;
        SacredSavedInventory? inventory = null, shared = null;
        byte[]? profile = null, startInfo = null;
        SacredHeroStatisticsExtension? statisticsExtension = null;
        var version = archive.Header.ObjectFormatVersion;
        for (var index = 0; index < archive.Chunks.Length; index++)
        {
            var type = SacredHeroPaxChunkTypes.GetDecodedType(archive.Chunks[index].Type);
            switch (type)
            {
                case SacredHeroPaxChunkTypes.Hero: hero = SacredSavedHero.FromPayload(archive.DecodeChunk(index, maximumDecodedBytes), version); break;
                case SacredHeroPaxChunkTypes.Objects: objects = SacredSavedObjectList.FromPayload(archive.DecodeChunk(index, maximumDecodedBytes), version, resolveCategory, maximumObjects); break;
                case SacredHeroPaxChunkTypes.Inventory: inventory = SacredSavedInventory.FromPayload(archive.DecodeChunk(index, maximumDecodedBytes)); break;
                case SacredHeroPaxChunkTypes.SharedInventory: shared = SacredSavedInventory.FromPayload(archive.DecodeChunk(index, maximumDecodedBytes)); break;
                case SacredHeroPaxChunkTypes.Profile: profile = archive.DecodeChunk(index, maximumDecodedBytes); break;
                case SacredHeroPaxChunkTypes.StartInfo: startInfo = archive.DecodeChunk(index, maximumDecodedBytes); break;
                case SacredHeroPaxChunkTypes.StatisticsExtension:
                    statisticsExtension = SacredHeroStatisticsExtension.FromPayload(archive.DecodeChunk(index, maximumDecodedBytes));
                    break;
            }
        }
        if (hero is null || objects is null || inventory is null || profile is null)
            throw new InvalidDataException("Saved participant preparation requires hero, object, inventory and profile records.");
        return new(hero, objects, inventory, shared, profile, startInfo, statisticsExtension) { ObjectFormatVersion = version };
    }
}
