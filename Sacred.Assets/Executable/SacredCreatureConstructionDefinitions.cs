using System.Buffers.Binary;
using System.Collections.Frozen;

namespace Sacred.Assets.Executable;

public readonly record struct SacredCreatureConstructionFlags(uint ObjectFlags, uint BehaviorFlags);

/// <summary>Constructor's native type exceptions and initial equipment selector.
/// All compared IDs and masks are extracted from original instructions.</summary>
public sealed class SacredCreatureConstructionDefinitions
{
    private readonly FrozenDictionary<uint, SacredCreatureConstructionFlags> _flags;
    private readonly SacredNativeTypeSwitch _switch;
    private readonly ulong _equipment;
    private SacredCreatureConstructionDefinitions(Dictionary<uint, SacredCreatureConstructionFlags> flags,
        SacredNativeTypeSwitch getter, ulong equipment) => (_flags, _switch, _equipment) = (flags.ToFrozenDictionary(), getter, equipment);
    public SacredCreatureConstructionFlags Flags(uint type) => _flags.GetValueOrDefault(type);
    public ushort EquipmentSelector(uint type) => unchecked((ushort)_switch.Read(_equipment, type));
    public static SacredCreatureConstructionDefinitions Load(string directory)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original creature constructor definitions unavailable.", failure);
    }
    public static SacredCreatureConstructionDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> store = [0x66, 0x89, 0x86, 0x5F, 2, 0, 0, 0x8B, 0x46, 0x10, 0x83, 0xF8];
        var offset = image.AsSpan().IndexOf(store);
        if (offset < 90 || offset > image.Length - 400 || image[offset - 5] != 0xE8)
            throw new InvalidDataException("Original creature constructor selector call not found.");
        var getter = new SacredNativeTypeSwitch(image);
        var equipment = unchecked((ulong)((long)getter.Address(offset) + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset - 4))));
        var flags = new Dictionary<uint, SacredCreatureConstructionFlags>();
        var first = offset - 90;
        if (!image.AsSpan(first, 3).SequenceEqual<byte>([0x8B, 0x46, 0x10])) throw new InvalidDataException("Unknown constructor prefix.");
        uint mask = 0;
        _ = Group(first + 3, ref mask);
        var cursor = Group(offset + 10, ref mask);
        _ = Group(cursor, ref mask);
        ReadOnlySpan<byte> ending = [0x8B, 0x46, 0x10, 0xB9];
        var tail = image.AsSpan(offset + 200, 200).IndexOf(ending);
        if (tail < 0) throw new InvalidDataException("Constructor tail type predicates not found.");
        cursor = offset + 200 + tail + 3;
        mask = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(cursor + 1));
        cursor = Group(cursor + 5, ref mask);
        _ = Group(cursor, ref mask);
        _ = getter.Read(equipment, 0); // Validate the constant-return default branch before exposing this reader.
        return new(flags, getter, equipment);

        int Group(int start, ref uint currentMask)
        {
            var cases = new List<uint>(); var targets = new List<int>(); var at = start; int lastSkip = -1;
            for (var count = 0; count < 100; count++)
            {
                var code = image.AsSpan(at);
                if (code[0] == 0x3D) { cases.Add(BinaryPrimitives.ReadUInt32LittleEndian(code[1..])); at += 5; }
                else if (code[..2].SequenceEqual<byte>([0x83, 0xF8])) { cases.Add(unchecked((uint)(sbyte)code[2])); at += 3; }
                else throw new InvalidDataException("Unknown native constructor type comparison.");
                if (image[at] is 0xBF or 0xB9) { currentMask = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 1)); at += 5; }
                if (image[at] == 0x74) targets.Add(at + 2 + (sbyte)image[at + 1]);
                else if (image[at] == 0x75) lastSkip = at + 2 + (sbyte)image[at + 1];
                else throw new InvalidDataException("Unknown native constructor conditional branch.");
                at += 2;
                if (lastSkip < 0) continue;
                var setter = image.AsSpan(at); var objectFlags = false; int size;
                if (setter[..6].SequenceEqual<byte>([0x81, 0x8E, 0xF4, 1, 0, 0])) { currentMask = BinaryPrimitives.ReadUInt32LittleEndian(setter[6..]); size = 10; }
                else if (setter[..6].SequenceEqual<byte>([0x09, 0xBE, 0xF4, 1, 0, 0]) || setter[..6].SequenceEqual<byte>([0x09, 0x8E, 0xF4, 1, 0, 0])) size = 6;
                else if (setter[..3].SequenceEqual<byte>([0x09, 0x4E, 0x14])) { objectFlags = true; size = 3; }
                else throw new InvalidDataException("Unknown native constructor flag setter.");
                if (lastSkip != at + size || targets.Any(target => target != at)) throw new InvalidDataException("Constructor predicates do not share their flag setter.");
                foreach (var type in cases)
                {
                    var previous = flags.GetValueOrDefault(type);
                    flags[type] = objectFlags ? previous with { ObjectFlags = previous.ObjectFlags | currentMask } : previous with { BehaviorFlags = previous.BehaviorFlags | currentMask };
                }
                return at + size;
            }
            throw new InvalidDataException("Constructor type predicate exceeds its bounded case count.");
        }
    }
}
