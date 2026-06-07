using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sacred.Assets;

public sealed class TexturePakArchive : IDisposable
{
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly PakStream[] _archives;
    private readonly Dictionary<string, IndexedTexturePakRecord> _recordsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, IndexedTexturePakRecord> _recordsByEntryId = new();
    private bool _disposed;

    private TexturePakArchive(string[] paths)
    {
        var archives = new List<PakStream>(paths.Length);
        try
        {
            foreach (var path in paths)
                archives.Add(OpenPakStream(path));

            _archives = archives.ToArray();
            foreach (var archive in _archives)
                Index(archive);
        }
        catch
        {
            foreach (var archive in archives)
                archive.Dispose();
            throw;
        }
    }

    public static TexturePakArchive LoadFromDirectory(string pakDirectory)
    {
        if (string.IsNullOrWhiteSpace(pakDirectory))
            throw new ArgumentException("PAK directory path cannot be empty.", nameof(pakDirectory));
        if (!Directory.Exists(pakDirectory))
            throw new DirectoryNotFoundException($"PAK directory was not found: {pakDirectory}");

        var paths = Directory
            .EnumerateFiles(pakDirectory, "texture*.pak", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => TexturePakSortKey(Path.GetFileNameWithoutExtension(path)))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            throw new FileNotFoundException($"No texture*.pak files were found in '{pakDirectory}'.");

        return new TexturePakArchive(paths);
    }

