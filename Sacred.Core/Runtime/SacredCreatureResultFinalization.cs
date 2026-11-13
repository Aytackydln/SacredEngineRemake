using System.Buffers.Binary;

namespace Sacred.Core.Runtime;

/// <summary>On-foot CalcResults after passive skills, with no overlevel weapon/
/// special-item penalties and no active effects. Regeneration, armor movement
/// penalty, actor speed flag and final native bounds; health/event work follows.</summary>
public static class SacredCreatureResultFinalization
{
    public static void Apply(byte[] values, uint actorFlags, uint componentFlags,
        bool horse, bool slowSpeedFloor, bool shareCombatArtRegeneration, float armorMovementFactor)
        => Apply(SacredX87Precision.Extended, values, actorFlags, componentFlags, horse, slowSpeedFloor, shareCombatArtRegeneration, armorMovementFactor);

    public static void Apply(SacredX87Precision precision, byte[] values, uint actorFlags, uint componentFlags,
        bool horse, bool slowSpeedFloor, bool shareCombatArtRegeneration, float armorMovementFactor)
    {
        var fp = new SacredResultArithmetic(precision);
        if (values.Length != SacredActorValuesLayout.Size) throw new ArgumentException("Expected 238-byte actor values.", nameof(values));
        Write(230, fp.Multiply(fp.Add(fp.Multiply(Word(16), 0.01f), 1), Read(230)));
        Write(234, fp.Multiply(fp.Add(fp.Multiply(Word(14), 0.01f), 1), Read(234)));
        if (armorMovementFactor < 1) Speed(56, Truncate(fp.Multiply(Short(56), armorMovementFactor)));
        if ((actorFlags & 0x2000) != 0)
            for (var at = 52; at <= 56; at += 2) Speed(at, Short(at) - 15);
        for (var at = 82; at < values.Length; at += 4)
        {
            if (at == 90) continue;
            var value = Read(at);
            // FCOMP's unordered result follows the same branch as negative.
            if (float.IsNaN(value) || value < 0) Write(at, 0);
        }
        var cap = (componentFlags & 0x10000) != 0 ? 260 : 220;
        var horseCap = (componentFlags & 0x10000) != 0 ? 420 : 380;
        // Native ordinary movement tests against 220 even when its replacement
        // cap is 260. A movement speed of 221 can therefore increase to 260.
        if (Short(56) > (horse ? horseCap : 220)) Speed(56, horse ? horseCap : cap);
        for (var at = 52; at <= 54; at += 2)
            if (Short(at) > cap) Speed(at, cap);
        var floor = slowSpeedFloor ? 80 : 100;
        for (var at = 52; at <= 56; at += 2)
            if (Short(at) < floor) Speed(at, floor);
        if (shareCombatArtRegeneration) values.AsSpan(234, 4).CopyTo(values.AsSpan(230, 4));

        ushort Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(at));
        short Short(int at) => BinaryPrimitives.ReadInt16LittleEndian(values.AsSpan(at));
        float Read(int at) => BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(at));
        void Write(int at, double value) => BinaryPrimitives.WriteSingleLittleEndian(values.AsSpan(at), (float)value);
        void Speed(int at, int value) => BinaryPrimitives.WriteInt16LittleEndian(values.AsSpan(at), unchecked((short)value));
        static int Truncate(double value) => unchecked((int)(long)value);
    }
}
