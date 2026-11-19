using System.Buffers.Binary;
using Sacred.GameplayHost.Scripting;

namespace Sacred.GameplayHost.Networking;

/// <summary>Engine 60BA00's two native local-value snapshots. The initial
/// publication path has no intervening stat mutations. Later gameplay updates
/// require their own original snapshot-update callers.</summary>
public sealed class WorldHostHeroValueSnapshots
{
    private readonly byte[] _values;
    private readonly byte _key;
    private readonly WorldObjectNode _node;
    private readonly uint _reference;
    public ReadOnlyMemory<byte> First { get; }
    public ReadOnlyMemory<byte> Second { get; }

    internal WorldHostHeroValueSnapshots(WorldCreatureInitialActor actor)
    {
        if (!actor.SavedLoaderTailPrepared || !actor.SavedWorldPreludeCompleted || actor.CampaignEntryPlaced ||
            !actor.Node.Active || actor.Component.Values.Length != 238)
            throw new InvalidOperationException("Value snapshots require the current restored hero before campaign placement.");
        (_values, _key, _node, _reference) = (actor.Component.Values.ToArray(), actor.Base.Byte38, actor.Node, actor.Base.ObjectReference);
        var mask = (uint)_key * 0x01010101u;
        First = Capture(mask);
        Second = Capture(unchecked(mask << 1));
    }

    public void RequireMatch(WorldCreatureInitialActor actor)
    {
        if (!_node.Active || !ReferenceEquals(_node, actor.Node) || actor.Base.ObjectReference != _reference || actor.Base.Byte38 != _key ||
            !actor.Component.Values.AsSpan().SequenceEqual(_values))
            throw new NotSupportedException("Current hero values differ from their native initial publication snapshots; original mutation/update lifecycle is required.");
    }

    private byte[] Capture(uint mask)
    {
        var result = new byte[250];
        BinaryPrimitives.WriteUInt32LittleEndian(result, BinaryPrimitives.ReadUInt32LittleEndian(_values.AsSpan(4)) ^ mask);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(_values.AsSpan(0x3e)) ^ mask);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), BinaryPrimitives.ReadUInt16LittleEndian(_values.AsSpan(0x4e)) ^ mask);
        _values.CopyTo(result, 12);
        return result;
    }
}
