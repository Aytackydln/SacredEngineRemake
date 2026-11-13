using System.Buffers.Binary;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Gold special-health branch constants, recovered from original
/// comparisons and addressed floating constants. The caller supplies the native
/// class predicate result; this reader does not infer it from an item ID.</summary>
public sealed class SacredCreatureHealthFactorDefinitions
{
    private readonly uint[] _scaledTypes;
    private readonly uint _fallbackType;
    private readonly double[] _difficulty;
    private readonly float _typeMultiplier;

    private SacredCreatureHealthFactorDefinitions(uint[] scaledTypes, uint fallbackType,
        double[] difficulty, float typeMultiplier)
        => (_scaledTypes, _fallbackType, _difficulty, _typeMultiplier) = (scaledTypes, fallbackType, difficulty, typeMultiplier);

    public int ApplyStartup(int health, float factor, uint type, uint difficulty, bool classPredicate)
    {
        return SacredCreatureSpecialHealth.ApplyStartup(health, factor, Multiplier(type, difficulty, classPredicate));
    }

    public float FactorStartup(float factor, uint type, uint difficulty, bool classPredicate)
        => SacredCreatureSpecialHealth.FactorStartup(factor, Multiplier(type, difficulty, classPredicate));

    private double Multiplier(uint type, uint difficulty, bool classPredicate)
        => _scaledTypes.Contains(type) ? _typeMultiplier
            : classPredicate ? _difficulty[difficulty == 0 ? 0 : difficulty == 1 ? 1 : 2]
            : type == _fallbackType ? _typeMultiplier : 1d;

    public static SacredCreatureHealthFactorDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> prefix = [0x8B, 0x40, 0x10, 0x3D];
        var found = -1;
        for (var search = 0; search <= image.Length - 128;)
        {
            var relative = image.AsSpan(search).IndexOf(prefix);
            if (relative < 0) break;
            var at = search + relative;
            search = at + prefix.Length;
            if (at > image.Length - 128 || image[at + 8] != 0x74 || image[at + 10] != 0x3D ||
                image[at + 15] != 0x74 || image[at + 17] != 0x8B || image[at + 18] != 0x0D || image[at + 24] != 0xE8 ||
                !image.AsSpan(at + 90, 3).SequenceEqual<byte>([0x83, 0x7A, 0x10])) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original special-health factor branch.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Original special-health factor branch unavailable.");
        var reader = new SacredNativeTypeSwitch(image);
        var doubles = new double[3];
        int[] loads = [42, 59, 71];
        for (var i = 0; i < loads.Length; i++)
        {
            var at = found + loads[i];
            if (!image.AsSpan(at, 2).SequenceEqual<byte>([0xDD, 0x05]))
                throw new InvalidDataException("Unknown special-health multiplier load.");
            var address = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 2));
            doubles[i] = BinaryPrimitives.ReadDoubleLittleEndian(reader.At(address, 8));
        }
        if (!image.AsSpan(found + 102, 2).SequenceEqual<byte>([0xD8, 0x0D]))
            throw new InvalidDataException("Unknown special-health type multiplier load.");
        var typeAddress = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(found + 104));
        var typeFactor = BinaryPrimitives.ReadSingleLittleEndian(reader.At(typeAddress, 4));
        if (!float.IsFinite(typeFactor) || doubles.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("Nonfinite special-health multiplier.");
        return new([BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(found + 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(found + 11))], image[found + 93], doubles, typeFactor);
    }
}
