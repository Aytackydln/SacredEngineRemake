using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Script object creation supplied by the gameplay host.</summary>
public interface IWorldScriptObjectWorld
{
    IReadOnlyList<WorldScriptCreatedObject> Objects { get; }
    IReadOnlyList<GoldObjectScriptCallback> Callbacks { get; }
    void Create(WorldScriptExecutionContext context, SacredScriptCommand command);
}
