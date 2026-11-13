using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Gold 0x5CECD0 shared-Weapon hand-refresh predicate. Resource
/// exceptions and usage values are extracted from original instructions.</summary>
public sealed class SacredWeaponHandRefreshDefinitions
{
    private readonly byte[] _usages;
    private readonly uint[] _resources;
    private SacredWeaponHandRefreshDefinitions(byte[] usages, uint[] resources) =>
        (_usages, _resources) = (usages, resources);

    public bool RequiresRefresh(uint type, byte? usage, uint baseItem)
    {
        if (usage is null) return false;
        return _usages.Contains(usage.Value) || _resources.Contains(baseItem == 0 ? type : baseItem);
    }

    public static SacredWeaponHandRefreshDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> prefix = [0x8B, 0x48, 0x24, 0x8B, 0x76, 0x10, 0x85, 0xC9, 0x74, 2,
            0x8B, 0xF1, 0x8A, 0x40, 0x1E];
        var found = -1;
        for (var i = 0; i <= image.Length - prefix.Length - 76; i++)
        {
            if (!image.Slice(i, prefix.Length).SequenceEqual(prefix)) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original Weapon hand-refresh predicate.");
            found = i;
        }
        if (found < 0) throw new InvalidDataException("Original Weapon hand-refresh predicate unavailable.");
        var code = image[(found + prefix.Length)..];
        var usages = new byte[5];
        var resources = new uint[6];
        // All short usage branches and the first five resource branches
        // converge on the true return; the last resource branches to false.
        for (var i = 0; i < usages.Length; i++)
        {
            var at = i * 4;
            if (code[at] != 0x3C || code[at + 2] != 0x74 || at + 4 + (sbyte)code[at + 3] != 68)
                throw new InvalidDataException("Unknown original Weapon usage comparison.");
            usages[i] = code[at + 1];
        }
        for (var i = 0; i < resources.Length; i++)
        {
            var at = 20 + i * 8;
            if (code[at] != 0x81 || code[at + 1] != 0xFE || code[at + 6] != (i == 5 ? 0x75 : 0x74) ||
                at + 8 + (sbyte)code[at + 7] != (i == 5 ? 72 : 68))
                throw new InvalidDataException("Unknown original Weapon resource comparison.");
            resources[i] = BinaryPrimitives.ReadUInt32LittleEndian(code[(at + 2)..]);
        }
        ReadOnlySpan<byte> returns = [0xB0, 1, 0x5E, 0xC3, 0x32, 0xC0, 0x5E, 0xC3];
        if (!code.Slice(68, returns.Length).SequenceEqual(returns))
            throw new InvalidDataException("Unknown original Weapon hand-refresh return.");
        return new(usages, resources);
    }
}
