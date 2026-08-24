using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Sacred.Assets.Utils;
using Sacred.Core.Pak.Texture;
using Sacred.Core.Utils;

namespace Sacred.Assets.Paks.Texture;

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
        using var stopwatch = new LoggingStopwatch("Loading Texture.pak... ");
 
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

    public bool TryResolveTextureName(string textureName, out string resolvedName)
    {
        if (TryFindTexture(textureName, out var indexedRecord))
        {
            resolvedName = indexedRecord.Record.Name;
            return true;
        }

        resolvedName = string.Empty;
        return false;
    }

    public bool TryGetTextureName(uint entryId, out string textureName)
    {
        if (_recordsByEntryId.TryGetValue(entryId, out var indexedRecord))
        {
            textureName = indexedRecord.Record.Name;
            return true;
        }

        textureName = string.Empty;
        return false;
    }

    public bool TryResolveTextureRecord(string textureName, out TexturePakRecord record)
    {
        if (TryFindTexture(textureName, out var indexedRecord))
        {
            record = indexedRecord.Record;
            return true;
        }

        record = default;
        return false;
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
        var payload = new byte[record.Size];
        await ReadExactlyAtAsync(
                archive.Stream.SafeFileHandle,
                payload,
                payloadOffset,
                cancellationToken)
            .ConfigureAwait(false);

        return TexturePakDecoder.Decode(record, payload);
    }

    private static async Task ReadExactlyAtAsync(
        SafeFileHandle handle,
        Memory<byte> destination,
        long fileOffset,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var bytesRead = await RandomAccess.ReadAsync(
                    handle,
                    destination[totalRead..],
                    fileOffset + totalRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
                throw new EndOfStreamException("The texture payload ended before its indexed size.");

            totalRead += bytesRead;
        }
    }

    private void Index(PakStream archive)
    {
        var stream = archive.Stream;
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: true);
        var header = reader.ReadStruct<TexturePakHeaderLayout>(TexturePakHeaderLayout.SerializedSize);
        header.ValidateSignature();
        var count = TexturePakDecoder.ReadEntryCount(header.EntryCount, header.EntryCount16, stream.Length);
        var descriptors = PakDataHelpers.ReadEntryDescriptors(stream, count, Path.GetFileName(archive.Path));

        for (var i = 0; i < count; i++)
        {
            var descriptor = descriptors[i];
            var offset = descriptor.Offset;
            var size = descriptor.Size;
            if (offset <= 0 || size <= 0 || offset > int.MaxValue || size > int.MaxValue)
                continue;

            if (offset + TexturePakDecoder.TextureHeaderSize > stream.Length)
                continue;

            stream.Position = offset;
            var textureHeader = reader.ReadStruct<TexturePakEntryHeaderLayout>(TexturePakEntryHeaderLayout.SerializedSize);
            var textureHeaderBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref textureHeader, 1));

            var name = ReadCString(textureHeaderBytes, 0x20);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var indexedRecord = new IndexedTexturePakRecord(
                archive,
                new TexturePakRecord(
                    name,
                    offset,
                    (int)size,
                    textureHeader.Width,
                    textureHeader.Height,
                    (byte)textureHeader.StorageFormat));

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

        public void Dispose() => Stream.Dispose();
    }

    private readonly record struct IndexedTexturePakRecord(PakStream Archive, TexturePakRecord Record);
}
