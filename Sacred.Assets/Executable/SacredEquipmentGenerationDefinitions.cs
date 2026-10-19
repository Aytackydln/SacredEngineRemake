using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Original authored-bonus combat-art class getter, reached from the
/// installed generation routine. Evaluates bounded constant-return instructions only.</summary>
public sealed class SacredEquipmentGenerationDefinitions
{
    private readonly SacredNativeTypeSwitch _switch;
    private readonly ulong _combatArtClass;
    private SacredEquipmentGenerationDefinitions(SacredNativeTypeSwitch getter, ulong combatArtClass)
        => (_switch, _combatArtClass) = (getter, combatArtClass);
    public byte CombatArtClass(uint code) => checked((byte)_switch.Read(_combatArtClass, code));

    public static SacredEquipmentGenerationDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> anchor = [0x81, 0xE1, 0x3F, 0, 0xC0, 0, 0x81, 0xF9, 0x3F, 0, 0xC0, 0,
            0x75, 0x28, 0x8B, 0x54, 0x9D, 0x4A, 0x52, 0xE8];
        var at = image.AsSpan().IndexOf(anchor);
        if (at < 0 || at > image.Length - 32 || image.AsSpan(at + anchor.Length).IndexOf(anchor) >= 0 ||
            !image.AsSpan(at + 24, 5).SequenceEqual<byte>([0x83, 0xC4, 4, 0x85, 0xC0]))
            throw new InvalidDataException("Original authored-bonus class call not found.");
        var getter = new SacredNativeTypeSwitch(image);
        var target = unchecked((ulong)((long)getter.Address(at + 24) + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 20))));
        if (getter.Read(target, 0) != 0) throw new InvalidDataException("Unknown authored-bonus class getter.");
        return new(getter, target);
    }

    public static SacredEquipmentGenerationDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment generation definitions unavailable.", failure);
    }
}
