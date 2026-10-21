using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.GameBin.Balance;

public static class CastingBalanceBinReader
{
    public static SacredSkillCurveLayout Load(string path)
    {
        var data = File.ReadAllBytes(path);
        var end = sizeof(uint) + SacredBalanceLayout.CastingSpeedCurveOffset + SacredSkillCurveLayout.Size;
        if (data.Length < end || BinaryPrimitives.ReadUInt32LittleEndian(data) < end - sizeof(uint))
            throw new InvalidDataException("Balance.bin omits its casting-speed curve.");
        var curve = MemoryMarshal.Read<SacredSkillCurveLayout>(data.AsSpan(sizeof(uint) + SacredBalanceLayout.CastingSpeedCurveOffset));
        if (!float.IsFinite(curve.Base) || !float.IsFinite(curve.Limit) || !float.IsFinite(curve.Divisor) || curve.Divisor <= 0)
            throw new InvalidDataException("Invalid Balance.bin casting-speed curve.");
        return curve;
    }
}
