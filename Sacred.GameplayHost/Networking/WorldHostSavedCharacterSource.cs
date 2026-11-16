using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.GameBin;
using Sacred.Core.Pax.Hero;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>One original selected PAX snapshot. Full source decoding does not
/// restore a runtime actor, choose a campaign start position or grant readiness.</summary>
public sealed class WorldHostSavedCharacterSource
{
    private WorldHostSavedCharacterSource(int selection, SacredHeroPaxProfile profile, SacredSavedCharacterRecords records,
        SacredMultiplayerStartArchive startPositions, string installation)
        => (Selection, Profile, Records, StartPositions, Installation) = (selection, profile, records, startPositions, installation);
    internal string Installation { get; }
    public SacredMultiplayerStartArchive StartPositions { get; }
    public int Selection { get; }
    public SacredHeroPaxProfile Profile { get; }
    public SacredSavedCharacterRecords Records { get; }
    public int ObjectCount
    {
        get { var count = 0; foreach (var record in Records.Objects.Records) if (!record.IsEmpty) count++; return count; }
    }

    public static WorldHostSavedCharacterSource ReadSelected(string installation, int selection)
    {
        var archive = SacredHeroPaxArchive.ReadSelected(installation, selection);
        var profile = SacredHeroPaxProfile.FromPayload(archive.ToPayload());
        var templates = SacredObjectTemplateArchive.Load(installation);
        var records = SacredSavedCharacterRecords.FromArchive(archive,
            resource => (byte)templates.GetItem(resource).ModelDesc.Category, 64 * 1024 * 1024,
            (int)GoldReferenceCodec.ReferenceLimit - 1);
        if (!records.Profile.SequenceEqual(profile.Profile))
            throw new InvalidDataException("Selected character profile differs from its decoded source records.");
        return new(selection, profile, records, SacredMultiplayerStartArchive.Load(installation), installation);
    }
}
