using System.Buffers.Binary;

namespace Sacred.Assets.Executable;

/// <summary>Read-only extraction of the flag written after a successful native
/// name test. Accepts only bounded branches, flag operations and return epilogues;
/// it neither executes native code nor follows calls.</summary>
internal static class SacredEquipmentClassRuleTrace
{
    public static uint MatchedFlag(ReadOnlySpan<byte> body, int offset)
    {
        uint eax = 1, flags = 0;
        var zero = false;
        for (var step = 0; step < 32; step++)
        {
            if (offset < 0 || offset > body.Length - 3) break;
            var code = body[offset..];
            if (code[..3].SequenceEqual<byte>([0x83, 0xC4, 8])) offset += 3;
            else if (code[..2].SequenceEqual<byte>([0x85, 0xC0])) { zero = eax == 0; offset += 2; }
            else if (code[0] is 0x74 or 0x75) offset += 2 + ((code[0] == 0x74 ? zero : !zero) ? (sbyte)code[1] : 0);
            else if (code[0] == 0x0F && code[1] is 0x84 or 0x85)
                offset += 6 + ((code[1] == 0x84 ? zero : !zero) ? BinaryPrimitives.ReadInt32LittleEndian(code[2..]) : 0);
            else if (code[..3].SequenceEqual<byte>([0x8B, 0x47, 4])) { eax = flags; offset += 3; }
            else if (code[0] == 0x0C) { eax |= code[1]; zero = eax == 0; offset += 2; }
            else if (code[0] == 0x0D) { eax |= BinaryPrimitives.ReadUInt32LittleEndian(code[1..]); zero = eax == 0; offset += 5; }
            else if (code[..3].SequenceEqual<byte>([0x89, 0x47, 4])) { flags = eax; offset += 3; }
            else if (code[..3].SequenceEqual<byte>([0x81, 0x4F, 4])) { flags |= BinaryPrimitives.ReadUInt32LittleEndian(code[3..]); zero = flags == 0; offset += 7; }
            else if (code[0] is >= 0x5B and <= 0x5F) offset++;
            else if (code[0] == 0xB0) { eax = (eax & 0xFFFFFF00) | code[1]; offset += 2; }
            else if (code[..3].SequenceEqual<byte>([0xC2, 4, 0]) && flags != 0 && (flags & (flags - 1)) == 0) return flags;
            else break;
        }
        throw new InvalidDataException("Unsupported original class-name flag branch.");
    }
}
