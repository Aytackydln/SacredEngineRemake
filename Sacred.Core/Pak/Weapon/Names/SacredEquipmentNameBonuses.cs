using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>Gold 0x5C3E05..0x5C3FBF's name variant strength, independent of rarity.</summary>
public static class SacredEquipmentNameBonuses
{
    public static int Rank(in SacredEquipmentInstanceLayout values, bool jewelry, SacredEquipmentBonusBalance curves)
    {
        var score = 0;
        for (var i = 0; i < 8; i++)
        {
            var group = values.BonusGroups[i];
            var code = (ushort)group;
            if (code == 0) continue;
            var family = SacredEquipmentMagicBonusSelection.Family(code);
            if (family is 0 or 1)
            {
                var curve = curves.Get(code);
                var factor = jewelry ? curve.JewelryNormalizationFactor : 1d;
                uint strength = 0;
                if ((group & 0xFF0000) != 0)
                    strength = Round(values.BonusValues[i] * factor * 100d / curve.PercentageMaximum + .5d);
                else if (curve.LevelScaling < (double)1e-5f)
                    strength = Round(values.BonusValues[i] * factor * 100d / curve.FlatMaximum + .5d);
                // The native masks rarity bits, then compares them to difficulty
                // constants outside that mask. Those branches never apply.
                if ((byte)values.BonusTypes[i] is not (0 or 1)) strength >>= 3;
                score = unchecked((int)(long)(score + Math.Max(1d, strength * (double).04f + .5d)));
            }
            else if (code is > 839 and < 871) score++;
        }
        return Math.Min(score, 5);
    }

    private static uint Round(double value) => !double.IsFinite(value) || value >= 9223372036854775808d || value < -9223372036854775808d
        ? 0 : unchecked((uint)(long)value);
}
