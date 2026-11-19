using Sacred.Granny.Animation;

namespace Sacred.GameplayHost.Networking;

/// <summary>The reactivated body's actual shared rest pose. No clip or control
/// is invented when the original loader resets native animation selector +0x64.</summary>
public sealed class WorldHostHeroBindPose
{
    private readonly GrnPose _pose;
    internal WorldHostHeroBindPose(WorldHostHeroModelResources resources)
    {
        if (resources.IsInvalidated || resources.Body.Skin is null || resources.Body.Mesh is null)
            throw new InvalidOperationException("Bind pose requires the current original body resource.");
        _pose = GrnBindPose.Create(resources.Body.Skin.SkinningData);
    }
    public bool IsInvalidated { get; private set; }
    public GrnPose Pose => IsInvalidated ? throw new InvalidOperationException("Host body pose lifetime ended.") : _pose;
    internal void Invalidate() => IsInvalidated = true;
}
