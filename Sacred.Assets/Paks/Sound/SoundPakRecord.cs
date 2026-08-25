using Sacred.Core.Pak.Sound;

namespace Sacred.Assets.Paks.Sound;

public readonly record struct SoundPakRecord(
    uint SoundId,
    SacredSoundStorageFormat StorageFormat,
    long Offset,
    int Size)
{
    public string FileExtension => StorageFormat switch
    {
        SacredSoundStorageFormat.Wave => ".wav",
        SacredSoundStorageFormat.Mp3 => ".mp3",
        _ => ".bin"
    };
}
