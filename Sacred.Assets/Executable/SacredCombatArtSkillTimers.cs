using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Assets.Executable;

public sealed record SacredCombatArtSkillTimer(byte Skill, IReadOnlySet<ushort> Codes, SacredSkillCurveLayout Curve, bool ChangesCastingSpeed);

/// <summary>Direct 566070 -> 565DC0 skill families. Reads code lists and curve
/// addresses from original instructions; no copied combat-art ID catalogue.</summary>
public sealed class SacredCombatArtSkillTimers
{
    private readonly Dictionary<byte, SacredCombatArtSkillTimer> _families;
    private SacredCombatArtSkillTimers(Dictionary<byte, SacredCombatArtSkillTimer> families) => _families = families;
    public SacredCombatArtSkillTimer? Family(byte skill) => _families.GetValueOrDefault(skill);

    public static SacredCombatArtSkillTimers Read(byte[] image, byte[] balance, IReadOnlySet<byte> requiredSkills)
    {
        ReadOnlySpan<byte> prefix = [0x81,0xE1,0xFF,0xFF,0,0,0x49,0x83,0xF9,0x20,0x0F,0x87];
        var found = -1;
        for (var at = 0; at < image.Length - 23; at++)
        {
            if (!image.AsSpan(at).StartsWith(prefix) || !image.AsSpan(at + 16, 3).SequenceEqual<byte>([0xFF,0x24,0x8D])) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original passive-art switch.");
            found = at;
        }
        if (found < 0) throw new InvalidDataException("Original passive-art switch unavailable.");
        var native = new SacredNativeTypeSwitch(image);
        var table = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(found + 19));
        // Recover the relocated Balance destination through skill 2's already
        // mapped Weapon Lore divisor, rather than assuming Gold's data address.
        var weaponLore = BinaryPrimitives.ReadUInt32LittleEndian(native.At(table + 4, 4));
        var weaponCode = native.At(weaponLore, 160);
        var divide = weaponCode.IndexOf<byte>([0xD8,0x35]);
        if (divide < 0) throw new InvalidDataException("Original Weapon Lore curve base unavailable.");
        var nativeBalanceGlobals = checked(BinaryPrimitives.ReadUInt32LittleEndian(weaponCode[(divide + 2)..]) -
            (uint)Marshal.OffsetOf<SacredCreatureSkillBalanceLayout>(nameof(SacredCreatureSkillBalanceLayout.WeaponLoreDamage)).ToInt32() - 4);
        var families = new Dictionary<byte, SacredCombatArtSkillTimer>();
        for (byte skill = 1; skill <= 33; skill++)
        {
            if (!requiredSkills.Contains(skill)) continue;
            var entry = BinaryPrimitives.ReadUInt32LittleEndian(native.At(table + (uint)(skill - 1) * 4, 4));
            var code = native.At(entry, 1100);
            if (!code.StartsWith<byte>([0xF6,0xDA,0xA1])) continue; // Other bodies require their own scalar/UI contract.
            var curveBase = BinaryPrimitives.ReadUInt32LittleEndian(code[9..]);
            var stores = new List<(int Offset, uint Value)>();
            var cast = false;
            var terminated = false;
            for (var cursor = 0; cursor < 1090;)
            {
                var instruction = code[cursor..];
                if (instruction.StartsWith<byte>([0xC7,0x84,0x24]))
                {
                    stores.Add((BinaryPrimitives.ReadInt32LittleEndian(instruction[3..]), BinaryPrimitives.ReadUInt32LittleEndian(instruction[7..])));
                    cursor += 11;
                }
                else if (instruction[0] == 0xE8)
                {
                    terminated = true; break;
                }
                else if (instruction.StartsWith<byte>([0x6A])) { cast = instruction[1] != 0; cursor += 2; }
                else if (instruction.StartsWith<byte>([0xF6,0xDA]) || instruction.StartsWith<byte>([0x1B,0xD2]) ||
                         instruction.StartsWith<byte>([0x23,0xD3]) || instruction.StartsWith<byte>([0x8B,0xCD])) cursor += 2;
                else if (instruction[0] is 0xA1 or 0x68) cursor += 5;
                else if (instruction.StartsWith<byte>([0x8B,0x0D]) || instruction.StartsWith<byte>([0x8B,0x15]) || instruction.StartsWith<byte>([0x81,0xE6])) cursor += 6;
                else if (instruction.StartsWith<byte>([0x8D,0x94,0x24])) cursor += 7;
                else if (instruction[0] is >= 0x50 and <= 0x57) cursor++;
                else throw new InvalidDataException($"Unknown original direct art-family instruction for skill {skill} at {entry + cursor:X}.");
            }
            if (!terminated || stores.Count < 2 || stores[^1].Value != 0 ||
                stores.Skip(1).Where((row, index) => row.Offset != stores[index].Offset + 4).Any())
                throw new InvalidDataException("Original direct art-family list is not a terminated contiguous array.");
            var offset = checked((int)(curveBase - nativeBalanceGlobals));
            var globalSize = BinaryPrimitives.ReadUInt32LittleEndian(balance);
            if (offset < 0 || offset + SacredSkillCurveLayout.Size > globalSize || globalSize > balance.Length - 4)
                throw new InvalidDataException("Original passive-art curve lies outside Balance.bin globals.");
            var curve = MemoryMarshal.Read<SacredSkillCurveLayout>(balance.AsSpan(4 + offset));
            families.Add(skill, new(skill, stores.Take(stores.Count - 1).Select(row => checked((ushort)row.Value)).ToHashSet(), curve, cast));
        }
        return new(families);
    }
}
