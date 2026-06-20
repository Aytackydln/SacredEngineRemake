using System.Buffers.Binary;
using System.Text;

namespace Sacred.Assets.Paks.Models;

internal sealed class ModelsMetadataTable
{
    private const int HeaderSize = 0x118;
    private const int ModelRecordSize = 1194;
    private const int MotionRecordSize = 256;
    private const int NameSize = 32;

    private readonly string[] _motionNames;

    private ModelsMetadataTable(string[] motionNames)
    {
        _motionNames = motionNames;
    }

    public static ModelsMetadataTable Empty { get; } = new([]);

    public static ModelsMetadataTable Load(string path)
    {
        if (!File.Exists(path))
            return Empty;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException("Models.tmp is shorter than its header.");

        var modelCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x10, 4));
        var motionCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x14, 4));
        var motionTableOffset = HeaderSize + (long)modelCount * ModelRecordSize;
        var requiredLength = motionTableOffset + (long)motionCount * MotionRecordSize;
        if (modelCount > int.MaxValue || motionCount > int.MaxValue ||
            motionTableOffset < HeaderSize || requiredLength > bytes.Length)
            throw new InvalidDataException("Models.tmp has invalid model or motion table bounds.");

        var motionNames = new string[checked((int)motionCount)];
        for (var index = 0; index < motionNames.Length; index++)
        {
            var offset = checked((int)motionTableOffset + index * MotionRecordSize);
            motionNames[index] = ReadName(bytes.AsSpan(offset, NameSize));
        }

        return new ModelsMetadataTable(motionNames);
    }

    public bool TryGetMotionName(uint motionIndex, out string name)
    {
        if (motionIndex < _motionNames.Length &&
            !string.IsNullOrWhiteSpace(_motionNames[motionIndex]))
        {
            name = _motionNames[motionIndex];
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? bytes[..end] : bytes);
    }
}
