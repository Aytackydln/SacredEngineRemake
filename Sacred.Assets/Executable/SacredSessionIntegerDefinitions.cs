using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

/// <summary>Follow importer getter calls to their independent keys/defaults.
/// The inspected installed pureHD importer has no ASE branch; it is guarded,
/// rather than inventing a getter call/default for that executable variant.</summary>
public static class SacredSessionIntegerDefinitions
{
    public static SacredIntegerSettingDefinition Read(byte[] image, SacredSessionIntegerSetting setting)
    {
        _ = SacredSessionImportDefinitions.Read(image);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders; var imageBase = headers.PEHeader!.ImageBase;
        var importer = image.AsSpan().IndexOf(SacredExecutableSessionImportLayout.Signature) - SacredExecutableSessionImportLayout.SignatureOffset;
        var gold = image[importer + SacredExecutableSessionIntegerLayout.GoldSpeedCall] == 0xe8;
        var call = setting switch
        {
            SacredSessionIntegerSetting.LobbyPort => gold ? SacredExecutableSessionIntegerLayout.GoldLobbyPortCall : SacredExecutableSessionIntegerLayout.PureHdLobbyPortCall,
            SacredSessionIntegerSetting.Speed => gold ? SacredExecutableSessionIntegerLayout.GoldSpeedCall : SacredExecutableSessionIntegerLayout.PureHdSpeedCall,
            SacredSessionIntegerSetting.AsePort when gold => SacredExecutableSessionIntegerLayout.GoldAsePortCall,
            _ => throw new InvalidDataException("Setting is not called by this inspected importer layout.")
        };
        var caller = importer + call;
        if (image[caller] != 0xe8) throw new InvalidDataException("Unknown original integer-getter call.");
        var getter = checked((uint)((long)Address(caller) + 5 + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(caller + 1))));
        var key = Key(SacredExecutableSessionIntegerLayout.ReadKeyOperand);
        if (setting == SacredSessionIntegerSetting.AsePort)
        {
            if (!At(getter + SacredExecutableSessionIntegerLayout.AseZeroReturnOffset, 2).SequenceEqual<byte>([0x33, 0xc0]))
                throw new InvalidDataException("Unknown original missing ASE-port return.");
            return new(key, default, null, 0);
        }
        var defaultKey = Key(SacredExecutableSessionIntegerLayout.DefaultKeyOperand);
        var push = getter + SacredExecutableSessionIntegerLayout.DefaultPushOffset;
        uint write; int returnOffset;
        switch (At(push, 1)[0])
        {
            case 0x68: write = Word(push + 1); returnOffset = SacredExecutableSessionIntegerLayout.Imm32ReturnOperand; break;
            case 0x6a: write = unchecked((uint)(sbyte)At(push + 1, 1)[0]); returnOffset = SacredExecutableSessionIntegerLayout.Imm8ReturnOperand; break;
            default: throw new InvalidDataException("Unknown original default-write instruction.");
        }
        if (At(getter + (uint)returnOffset - 1, 1)[0] != 0xb8) throw new InvalidDataException("Unknown original default-return instruction.");
        return new(key, defaultKey, write, Word(getter + (uint)returnOffset));

        uint Word(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(At(address, 4));
        byte[] Key(int operand)
        {
            if (At(getter + (uint)operand - 1, 1)[0] != 0xbf) throw new InvalidDataException("Unknown original integer-setting key instruction.");
            var address = Word(getter + (uint)operand); uint length = 0;
            for (uint index = 0; index < 4096; index++)
            {
                if (At(checked(address + index), 1)[0] != 0) continue;
                length = index; break;
            }
            if (At(checked(address + length), 1)[0] != 0) throw new InvalidDataException("Original setting key exceeds compared bounds.");
            var endOperand = getter + (uint)(operand + SacredExecutableSessionIntegerLayout.KeyEndBaseOperandDistance);
            var beginOperand = getter + (uint)(operand + SacredExecutableSessionIntegerLayout.KeyRangeStartOperandDistance);
            if (!At(endOperand - 2, 2).SequenceEqual<byte>([0x81, 0xc1]) || At(beginOperand - 1, 1)[0] != 0x68)
                throw new InvalidDataException("Unknown original integer-setting key range.");
            var begin = Word(beginOperand); var end = checked(Word(endOperand) + length);
            if (end < begin || end - begin > 4096) throw new InvalidDataException("Original key range exceeds compared bounds.");
            return At(begin, (int)(end - begin)).ToArray();
        }
        uint Address(int position)
        {
            foreach (var section in headers.SectionHeaders)
                if (position >= section.PointerToRawData && position - section.PointerToRawData < section.SizeOfRawData)
                    return checked((uint)(imageBase + (uint)section.VirtualAddress + (uint)(position - section.PointerToRawData)));
            throw new InvalidDataException("Original caller lies outside stored sections.");
        }
        ReadOnlySpan<byte> At(uint address, int count)
        {
            if (address < imageBase) throw new InvalidDataException("Operand precedes original image.");
            var rva = (ulong)address - imageBase;
            foreach (var section in headers.SectionHeaders)
            {
                if (rva < (uint)section.VirtualAddress) continue;
                var relative = rva - (uint)section.VirtualAddress;
                if (relative >= (uint)section.SizeOfRawData || count > (long)section.SizeOfRawData - (long)relative) continue;
                var at = checked(section.PointerToRawData + (int)relative);
                if (at < 0 || at > image.Length - count) break;
                return image.AsSpan(at, count);
            }
            throw new InvalidDataException("Original integer operand exceeds stored section bounds.");
        }
    }

    public static SacredIntegerSettingDefinition Load(string directory, SacredSessionIntegerSetting setting)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path), setting); } catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original session integer definition unavailable.", failure);
    }
}
