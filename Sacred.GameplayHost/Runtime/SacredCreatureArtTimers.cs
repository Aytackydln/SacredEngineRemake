using System.Buffers.Binary;
using Sacred.Assets.Executable;
using Sacred.Core.CombatArts;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold 571080 base timers and direct 565DC0 skill curves, in PC=24.
/// Current container casting-speed effects and equipment art bonuses remain
/// separate. Every record is private to this calculation.</summary>
public sealed class SacredCreatureArtTimers
{
    private readonly SacredCombatArtSkillTimers _skills;
    internal SacredCreatureArtTimers(IEnumerable<byte[]> source, SacredCombatArtTimerDefinitions definitions, SacredCombatArtSkillTimers skills)
    {
        _skills = skills;
        Records = source.Select(row => row.ToArray()).ToArray();
        var fp = new SacredResultArithmetic(SacredX87Precision.Single);
        foreach (var record in Records)
        {
            if (record.Length != SacredCombatArtRuntimeLayout.Size) throw new InvalidDataException("Expected a complete acquired-art record.");
            record[7] = 0; // 579CBA removes old equipment rank before the equipment traversal.
            BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(14), 1);
            var code = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4));
            double timer;
            if (definitions.TryGetMagic(code, out var magic)) timer = magic.RegenerationBase + record[6] * magic.RegenerationPerLevel;
            else if (definitions.TryGetSpell(code, out var spell)) timer = fp.Add(fp.Multiply(record[6], spell.RegenerationPerLevel), spell.RegenerationBase);
            else throw new NotSupportedException($"Acquired art {code} has no original timer definition.");
            BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(10), (float)timer);
        }
    }

    public IReadOnlyList<byte[]> Records { get; }
    public bool ApplySkill(byte skill, int rank)
    {
        var family = _skills.Family(skill);
        if (family is null) return false;
        if (family.ChangesCastingSpeed) throw new NotSupportedException("This art skill also requires its actual current-container casting-speed query.");
        var fp = new SacredResultArithmetic(SacredX87Precision.Single);
        var curve = family.Curve;
        if (!float.IsFinite(curve.Divisor) || curve.Divisor == 0) throw new InvalidDataException("Invalid original art skill curve.");
        var approach = fp.Subtract(1, fp.Divide(1, fp.Add(fp.Divide(rank - 1, curve.Divisor), 1)));
        var amount = fp.Multiply(fp.Subtract(curve.Limit, curve.Base), approach);
        var percent = (float)fp.Add(fp.Add(amount, amount), curve.Base);
        var denominator = fp.Add(fp.Multiply(percent, .01f), 1);
        foreach (var record in Records)
        {
            if (!family.Codes.Contains(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4)))) continue;
            var timer = (float)fp.Divide(BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(10)), denominator);
            if (timer < 1) timer = 1;
            BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(10), timer);
            if (BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(18)) > timer)
                BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(18), timer);
        }
        return true;
    }
}
