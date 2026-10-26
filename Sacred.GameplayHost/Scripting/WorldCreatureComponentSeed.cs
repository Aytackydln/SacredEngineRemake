using System.Buffers.Binary;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Non-hero component's explicitly seeded value inputs (Gold 0x55E1B0 /
/// 0x586C00). The result block still requires CalcResults; this buffer is not a
/// completed actor or a full-values publication.</summary>
public static class WorldCreatureComponentSeed
{
    public static byte[] Create(uint type, SacredCreatureTemplate? template)
    {
        if (template?.Class == SacredCreatureClass.Hero)
            throw new ArgumentException("Hero construction requires native learned combat arts and hero initialization.", nameof(template));
        var values = new byte[SacredActorValuesLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(values, type);
        // Seed copies Basis in archive order. Current attributes use a different
        // permutation; CalcResults later reads Basis in runtime attribute order.
        byte[] basis = template is null ? [25, 15, 20, 20, 10, 5] :
            [template.Strength, template.Dexterity, template.PhysicalRegeneration,
                template.Endurance, template.Charisma, template.MentalRegeneration];
        for (var i = 0; i < basis.Length; i++) WriteWord(66 + i * 2, basis[i]);
        // Missing-template seed leaves current attributes at constructor zero.
        if (template is not null)
        {
            byte[] current = [template.Strength, template.Endurance, template.Dexterity,
                template.PhysicalRegeneration, template.MentalRegeneration, template.Charisma];
            for (var i = 0; i < current.Length; i++) WriteWord(8 + i * 2, current[i]);
            for (var i = 0; i < 8; i++)
            {
                var skill = i < 2 ? template.StartingSkills[i] : template.AvailableSkills[i - 2];
                values[28 + i] = skill;
                if (skill != 0) values[36 + i] = 1;
            }
        }
        WriteWord(52, 100);
        WriteWord(54, 100);
        WriteWord(56, 130);
        WriteWord(78, 1);
        return values;

        void WriteWord(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(offset), value);
    }
}
