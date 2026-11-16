using System.Buffers.Binary;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>One main/enabled merged item's CalcResults scalar contributions.
/// Slot traversal, level penalties, set tracking and learned-art effects belong
/// to the caller. Attribute additions stay pending until the native base stage.
/// Returns actor +0x200 flag additions; it does not publish an object.</summary>
public static class SacredCreatureEquipmentContributions
{
    public static uint Apply(byte[] actor, int[] attributeBonuses, byte[] item,
        SacredItemCategory category, SacredCreatureEquipmentContext context)
        => Apply(SacredX87Precision.Extended, actor, attributeBonuses, item, category, context);

    public static uint Apply(SacredX87Precision precision, byte[] actor, int[] attributeBonuses, byte[] item,
        SacredItemCategory category, SacredCreatureEquipmentContext context)
    {
        var fp = new SacredResultArithmetic(precision);
        if (actor.Length != SacredActorValuesLayout.Size || attributeBonuses.Length != 6 || item.Length != SacredEquipmentInstanceLayout.Size)
            throw new ArgumentException("Expected actor, six pending attributes and a concrete item record.");
        if (context.LearnedCombatArtCount != 0) throw new NotSupportedException("Equipment bonuses to learned combat arts require native art-entry effects.");
        if (!context.MergedRecord)
        {
            if (!context.Unarmed || category != SacredItemCategory.Weapon)
                for (var i = 0; i < 4; i++)
                {
                    Add(110 + i * 4, Short(36 + i * 2));
                    Add(126 + i * 4, Short(28 + i * 2));
                }
            for (var i = 0; i < 4; i++) Add(94 + i * 4, Short(50 + i * 2));
            Add(82, fp.Multiply(fp.Multiply(Short(44), context.Profile.EquipmentAttackPercent), 0.01f));
            Add(86, fp.Multiply(fp.Multiply(Short(46), context.Profile.EquipmentDefensePercent), 0.01f));
        }
        uint flags = 0;
        for (var slot = 0; slot < 8; slot++)
        {
            var type = BinaryPrimitives.ReadUInt16LittleEndian(item.AsSpan(58 + slot * 2));
            if ((byte)type is not (0 or 13)) continue;
            var hero = (type >> 8) & 15;
            if (hero is >= 1 and <= 5 && context.ActorResource != hero || hero == 6 && context.ActorResource is not (6 or 7)) continue;
            var group = BinaryPrimitives.ReadUInt32LittleEndian(item.AsSpan(74 + slot * 4));
            var source = (byte)(group >> 16);
            var raw = Short(106 + slot * 2);
            // 0x568C40 multiplies signed magnitude and the word with IMUL before
            // FILD. Precision control first applies to the following FMUL.
            var amount = source is >= 1 and <= 6 ? (float)fp.Multiply(raw * Attribute(source), 0.01f) : raw;
            var code = (ushort)group;
            if (code is >= 801 and <= 804)
            {
                var element = code - 801;
                if ((type & 0x8000) != 0) Add(94 + element * 4, amount);
                else { Add(110 + element * 4, amount); Add(126 + element * 4, amount); }
            }
            else if (code is >= 805 and <= 808)
            {
                var element = code - 805;
                var at = (type & 0x8000) != 0 ? 206 : (type & 0x4000) != 0 ? 158 : (type & 0x2000) != 0 ? 174 : 142;
                Add(at + element * 4, Percent(amount));
            }
            else switch (code)
            {
                case 809: Add(222, Percent(amount)); break;
                case 810: Add(226, Percent(amount)); break;
                case 811: Speed(52, amount); break;
                case 812: Speed(56, amount); break;
                case 813: Add(230, Percent(amount)); break;
                case 814: Add(234, Percent(amount)); break;
                case >= 815 and <= 820:
                    var index = code switch { 815 => 0, 816 => 2, 817 => 1, 818 => 3, 819 => 4, _ => 5 };
                    attributeBonuses[index] = Truncate(fp.Add(attributeBonuses[index], amount));
                    break;
                case 847:
                    for (var i = 0; i < 4; i++) Add(206 + i * 4, Percent(amount));
                    break;
                case 848:
                    Add(222, Percent(amount)); Add(226, Percent(amount));
                    break;
                case 849:
                    for (var i = 0; i < 8; i++) Skill(i, amount);
                    break;
                case 860:
                    for (var i = 0; i < 4; i++) Add(142 + i * 4, Percent(amount));
                    break;
                case 864:
                    flags |= 0x10000000;
                    break;
                default:
                    if (code is >= 600 and < 799)
                        for (var i = 0; i < 8; i++)
                            if (actor[28 + i] == code - 599) { Skill(i, amount); break; }
                    break;
            }
        }
        return flags;

        short Short(int at) => BinaryPrimitives.ReadInt16LittleEndian(item.AsSpan(at));
        ushort Attribute(int source) => BinaryPrimitives.ReadUInt16LittleEndian(actor.AsSpan(source switch
        {
            1 => 8, 2 => 12, 3 => 10, 4 => 14, 5 => 16, _ => 18
        }));
        double Percent(float amount) => fp.Multiply(amount, 0.01f);
        void Add(int at, double amount) => BinaryPrimitives.WriteSingleLittleEndian(actor.AsSpan(at), (float)fp.Add(BinaryPrimitives.ReadSingleLittleEndian(actor.AsSpan(at)), amount));
        void Speed(int at, float amount) => BinaryPrimitives.WriteInt16LittleEndian(actor.AsSpan(at), unchecked((short)Truncate(fp.Add(BinaryPrimitives.ReadInt16LittleEndian(actor.AsSpan(at)), amount))));
        void Skill(int index, float amount) => actor[44 + index] = (byte)Math.Clamp(actor[44 + index] + unchecked((short)Truncate(amount)), 0, 255);
        static int Truncate(double value) => unchecked((int)(long)value);
    }
}
