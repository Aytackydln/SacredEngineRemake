using System.Threading;
using System.Threading.Tasks;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Assets;

public sealed partial class AssetManager
{
    internal Task<GrnAnimationClip?> LoadNetworkCombatMotionAsync(string modelName, uint motion,
        CancellationToken cancellationToken) => motion <= byte.MaxValue
        ? _modelsPak.LoadModelAnimationAsync(modelName, (byte)motion, cancellationToken)
        : Task.FromResult<GrnAnimationClip?>(null);
}