    public async Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken = default)
    {
        if (!TryFindTexture(textureName, out var indexedRecord))
            throw new FileNotFoundException($"Texture '{textureName}' was not found in: {string.Join(", ", _archives.Select(static archive => Path.GetFileName(archive.Path)))}.");

        return await LoadTextureAsync(indexedRecord, cancellationToken).ConfigureAwait(false);
    }

    public Task<TextureAsset> LoadTextureAsync(uint entryId, CancellationToken cancellationToken = default)
    {
        if (!_recordsByEntryId.TryGetValue(entryId, out var indexedRecord))
            throw new FileNotFoundException($"Texture entry #{entryId} was not found in: {string.Join(", ", _archives.Select(static archive => Path.GetFileName(archive.Path)))}.");

        return LoadTextureAsync(indexedRecord, cancellationToken);
    }

    private async Task<TextureAsset> LoadTextureAsync(
        IndexedTexturePakRecord indexedRecord,
        CancellationToken cancellationToken)
    {
        var archive = indexedRecord.Archive;
        var record = indexedRecord.Record;
        var payloadOffset = record.Offset + TexturePakDecoder.TextureHeaderSize;
        var payloadLength = Math.Min(record.Size, Math.Max(0, archive.Stream.Length - payloadOffset));

        await archive.StreamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            archive.Stream.Position = payloadOffset;
            var payload = new byte[(int)payloadLength];
            await archive.Stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return TexturePakDecoder.Decode(record, payload);
        }
        finally
        {
            archive.StreamLock.Release();
        }
    }

    private void Index(PakStream archive)
    {
        var stream = archive.Stream;
        var header = new byte[TexturePakDecoder.HeaderSize];
        if (stream.Read(header) != header.Length)
            throw new InvalidDataException($"{Path.GetFileName(archive.Path)} is too small to contain a header.");

        var count = TexturePakDecoder.ReadEntryCount(header, stream.Length);
        Span<byte> descriptor = stackalloc byte[TexturePakDecoder.DescriptorSize];
        Span<byte> textureHeader = stackalloc byte[TexturePakDecoder.TextureHeaderSize];

        for (var i = 0; i < count; i++)
        {
            stream.Position = TexturePakDecoder.HeaderSize + i * TexturePakDecoder.DescriptorSize;
            stream.ReadExactly(descriptor);

            var offset = BitConverter.ToUInt32(descriptor[4..8]);
            var size = BitConverter.ToUInt32(descriptor[8..12]);
            if (offset <= 0 || size <= 0 || offset > int.MaxValue || size > int.MaxValue)
                continue;

            if (offset + TexturePakDecoder.TextureHeaderSize > stream.Length)
                continue;

            stream.Position = offset;
            stream.ReadExactly(textureHeader);

            var name = ReadCString(textureHeader, 0x20);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var indexedRecord = new IndexedTexturePakRecord(
                archive,
                new TexturePakRecord(
                    name,
                    offset,
                    (int)size,
                    BitConverter.ToUInt16(textureHeader[0x20..0x22]),
                    BitConverter.ToUInt16(textureHeader[0x22..0x24]),
                    textureHeader[0x24]));

            _recordsByEntryId.TryAdd((uint)i, indexedRecord);
            AddLookupNames(indexedRecord);
        }
    }

    private bool TryFindTexture(string textureName, out IndexedTexturePakRecord indexedRecord)
    {
        if (_recordsByName.TryGetValue(NormalizeName(textureName), out indexedRecord))
            return true;

        var stem = Path.GetFileNameWithoutExtension(textureName);
        var stemWithoutDimensions = StripDimensionSuffix(stem);
        return _recordsByName.TryGetValue(NormalizeName(stem), out indexedRecord) ||
               _recordsByName.TryGetValue(NormalizeName(stemWithoutDimensions), out indexedRecord) ||
               _recordsByName.TryGetValue(NormalizeName(StripNumericSuffix(stemWithoutDimensions)), out indexedRecord);
    }

    private void AddLookupNames(IndexedTexturePakRecord indexedRecord)
    {
        var record = indexedRecord.Record;
        Add(indexedRecord, record.Name);
        Add(indexedRecord, Path.GetFileNameWithoutExtension(record.Name));

        var stem = Path.GetFileNameWithoutExtension(record.Name);
        if (stem?.StartsWith("mix", StringComparison.OrdinalIgnoreCase) == true)
            Add(indexedRecord, stem + ".444");

        var lowerName = record.Name.ToLowerInvariant();
        if (lowerName.StartsWith("iso", StringComparison.Ordinal) &&
            lowerName.EndsWith(".tga", StringComparison.Ordinal) &&
            int.TryParse(lowerName[3..^4], out var number))
        {
            for (var width = 1; width <= 4; width++)
                Add(indexedRecord, number.ToString().PadLeft(width, '0').Insert(0, "iso") + ".tga");
            Add(indexedRecord, $"iso{number}.tga");
        }
    }

    private void Add(IndexedTexturePakRecord indexedRecord, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _recordsByName[NormalizeName(name)] = indexedRecord;
    }

    private static string ReadCString(ReadOnlySpan<byte> data, int maxLength)
    {
        var length = 0;
        var limit = Math.Min(data.Length, maxLength);
        while (length < limit && data[length] != 0)
            length++;

        return NameEncoding.GetString(data[..length]);
    }

    private static string StripDimensionSuffix(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return string.Empty;

        var separator = stem.LastIndexOf('_');
        if (separator <= 0 || separator + 1 >= stem.Length)
            return stem;

        var suffix = stem[(separator + 1)..];
        var x = suffix.IndexOf('x');
        if (x <= 0 || x + 1 >= suffix.Length)
            return stem;

        return int.TryParse(suffix[..x], out _) && int.TryParse(suffix[(x + 1)..], out _)
            ? stem[..separator]
            : stem;
    }

    private static string StripNumericSuffix(string stem)
    {
        var separator = stem.LastIndexOf('_');
        if (separator <= 0 || separator + 1 >= stem.Length)
            return stem;

        return int.TryParse(stem[(separator + 1)..], out _)
            ? stem[..separator]
            : stem;
    }

    private static string NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : name.Replace('\\', '/').Trim().ToLowerInvariant();

    private static int TexturePakSortKey(string? stem)
    {
        if (string.Equals(stem, "texture", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (stem?.StartsWith("texture", StringComparison.OrdinalIgnoreCase) == true &&
            int.TryParse(stem["texture".Length..], out var suffix))
            return suffix;

        return int.MaxValue;
    }

    private static PakStream OpenPakStream(string path) =>
        new(path, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var archive in _archives)
            archive.Dispose();
    }

    private sealed class PakStream(string path, FileStream stream) : IDisposable
    {
        public string Path { get; } = path;
        public FileStream Stream { get; } = stream;
        public SemaphoreSlim StreamLock { get; } = new(1, 1);

        public void Dispose()
        {
            Stream.Dispose();
            StreamLock.Dispose();
        }
    }

    private readonly record struct IndexedTexturePakRecord(PakStream Archive, TexturePakRecord Record);
}
