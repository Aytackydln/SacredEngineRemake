using System.Collections.ObjectModel;
using System.Text;
using Sacred.Assets.Utils;
using Sacred.Core.Pak.Sound;
using Sacred.Core.Utils;

namespace Sacred.Assets.Paks.Sound;

/// <summary>Indexed, random-access reader for Sacred's sparse Sound.pak archive.</summary>
public sealed class SoundPakArchive : IDisposable
{
    private readonly FileStream _stream;
    private readonly Dictionary<uint, SoundPakRecord> _recordsById;
    private bool _disposed;

    private SoundPakArchive(FileStream stream, Dictionary<uint, SoundPakRecord> recordsById)
    {
        _stream = stream;
        _recordsById = recordsById;
        Records = new ReadOnlyCollection<SoundPakRecord>(
            recordsById.Values.OrderBy(static record => record.SoundId).ToArray());
    }

    public IReadOnlyList<SoundPakRecord> Records { get; }

    public static SoundPakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading Sound.pak index... ");

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Sound PAK path cannot be empty.", nameof(path));

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.RandomAccess);

        try
        {
            using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: true);
            var header = reader.ReadStruct<SoundPakHeaderLayout>(SoundPakHeaderLayout.SerializedSize);
            header.ValidateSignature();
            if (header.EntryCount > int.MaxValue)
                throw new InvalidDataException($"Sound.pak has too many descriptor slots: {header.EntryCount}.");

            var descriptors = PakDataHelpers.ReadEntryDescriptors(
                stream,
                (int)header.EntryCount,
                Path.GetFileName(path));
            var records = new Dictionary<uint, SoundPakRecord>();

            for (var id = 0; id < descriptors.Length; id++)
            {
                var descriptor = descriptors[id];
                if (descriptor.Type == 0 && descriptor.Offset == 0 && descriptor.Size == 0)
                    continue;

                if (!Enum.IsDefined(typeof(SacredSoundStorageFormat), descriptor.Type))
                    throw new InvalidDataException(
                        $"Sound #{id} uses unknown storage type {descriptor.Type}.");
                if (descriptor.Offset == 0 || descriptor.Size == 0 ||
                    descriptor.Offset > stream.Length || descriptor.Size > int.MaxValue ||
                    (ulong)descriptor.Offset + descriptor.Size > (ulong)stream.Length)
                {
                    throw new InvalidDataException(
                        $"Sound #{id} points outside the archive: offset={descriptor.Offset}, size={descriptor.Size}.");
                }

                records.Add((uint)id, new SoundPakRecord(
                    (uint)id,
                    (SacredSoundStorageFormat)descriptor.Type,
                    descriptor.Offset,
                    (int)descriptor.Size));
            }

            return new SoundPakArchive(stream, records);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public bool TryGetRecord(uint soundId, out SoundPakRecord record) =>
        _recordsById.TryGetValue(soundId, out record);

    public byte[] Read(uint soundId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_recordsById.TryGetValue(soundId, out var record))
            throw new KeyNotFoundException($"Sound.pak does not contain sound #{soundId}.");

        var bytes = GC.AllocateUninitializedArray<byte>(record.Size);
        var totalRead = 0;
        while (totalRead < bytes.Length)
        {
            var bytesRead = RandomAccess.Read(
                _stream.SafeFileHandle,
                bytes.AsSpan(totalRead),
                record.Offset + totalRead);
            if (bytesRead == 0)
                throw new EndOfStreamException($"Sound #{soundId} ended before its indexed size.");

            totalRead += bytesRead;
        }
        return bytes;
    }

    public string Extract(uint soundId, string outputDirectory, string? fileStem = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory cannot be empty.", nameof(outputDirectory));
        if (!_recordsById.TryGetValue(soundId, out var record))
            throw new KeyNotFoundException($"Sound.pak does not contain sound #{soundId}.");

        Directory.CreateDirectory(outputDirectory);
        var safeStem = string.IsNullOrWhiteSpace(fileStem) ? soundId.ToString() : fileStem;
        var outputPath = Path.Combine(outputDirectory, safeStem + record.FileExtension);
        File.WriteAllBytes(outputPath, Read(soundId));
        return outputPath;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stream.Dispose();
    }
}
