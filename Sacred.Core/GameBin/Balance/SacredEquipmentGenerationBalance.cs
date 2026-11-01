using Sacred.Core.Runtime;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Gold item-generation level preparation (0x5D509B and 0x5D52A2..0x5D531D).
/// Combat uses the capped input level; later eligibility/bonus stages use the raised level.</summary>
public sealed class SacredEquipmentGenerationBalance
{
    public SacredEquipmentGenerationBalance(float levelFactorZero, float levelFactorHundred, float levelScale)
    {
        if (!float.IsFinite(levelFactorZero) || !float.IsFinite(levelFactorHundred) ||
            !float.IsFinite(levelScale) || levelScale <= 0)
            throw new ArgumentException("Invalid equipment generation tuning.");
        (LevelFactorZero, LevelFactorHundred, LevelScale) = (levelFactorZero, levelFactorHundred, levelScale);
    }
    public float LevelFactorZero { get; }
    public float LevelFactorHundred { get; }
    public float LevelScale { get; }

    public SacredEquipmentGenerationLevel ResolveLevel(uint input, int minimumLevel, SacredX87Precision precision = SacredX87Precision.Extended)
    {
        var fp = new SacredResultArithmetic(precision);
        var level = input & 0xFFFF;
        var maximum = fp.Divide(216d, LevelScale);
        if (level > maximum) level = unchecked((uint)(long)maximum);
        var factor = (float)fp.Add(fp.Multiply(fp.Multiply(fp.Subtract(LevelFactorHundred, LevelFactorZero), level & 0xFFFF), 0.01f), LevelFactorZero);
        // The native minimum word is converted to single precision first.
        var minimum = (float)minimumLevel;
        if (fp.Multiply(level & 0xFFFF, LevelScale) < minimum && LevelScale > 1.1920929e-7f)
            level = unchecked((uint)(long)fp.Add(fp.Divide(minimum, LevelScale), 0.5d));
        return new(unchecked((ushort)level), factor);
    }
}

public readonly record struct SacredEquipmentGenerationLevel(ushort Level, float CombatFactor);
