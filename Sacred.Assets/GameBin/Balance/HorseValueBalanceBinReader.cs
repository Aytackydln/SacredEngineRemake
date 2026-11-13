using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;
namespace Sacred.Assets.GameBin.Balance;
public static class HorseValueBalanceBinReader
{
    public static SacredHorseValueBalance Read(ReadOnlySpan<byte> file)
    {
        var needed = SacredHorseValueBalance.GlobalOffset + SacredHorseValueBalance.Count * SacredHorseValueCurveLayout.Size;
        if (file.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(file) < needed || file.Length < needed + 4)
            throw new InvalidDataException("Balance.bin omits original HorseClass value curves.");
        return new(MemoryMarshal.Cast<byte,SacredHorseValueCurveLayout>(file.Slice(4+SacredHorseValueBalance.GlobalOffset, SacredHorseValueBalance.Count*SacredHorseValueCurveLayout.Size)).ToArray());
    }
}
