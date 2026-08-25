using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Assets.Utils;
using Sacred.Core.Pak.Sound;
using Sacred.Core.Utils;

namespace Sacred.Assets.Paks.Sound;

/// <summary>Reader for the named sound-selection tables in sndProfiles.pak.</summary>
public sealed class SoundProfilePakArchive
{
    private static readonly Encoding NameEncoding = Encoding.GetEncoding("iso-8859-1");
    private readonly Dictionary<uint, SoundProfileRecord> _profilesById;

    private SoundProfilePakArchive(Dictionary<uint, SoundProfileRecord> profilesById)
    {
        _profilesById = profilesById;
        Profiles = new ReadOnlyCollection<SoundProfileRecord>(
            profilesById.Values.OrderBy(static profile => profile.ProfileId).ToArray());
    }

    public IReadOnlyList<SoundProfileRecord> Profiles { get; }

    public static SoundProfilePakArchive Load(string path)
    {
        using var stopwatch = new LoggingStopwatch("Loading sndProfiles.pak... ");

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Sound profile PAK path cannot be empty.", nameof(path));

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, NameEncoding, leaveOpen: true);
        var header = reader.ReadStruct<SoundProfilePakHeaderLayout>(SoundProfilePakHeaderLayout.SerializedSize);
        header.ValidateSignature();
        if (header.EntryCount > int.MaxValue)
            throw new InvalidDataException($"sndProfiles.pak has too many descriptor slots: {header.EntryCount}.");

        var descriptors = PakDataHelpers.ReadEntryDescriptors(
            stream,
            (int)header.EntryCount,
            Path.GetFileName(path));
        var profiles = new Dictionary<uint, SoundProfileRecord>();

        for (var id = 0; id < descriptors.Length; id++)
        {
            var descriptor = descriptors[id];
            if (descriptor.Offset == 0 && descriptor.Size == 0)
                continue;
            if (descriptor.Type != SoundProfilePakEntryLayout.DescriptorType ||
                descriptor.Size != SoundProfilePakEntryLayout.SerializedSize ||
                (ulong)descriptor.Offset + descriptor.Size > (ulong)stream.Length)
            {
                throw new InvalidDataException(
                    $"Sound profile #{id} has an invalid descriptor: type={descriptor.Type}, " +
                    $"offset={descriptor.Offset}, size={descriptor.Size}.");
            }

            stream.Position = descriptor.Offset;
            var layout = reader.ReadStruct<SoundProfilePakEntryLayout>(SoundProfilePakEntryLayout.SerializedSize);
            if (layout.IsDefined == 0)
                continue;

            var nameBytes = layout.NameBytes;
            var nameSpan = MemoryMarshal.CreateReadOnlySpan(ref nameBytes[0], SoundProfilePakEntryLayout.NameLength);
            var nullIndex = nameSpan.IndexOf((byte)0);
            var name = NameEncoding.GetString(nullIndex < 0 ? nameSpan : nameSpan[..nullIndex]);

            var layoutSoundIds = layout.SoundIds;
            var soundIds = new ushort[SoundProfilePakEntryLayout.SoundSlotCount];
            for (var slot = 0; slot < soundIds.Length; slot++)
                soundIds[slot] = layoutSoundIds[slot];

            profiles.Add((uint)id, new SoundProfileRecord((uint)id, name, soundIds));
        }

        return new SoundProfilePakArchive(profiles);
    }

    public bool TryGetProfile(uint profileId, out SoundProfileRecord? profile) =>
        _profilesById.TryGetValue(profileId, out profile);
}
