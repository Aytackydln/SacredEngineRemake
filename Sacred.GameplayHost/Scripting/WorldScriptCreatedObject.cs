using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Executed object construction. The authored item block is retained independently from
/// subsequent value calculation. Animation/bone and complete world placement remain upload dependencies.</summary>
public sealed class WorldScriptCreatedObject
{
    private readonly byte[] _instance;
    internal WorldScriptCreatedObject(int sourceOffset, SacredScriptCreateObject creation, GoldObjectBaseState state, byte[] instance)
    {
        (SourceOffset, Creation, BaseState, _instance) = (sourceOffset, creation, state, instance.ToArray());
        CurrentValues = instance.ToArray();
    }
    public int SourceOffset { get; }
    public SacredScriptCreateObject Creation { get; }
    public GoldObjectBaseState BaseState { get; internal set; }
    /// <summary>cChest's real cNode header; model loading and later ticks remain separate.</summary>
    public WorldObjectNode Node { get; } = new() { Flags = 2, PeriodMilliseconds = 200000 };
    /// <summary>Actual current item-base values, separate from the retained file record.</summary>
    public ReadOnlyMemory<byte> CurrentValues { get; internal init; }
    /// <summary>Native +0x2C, static floor selected on elevated sector re-placement.</summary>
    public uint StaticSurfaceReference { get; internal set; }
    /// <summary>Native +0x30, linked by the sector-load callback.</summary>
    public uint NextOnTile { get; internal set; }
    public byte[] GetAuthoredInstance() => _instance.ToArray();
    public bool PacketReady => false;
    /// <summary>Native 0xAB still requires model/animation execution before packet production.</summary>
    public ushort? PendingAnimationEvent { get; internal init; }
}
