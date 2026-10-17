using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Network.Gameplay;

public sealed class GoldMovementTriggerRecord
{
    private readonly byte[] _data;
    private GoldMovementTriggerRecord(byte[] data) => _data = data;
    public ushort FirstX => Word(GoldMovementTriggerLayout.FirstXOffset);
    public ushort FirstY => Word(GoldMovementTriggerLayout.FirstYOffset);
    public ushort Surface => Word(GoldMovementTriggerLayout.SurfaceOffset);
    public ushort LastX => Word(GoldMovementTriggerLayout.LastXOffset);
    public ushort LastY => Word(GoldMovementTriggerLayout.LastYOffset);
    public ushort RetainedWord => Word(GoldMovementTriggerLayout.RetainedWordOffset);
    public ReadOnlySpan<byte> NameStorage => _data.AsSpan(GoldMovementTriggerLayout.NameOffset);
    public ReadOnlySpan<byte> NameBytes
    {
        get
        {
            var end = NameStorage.IndexOf((byte)0);
            return end >= 0 ? NameStorage[..end] : NameStorage;
        }
    }
    public string Name => Encoding.Latin1.GetString(NameBytes);
    public byte[] ToPayload() => _data.ToArray();
    public static GoldMovementTriggerRecord FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldMovementTriggerLayout.Size) throw new InvalidDataException("Gold movement trigger requires exactly 76 bytes.");
        return new(payload.ToArray());
    }

    /// <summary>Native append normalization and first-sector clipping. Recursive sector routing belongs to the world initializer.
    /// Unwritten padding/name suffix bytes are zero in this managed allocation; received values are always retained.</summary>
    public static GoldMovementTriggerRecord Create(SacredBaseTriggerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.NameBytes.Length >= GoldMovementTriggerLayout.NameSize)
            throw new ArgumentException("Trigger name does not fit the native 64-byte NUL-terminated field.", nameof(definition));
        var firstX = Math.Min(definition.First.X, definition.Last.X);
        var firstY = Math.Min(definition.First.Y, definition.Last.Y);
        var lastX = Math.Max(definition.First.X, definition.Last.X);
        var lastY = Math.Max(definition.First.Y, definition.Last.Y);
        if (firstX / GoldMovementTriggerLayout.SectorWidth < lastX / GoldMovementTriggerLayout.SectorWidth)
            lastX = firstX / GoldMovementTriggerLayout.SectorWidth * GoldMovementTriggerLayout.SectorWidth + GoldMovementTriggerLayout.SectorWidth - 1;
        if (firstY / GoldMovementTriggerLayout.SectorWidth < lastY / GoldMovementTriggerLayout.SectorWidth)
            lastY = firstY / GoldMovementTriggerLayout.SectorWidth * GoldMovementTriggerLayout.SectorWidth + GoldMovementTriggerLayout.SectorWidth - 1;
        var bytes = new byte[GoldMovementTriggerLayout.Size];
        Write(GoldMovementTriggerLayout.FirstXOffset, firstX);
        Write(GoldMovementTriggerLayout.FirstYOffset, firstY);
        Write(GoldMovementTriggerLayout.SurfaceOffset, definition.Surface);
        Write(GoldMovementTriggerLayout.LastXOffset, lastX);
        Write(GoldMovementTriggerLayout.LastYOffset, lastY);
        definition.NameBytes.CopyTo(bytes.AsSpan(GoldMovementTriggerLayout.NameOffset));
        return new(bytes);
        void Write(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), unchecked((ushort)value));
    }
    private ushort Word(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset));
}
