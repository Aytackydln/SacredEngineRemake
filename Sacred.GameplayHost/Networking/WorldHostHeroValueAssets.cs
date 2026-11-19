using Sacred.Assets.Executable;
using Sacred.GameplayHost.Runtime;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.GameplayHost.Networking;

/// <summary>Original executable/Balance definitions for the reached saved-root
/// value import. Uses the established installed Sacred/pureHD fallback order.</summary>
internal sealed record WorldHostHeroValueAssets(SacredSavedCreatureProfiles Profiles, SacredCreatureArtTimerInputs Arts)
{
    public SacredHostHeroPostLoadDefinitions PostLoad { get; init; } = null!;
    public SacredSavedInventoryModeDefinitions InventoryMode { get; init; } = null!;
    public SacredHostPublicationDefaults Publication { get; init; } = null!;
    public static WorldHostHeroValueAssets Load(string installation, SacredSavedCreature saved)
    {
        var balance = File.ReadAllBytes(Path.Combine(installation, "bin", "Balance.bin"));
        var skills = saved.Values.Block08.Slice(28, 8).ToArray().ToHashSet();
        Exception? failure = null;
        foreach (var filename in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, filename);
            if (!File.Exists(path)) continue;
            try
            {
                var image = File.ReadAllBytes(path);
                return new(SacredSavedCreatureProfiles.Read(image), new(
                    Enumerable.Range(0, saved.Values.Count22).Select(index => saved.Values.GetRecord22(index).ToArray()),
                    SacredCombatArtTimerDefinitions.Read(image), SacredCombatArtSkillTimers.Read(image, balance, skills)))
                    { PostLoad = SacredHostHeroPostLoadDefinitions.Read(image), InventoryMode = SacredSavedInventoryModeDefinitions.Read(image),
                        Publication = SacredHostPublicationDefaults.Read(image) };
            }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original saved-hero value/publication definitions unavailable.", failure);
    }
}
