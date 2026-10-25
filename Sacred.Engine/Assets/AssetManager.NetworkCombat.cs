using System.Threading;
using System.Threading.Tasks;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Assets;

public sealed partial class AssetManager
{
    internal bool HasNetworkCombatMotion(string modelName, uint motion) => motion <= byte.MaxValue &&
        _modelsPak.TryGetModelMotionName(modelName, (byte)motion, out var name) && _modelsPak.ContainsResource(name);
    internal Task<GrnAnimationClip?> LoadNetworkCombatMotionAsync(string modelName, uint motion,
        CancellationToken cancellationToken) => motion <= byte.MaxValue
        ? _modelsPak.LoadModelAnimationAsync(modelName, (byte)motion, cancellationToken)
        : Task.FromResult<GrnAnimationClip?>(null);
}
