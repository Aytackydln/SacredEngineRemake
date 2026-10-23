using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public readonly record struct GoldTriggerPatchCell(int X, int Y, int Surface)
{
    /// <summary>Gold 0x463F93: signed surface values below 2 share the outdoor key; shifts and ORs wrap.</summary>
    public uint Key => unchecked((((uint)X << 14 | (uint)Y) << 4) | (Surface >= 2 ? (uint)Surface : 0));
}

public sealed record GoldTriggerPatchEvent(GoldMessage Message, int DeclaredCount, uint TriggerReference,
    IReadOnlyList<GoldTriggerPatchCell> Cells) : GoldScriptWorldEvent(Message)
{
    public int ActiveCount => Math.Clamp(DeclaredCount, 0, GoldTriggerPatchCodec.Capacity);
}

/// <summary>TriggerPatch2 always copies all 100 triples before applying the signed count clamp.</summary>
public static class GoldTriggerPatchCodec
{
    public const ushort Opcode = 0x1CE;
    public const int Capacity = 100;
    public const int MinimumSize = 8+Capacity*12;
    public static GoldTriggerPatchEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < MinimumSize)
            throw new InvalidDataException("Gold trigger patch requires two words and all 100 coordinate triples.");
        var bytes = message.Payload.AsSpan(); var cells = new GoldTriggerPatchCell[Capacity];
        for (var i = 0; i < cells.Length; i++)
        {
            var offset = 8+i*12;
            cells[i] = new(BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]),
                BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset+4)..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset+8)..]));
        }
        return new(message with { Payload = bytes.ToArray() }, BinaryPrimitives.ReadInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), Array.AsReadOnly(cells));
    }
}
