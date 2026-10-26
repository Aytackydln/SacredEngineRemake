using System.Buffers.Binary;
using System.Text;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Assets.Executable;

public static class SacredEquipmentSelectionClassGateDefinitions
{
    public static SacredEquipmentClassGateRules Read(byte[] image)
    {
        ReadOnlySpan<byte> prefix = [0x8B, 0x4C, 0x24, 0x10, 0x51, 0x8B, 0x4C, 0x24, 0x24, 0x6A];
        var at = image.AsSpan().IndexOf(prefix);
        if (at < 0 || at > image.Length - 0x59 || image.AsSpan(at + prefix.Length).IndexOf(prefix) >= 0)
            throw new InvalidDataException("Unique original equipment class gate unavailable.");
        var native = new SacredNativeTypeSwitch(image);
        var callAddress = native.Address(at + 11);
        var target = unchecked((ulong)((long)callAddress + 5 + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 12))));
        var cutoff = native.At(target + 0x1ED, 5);
        if (!cutoff[..2].SequenceEqual<byte>([0x83, 0xFE]) || !cutoff[3..].SequenceEqual<byte>([0x7C, 0x0C]))
            throw new InvalidDataException("Unsupported original equipment variant cutoff.");
        var markerAddress = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 54));
        var marker = native.At(markerAddress, 64);
        var end = marker.IndexOf((byte)0);
        if (end <= 0) throw new InvalidDataException("Invalid original equipment class marker.");
        return ReadCode(image.AsSpan(at, 0x59), cutoff[2], Encoding.Latin1.GetString(marker[..end]));
    }

    public static SacredEquipmentClassGateRules ReadCode(ReadOnlySpan<byte> code, uint cutoff, string marker)
    {
        if (code.Length < 0x59 || code[9] != 0x6A || code[11] != 0xE8 || code[29] != 0x6A || code[31] != 0xE8 ||
            !code.Slice(16, 4).SequenceEqual<byte>([0x85, 0xC0, 0x74, 0x17]) || code[36] != 0x3D || code[53] != 0x68 ||
            !code.Slice(41, 2).SequenceEqual<byte>([0x75, 0x2E]) ||
            unchecked(16 + BinaryPrimitives.ReadInt32LittleEndian(code[12..])) != unchecked(36 + BinaryPrimitives.ReadInt32LittleEndian(code[32..])) ||
            code[10] >= cutoff || code[30] >= cutoff || string.IsNullOrEmpty(marker))
            throw new InvalidDataException("Unsupported original equipment class-gate instructions.");
        return new(code[10], code[30], cutoff, marker);
    }

    public static SacredEquipmentClassGateRules Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment class gate unavailable.", failure);
    }
}
