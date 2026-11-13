using System.Buffers.Binary;

namespace Sacred.GameplayHost.Scripting;

/// <summary>One retained 68-byte action from Gold creature +0x588. Construction
/// and placement are separate queue records; consuming the queue is a distinct
/// runtime operation. Unknown action words are preserved.</summary>
public sealed class WorldScriptDeferredObjectAction
{
    public const int Size = 68;
    public const uint CreateObjectKind = 11;
    public const uint PlacementKind = 13;
    private readonly byte[] _data;

    private WorldScriptDeferredObjectAction(byte[] data) => _data = data;
    public uint Kind => Word(0);
    /// <summary>Only kind 11 contains a resource type in word 1.</summary>
    public uint ObjectType => Kind == CreateObjectKind ? Word(4) :
        throw new InvalidOperationException("The deferred action is not object construction.");
    public uint PrimaryResource => Kind == CreateObjectKind ? Word(32) :
        throw new InvalidOperationException("The deferred action is not object construction.");
    public uint Word(int offset)
    {
        if (offset < 0 || offset > Size - 4 || offset % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset));
    }
    public byte[] ToPayload() => (byte[])_data.Clone();
    public static WorldScriptDeferredObjectAction FromPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length != Size) throw new InvalidDataException("Expected one complete deferred object action.");
        return new(data.ToArray());
    }
}
