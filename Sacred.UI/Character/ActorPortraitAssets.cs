using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Core.UI;
using Sacred.UI.Textures;

namespace Sacred.UI.Character;

/// <summary>Native portrait crops and actor-to-crop selectors read from the selected installation.</summary>
public sealed class ActorPortraitAssets
{
    public UiTextureRegion HealthFull { get; }
    public UiTextureRegion HealthEmpty { get; }
    public UiTextureRegion ExperienceFrame { get; }
    public IReadOnlyDictionary<uint, int> Selectors { get; }
    public IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> Definitions { get; }

    private ActorPortraitAssets(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> definitions,
        IReadOnlyDictionary<uint, int> selectors)
    {
        Definitions = definitions; Selectors = selectors;
        HealthFull = new(definitions[41]); HealthEmpty = new(definitions[42]);
        ExperienceFrame = new(definitions[66]);
    }

    public UiTextureRegion? GetPortrait(uint actorType) => Selectors.TryGetValue(actorType, out var selector)
        ? new(Definitions[selector]) : null;

    public static Task<ActorPortraitAssets> LoadAsync(string gameDirectory) => Task.Run(() =>
    {
        // Gold's protected executable retains the UI table but obscures its code;
        // the installed pureHD executable carries the corresponding native switch.
        Exception? failure = null;
        foreach (var filename in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(gameDirectory, filename);
            if (!File.Exists(path)) continue;
            try
            {
                var image = File.ReadAllBytes(path);
                var definitions = SacredUiTextureTable.Read(image);
                return new ActorPortraitAssets(definitions, SacredPortraitSelectors.Read(image, definitions));
            }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Native portrait definitions unavailable in this installation.", failure);
    });
}
