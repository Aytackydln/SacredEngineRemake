using System.Buffers.Binary;
using System.Numerics;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldFxBatchEntry(uint CreationParameter, GoldObjectPosition Position, ReadOnlyMemory<byte> NativePosition,
    Vector3 Vector, ReadOnlyMemory<byte> EffectData, uint ScalarBits);
public sealed record GoldFxBatchEvent(GoldMessage Message, uint SourceReference, uint CombatArt,
    IReadOnlyList<GoldFxBatchEntry> Entries, uint TargetReference, Vector3 Vector734,
    Vector3 Vector740, uint FinalParameter, byte Owner, bool SuppressTarget) : GoldReceiveWorkEvent(Message);
public sealed record GoldFxBatchWork(uint FxType, uint EventId, uint SourceReference, uint TargetReference,
    uint CreationParameter, GoldObjectPosition Position, Vector3 Vector1, Vector3 Vector2,
    int Parameter, int Index, uint CombatArt, byte Owner, ReadOnlyMemory<byte> EffectData);

/// <summary>Gold 7E97F0: two words followed by the fixed 20-entry, 0x756-byte FX batch.</summary>
public static class GoldFxBatchCodec
{
    public const ushort Opcode = 0x181;
    public const int Size = 8 + 0x756;
    public static GoldFxBatchEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < Size) throw new InvalidDataException("Truncated Gold FX batch.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(at));
        Vector3 Vector(int at) => new(BitConverter.UInt32BitsToSingle(Word(at)), BitConverter.UInt32BitsToSingle(Word(at + 4)), BitConverter.UInt32BitsToSingle(Word(at + 8)));
        var count = Word(8 + 0x750);
        if (count > 20) throw new InvalidDataException("Gold FX batch exceeds its 20-entry arrays.");
        var entries = new GoldFxBatchEntry[(int)count];
        for (var i = 0; i < entries.Length; i++)
        {
            var at = 8 + 0x50 + i * 16;
            entries[i] = new(Word(8 + i * 4), new(unchecked((int)Word(at + 4)), unchecked((int)Word(at + 8)), (byte)Word(at + 12), (ushort)Word(at)),
                owned.Payload.AsMemory(at, 16),
                Vector(8 + 0x190 + i * 12), owned.Payload.AsMemory(8 + 0x280 + i * 56, 56), Word(8 + 0x6E0 + i * 4));
        }
        return new(owned, Word(0), Word(4), Array.AsReadOnly(entries), Word(8 + 0x730), Vector(8 + 0x734),
            Vector(8 + 0x740), Word(8 + 0x74C), owned.Payload[8 + 0x754], owned.Payload[8 + 0x755] != 0);
    }
    internal static IReadOnlyList<GoldFxBatchWork> Plan(GoldFxBatchEvent batch, IReadOnlyDictionary<uint, GoldReplicatedObject> objects)
    {
        var work = new List<GoldFxBatchWork>();
        for (var i = 0; i < batch.Entries.Count - 1; i++)
        {
            var e = batch.Entries[i]; var scalar = BitConverter.UInt32BitsToSingle(e.ScalarBits);
            // x87 unordered sets C0 too, so NaN follows the conversion branch and yields low32=0.
            var bounded = scalar >= 30 ? 30 : NativeInteger(scalar);
            work.Add(new(0x31F, 0x20, batch.SourceReference, batch.TargetReference, e.CreationParameter, e.Position,
                e.Vector, batch.Vector734, bounded, i, batch.CombatArt, batch.Owner, e.EffectData));
        }
        if (batch.Entries.Count != 0 && !batch.SuppressTarget && objects.TryGetValue(batch.TargetReference, out var target))
        {
            var e = batch.Entries[^1]; var p = target.BaseState;
            work.Add(new(0x368, 0x60, batch.SourceReference, batch.TargetReference, e.CreationParameter,
                new(p.X, p.Y, p.Surface, p.Sector), batch.Vector740, default, unchecked((int)batch.FinalParameter), 0,
                batch.CombatArt, batch.Owner, e.EffectData));
        }
        return work.AsReadOnly();
    }
    private static int NativeInteger(float value) => float.IsFinite(value) && (double)value >= long.MinValue && (double)value < 9223372036854775808d
        ? unchecked((int)(long)value) : 0;
}
