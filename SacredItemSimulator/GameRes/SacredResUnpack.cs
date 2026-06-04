using System.Buffers;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Text;

namespace SacredItemSimulator.GameRes;

public static class SacredResUnpack
{
    public static FrozenDictionary<uint, string> UnpackAsDictionary(params IEnumerable<string> files)
    {
        return Unpack(files)
            .DistinctBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            .ToFrozenDictionary();
    }
    
    public static IEnumerable<KeyValuePair<uint, string>> Unpack(params IEnumerable<string> files)
    {
        foreach (var fileName in files)
        {
            foreach (var kvp in ReadFile(fileName))
                yield return kvp;
        }
    }
    
    private static IEnumerable<KeyValuePair<uint, string>> ReadFile(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, false);
        using var br = new BinaryReader(fs, Encoding.Unicode);

        var stringCount = br.ReadUInt32();

        if (stringCount == 0)
            yield break;

        // Phase 1: Read index sequentially
        var entries = new List<(uint Id, uint Offset, uint Length)>((int)stringCount);

        var indexEndPosition = fs.Position;

        for (var i = 0; i < stringCount; i++)
        {
            var id = br.ReadUInt32();
            var offset = br.ReadUInt32();
            _ = br.ReadUInt32(); // unknown
            var length = br.ReadUInt32();

            if (length > 0)
                entries.Add((id, offset, length));
        }

        // Phase 2: Read ALL string data in one sequential read
        var dataStart = fs.Position;
        var totalDataSize = fs.Length - dataStart;

        if (totalDataSize <= 0)
            throw new InvalidDataException("No string data found after index.");

        var rented = ArrayPool<byte>.Shared.Rent((int)totalDataSize);
        try
        {
            fs.ReadExactly(rented, 0, (int)totalDataSize);
            var dataSpan = rented.AsMemory(0, (int)totalDataSize);

            // Phase 3: Build list

            foreach (var (id, offset, length) in entries)
            {
                // Original code used: offset + 4
                long absoluteOffset = offset + 4;

                if (absoluteOffset < indexEndPosition)
                    absoluteOffset = absoluteOffset - indexEndPosition + dataStart; // fallback adjustment

                var relativePos = (int)(absoluteOffset - dataStart);

                if (relativePos < 0 || relativePos + length > dataSpan.Length)
                    continue; // safety - skip bad entries

                var stringSpan = dataSpan.Slice(relativePos, (int)length);

                // Convert UTF-16 bytes directly to string using MemoryMarshal (very fast)
                var value = MemoryMarshal.Cast<byte, char>(stringSpan.Span).ToString();

                yield return new KeyValuePair<uint, string>(id, value);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}