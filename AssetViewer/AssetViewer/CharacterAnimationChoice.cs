using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Models;
using Sacred.Granny.Animation;

namespace AssetViewer.AssetViewer;

internal sealed record CharacterAnimationChoice(string Name, byte? Slot = null, bool IsDefault = false)
{
    public static CharacterAnimationChoice BindPose { get; } = new("Bind pose");
    public static CharacterAnimationChoice Default { get; } = new("Default animation", IsDefault: true);

    public static CharacterAnimationChoice[] ForModel(ModelsPakArchive models, string name)
    {
        var choices = new List<CharacterAnimationChoice> { BindPose, Default };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var slot = 0; slot <= byte.MaxValue; slot++)
            if (models.TryGetModelMotionName(name, (byte)slot, out var motion) && models.ContainsResource(motion) && seen.Add(motion))
                choices.Add(new(motion, (byte)slot));
        return choices.ToArray();
    }

    public Task<GrnAnimationClip?> LoadAsync(ModelsPakArchive models, string modelName, CancellationToken token) =>
        IsDefault ? models.LoadDefaultCharacterAnimationAsync(modelName, token) :
        Slot is { } slot ? models.LoadModelAnimationAsync(modelName, slot, token) :
        Task.FromResult<GrnAnimationClip?>(null);

    public override string ToString() => Name;
}
