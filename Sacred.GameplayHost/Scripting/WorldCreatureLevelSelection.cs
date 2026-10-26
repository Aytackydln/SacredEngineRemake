using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Native keeps a 32-bit selection while storing only a 16-bit level.
/// Skill selection/ranks use the former, whereas experience tests the stored word.</summary>
public readonly record struct WorldCreatureLevelSelection(uint CalculatedLevel)
{
    public ushort Level => unchecked((ushort)CalculatedLevel);
    public uint Experience => Level < 2 ? 0 : SacredCreatureProgressionRules.ExperienceAtLevel(unchecked((ushort)(CalculatedLevel - 1)));
    public byte ActiveSkillCount => CalculatedLevel < 3 ? (byte)0 : CalculatedLevel < 7 ? (byte)1
        : CalculatedLevel < 21 ? (byte)2 : CalculatedLevel <= 35 ? (byte)3 : (byte)8;
    public byte SkillRank => unchecked((byte)(CalculatedLevel / 3));
}
