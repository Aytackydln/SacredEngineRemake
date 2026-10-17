using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Executed object construction. The authored item block is retained independently from
/// subsequent value calculation. Animation/bone and complete world placement remain upload dependencies.</summary>
public sealed class WorldScriptCreatedObject
{
    private readonly byte[] _instance;
    internal WorldScriptCreatedObject(int sourceOffset, SacredScriptCreateObject creation, GoldObjectBaseState state, byte[] instance)
        => (SourceOffset, Creation, BaseState, _instance) = (sourceOffset, creation, state, instance.ToArray());
    public int SourceOffset { get; }
    public SacredScriptCreateObject Creation { get; }
    public GoldObjectBaseState BaseState { get; }
    public byte[] GetAuthoredInstance() => _instance.ToArray();
    public bool PacketReady => false;
    /// <summary>Native 0xAB still requires model/animation execution before packet production.</summary>
    public ushort? PendingAnimationEvent { get; internal init; }
}
