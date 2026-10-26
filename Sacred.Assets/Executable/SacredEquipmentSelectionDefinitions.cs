using System.Buffers.Binary;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Executable;

/// <summary>Resource-specific exclusions and modifier redirection ranges, read
/// from the installed Gold instructions instead of hardcoded into file logic.</summary>
public sealed class SacredEquipmentSelectionDefinitions
{
    private SacredEquipmentSelectionDefinitions(SacredEquipmentSelectionPoolRules poolRules,
        SacredEquipmentModifierRedirectRules modifierRedirects)
        => (PoolRules, ModifierRedirects) = (poolRules, modifierRedirects);

    public SacredEquipmentSelectionPoolRules PoolRules { get; }
    public SacredEquipmentModifierRedirectRules ModifierRedirects { get; }

    public static SacredEquipmentSelectionDefinitions Read(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> poolPattern = [0x81, 0xFE, 0, 0, 0, 0, 0x7F, 0x18,
            0x81, 0xFE, 0, 0, 0, 0, 0x7D, 0x71, 0x81, 0xFE, 0, 0, 0, 0, 0x7C, 8,
            0x81, 0xFE, 0, 0, 0, 0, 0x7E, 0x61];
        ReadOnlySpan<byte> redirectPattern = [0x81, 0xFD, 0, 0, 0, 0, 0x7E, 8,
            0x81, 0xFD, 0, 0, 0, 0, 0x7C, 0x10, 0x81, 0xFD, 0, 0, 0, 0, 0x7E, 0x48,
            0x81, 0xFD, 0, 0, 0, 0, 0x7D, 0x40];
        ReadOnlySpan<byte> scanPattern = [0x46, 0x81, 0xFE, 0, 0, 0, 0, 0x0F, 0x8C, 0, 0, 0, 0, 0x8B, 0x74, 0x24, 0x3C];
        var pool = Find(image, poolPattern, [2, 10, 18, 26]);
        var redirects = Find(image, redirectPattern, [2, 10, 18, 26]);
        var scan = Find(image, scanPattern, [3, 9]);
        var rules = new SacredEquipmentModifierRedirectRules(At(image, redirects + 2), At(image, redirects + 10), At(image, redirects + 18), At(image, redirects + 26));
        var poolRules = new SacredEquipmentSelectionPoolRules(At(image, scan + 3), At(image, pool + 10), At(image, pool + 2), At(image, pool + 18), At(image, pool + 26));
        if (rules.FirstMinimum >= rules.FirstMaximum || rules.SecondMinimum >= rules.SecondMaximum ||
            poolRules.MaximumResourceExclusive == 0 || poolRules.FirstExcludedMinimum > poolRules.FirstExcludedMaximum ||
            poolRules.SecondExcludedMinimum > poolRules.SecondExcludedMaximum)
            throw new InvalidDataException("Invalid original equipment selection ranges.");
        return new(poolRules, rules);
    }

    private static uint At(ReadOnlySpan<byte> image, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(image[offset..]);

    private static int Find(ReadOnlySpan<byte> image, ReadOnlySpan<byte> pattern, ReadOnlySpan<int> operands)
    {
        var found = -1;
        for (var at = 0; at <= image.Length - pattern.Length; at++)
        {
            if (image[at] != pattern[0] || image[at + 1] != pattern[1]) continue;
            var match = true;
            for (var i = 0; i < pattern.Length; i++)
            {
                var operand = false;
                foreach (var offset in operands) if (i >= offset && i < offset + 4) { operand = true; break; }
                if (!operand && image[at + i] != pattern[i]) { match = false; break; }
            }
            if (!match) continue;
            if (found >= 0) throw new InvalidDataException("Ambiguous original equipment selection ranges.");
            found = at;
        }
        return found >= 0 ? found : throw new InvalidDataException("Original equipment selection ranges unavailable.");
    }

    public static SacredEquipmentSelectionDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment selection definitions unavailable.", failure);
    }
}
