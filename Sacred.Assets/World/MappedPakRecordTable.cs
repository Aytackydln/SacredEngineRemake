using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Sacred.Assets.World;

/// <summary>
/// Reads indexed fixed-size PAK records directly from a read-only mapping. Only pages touched by
/// visible world data enter the working set; the complete archive is never copied to managed RAM.
/// </summary>
internal sealed class MappedPakRecordTable<T> : IDisposable where T : struct
{
    private const int HeaderSize = 0x100;

    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly long _fileLength;
    private readonly int _recordSize;
    private readonly int _count;

    public MappedPakRecordTable(string path, int recordSize, string archiveName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{archiveName} path cannot be empty.", nameof(path));

        _recordSize = recordSize;
        if (Marshal.SizeOf<T>() != recordSize)
            throw new InvalidDataException($"{archiveName} record layout does not match its serialized size.");

        using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _fileLength = stream.Length;
            Span<byte> header = stackalloc byte[8];
            stream.ReadExactly(header);
            _count = ReadEntryCount(header, stream.Length, archiveName);
        }

        _mapping = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            null,
            0,
            MemoryMappedFileAccess.Read);
        _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public T? Get(uint id)
    {
        if (id == 0 || id >= _count)
            return null;

        var descriptorOffset = HeaderSize + (long)id * PakDataHelpers.EntryDescriptorSize;
        var recordOffset = _view.ReadUInt32(descriptorOffset + sizeof(uint));
        if (recordOffset == 0 || recordOffset > _fileLength - _recordSize)
            return null;

        _view.Read(recordOffset, out T record);
        return record;
    }

    public void Dispose()
    {
        _view.Dispose();
        _mapping.Dispose();
    }

    private static int ReadEntryCount(ReadOnlySpan<byte> header, long fileLength, string archiveName)
    {
        if (fileLength < HeaderSize)
            throw new InvalidDataException($"{archiveName} is too small to contain a header.");

        var count32 = BitConverter.ToUInt32(header[4..8]);
        var count16 = BitConverter.ToUInt16(header[4..6]);
        var maxCount = Math.Max(0L, (fileLength - HeaderSize) / PakDataHelpers.EntryDescriptorSize);
        if (count32 <= maxCount && count32 <= int.MaxValue)
            return (int)count32;
        if (count16 <= maxCount)
            return count16;

        throw new InvalidDataException(
            $"Cannot determine {archiveName} entry count. count16={count16}, count32={count32}, max={maxCount}");
    }
}
