namespace Sacred.Core.Pak.Sound;

/// <summary>Native <c>cObjectSounds::eObjectSound</c> values stored in sndProfiles.pak.</summary>
public enum SacredSoundProfileType : uint
{
    OST_INVALID = 0,
    OST_SOUND = 1,
}

/// <summary>Native <c>cObjectSounds::eObjectSounds</c> flag values stored in sndProfiles.pak.</summary>
[Flags]
public enum SacredSoundProfileFlags : uint
{
    OSF_RESERVED = 1,
}
