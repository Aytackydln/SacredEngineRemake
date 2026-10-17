namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x450630 / 0x622620: heading from physical +0x70, in projected-world degrees.</summary>
public static class GoldPhysicalFacing
{
    public static uint FromDirection(GoldPhysicalVector direction)
    {
        // Native projection rows are (2/sqrt(5), -2/sqrt(5)) and (1/sqrt(5), 1/sqrt(5)).
        var scale = 1 / MathF.Sqrt(5);
        var x = direction.X * (2 * scale) - direction.Y * (2 * scale);
        var y = direction.X * scale + direction.Y * scale;
        return BitConverter.SingleToUInt32Bits(MathF.Atan2(y, x) * (float)(180 / Math.PI));
    }
}
