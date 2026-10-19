namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x450630 / 0x622620: heading from physical +0x70, in projected-world degrees.</summary>
public static class GoldPhysicalFacing
{
    /// <summary>Gold 0x44DDC0 / 0x622660: a reported projected-world heading becomes +0x70 again.</summary>
    public static GoldPhysicalVector ToDirection(uint facingBits)
    {
        var angle = BitConverter.UInt32BitsToSingle(facingBits) * (float)(Math.PI / 180);
        var x = MathF.Cos(angle); var y = MathF.Sin(angle);
        var inverse = MathF.Sqrt(5) * .25f;
        return new((x + 2*y) * inverse, (-x + 2*y) * inverse, 0);
    }

    public static uint FromDirection(GoldPhysicalVector direction)
    {
        // Native projection rows are (2/sqrt(5), -2/sqrt(5)) and (1/sqrt(5), 1/sqrt(5)).
        var scale = 1 / MathF.Sqrt(5);
        var x = direction.X * (2 * scale) - direction.Y * (2 * scale);
        var y = direction.X * scale + direction.Y * scale;
        return BitConverter.SingleToUInt32Bits(MathF.Atan2(y, x) * (float)(180 / Math.PI));
    }
}
