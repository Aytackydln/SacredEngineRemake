using System.Buffers.Binary;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>CalcResults' actor-flag adjustments before innate rows and unarmed
/// fallback after them (Gold 0x57B68A and 0x57C141). Preserve this ordering.</summary>
public static class SacredCreatureCombatAdjustments
{
    public static void ApplyActorFlags(byte[] values, uint flags)
    {
        Validate(values);
        if ((flags & 0x200000) == 0) return;
        Write(values, 150, Read(values, 150) + 0.5d);
        Write(values, 206, Read(values, 206) * 2d);
        Write(values, 210, Read(values, 210) * 2d);
        Write(values, 214, Read(values, 214) * 0.5d);
        Write(values, 218, Read(values, 218) * 2d);
    }

    public static void ApplyUnarmedFallback(byte[] values, bool unarmed)
        => ApplyUnarmedFallback(SacredX87Precision.Extended, values, unarmed);

    public static void ApplyUnarmedFallback(SacredX87Precision precision, byte[] values, bool unarmed)
    {
        var fp = new SacredResultArithmetic(precision);
        Validate(values);
        var maximum = Read(values, 110);
        if (!unarmed || maximum != 0 && !float.IsNaN(maximum)) return;
        var strength = Word(8);
        var dexterity = Word(12);
        var levelPart = Word(78) * 0.5d;
        Write(values, 126, fp.Add(fp.Add(fp.Multiply(dexterity + strength, 0.175f), levelPart), Read(values, 126)));
        // Original operand 0x3E199999 is the float below the usual 0.15f.
        Write(values, 110, fp.Add(fp.Add(fp.Multiply(dexterity + strength * 2, 0.14999999105930328f), levelPart), maximum));
        ushort Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(at));
    }

    private static void Validate(byte[] values)
    {
        if (values.Length != SacredActorValuesLayout.Size) throw new ArgumentException("Expected 238-byte actor values.", nameof(values));
    }
    private static float Read(byte[] values, int at) => BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(at));
    private static void Write(byte[] values, int at, double value) => BinaryPrimitives.WriteSingleLittleEndian(values.AsSpan(at), (float)value);
}
