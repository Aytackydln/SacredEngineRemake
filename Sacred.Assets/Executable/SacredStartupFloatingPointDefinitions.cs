using System.Buffers.Binary;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Gold main initialization and reset callbacks explicitly call
/// _control87(value, 0x30000). Keep the selected precision in original-image
/// definitions instead of assuming the CRT/default fixture's floating mode.</summary>
public sealed class SacredStartupFloatingPointDefinitions
{
    private SacredStartupFloatingPointDefinitions(SacredX87Precision precision, int sites) => (Precision, SiteCount) = (precision, sites);
    public SacredX87Precision Precision { get; }
    public int SiteCount { get; }

    public static SacredStartupFloatingPointDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> prefix = [0x68, 0, 0, 3, 0, 0x68];
        SacredX87Precision? selected = null;
        var sites = 0;
        for (var search = 0; search <= image.Length - 15;)
        {
            var relative = image.AsSpan(search).IndexOf(prefix);
            if (relative < 0) break;
            var at = search + relative;
            search = at + prefix.Length;
            if (at > image.Length - 15 || image[at + 10] != 0xE8) continue;
            // A following call alone is not sufficient: validate the original
            // control-word getter/setter body reached by that relative operand.
            var reader = new SacredNativeTypeSwitch(image);
            var target = unchecked(reader.Address(at + 15) + (ulong)(long)BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 11)));
            if (!reader.At(checked((uint)target), 10).SequenceEqual<byte>([0x55, 0x8B, 0xEC, 0x51, 0x56, 0x9B, 0xD9, 0x7D, 0xFC, 0xFF])) continue;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 6));
            var precision = (value & 0x30000) switch
            {
                0 => SacredX87Precision.Extended,
                0x10000 => SacredX87Precision.Double,
                0x20000 => SacredX87Precision.Single,
                _ => throw new InvalidDataException("Unsupported original startup precision selector.")
            };
            if (selected.HasValue && selected != precision) throw new InvalidDataException("Conflicting original startup precision selections.");
            selected = precision;
            sites++;
        }
        if (!selected.HasValue || sites < 2) throw new InvalidDataException("Original startup precision call sites unavailable.");
        return new(selected.Value, sites);
    }
}
