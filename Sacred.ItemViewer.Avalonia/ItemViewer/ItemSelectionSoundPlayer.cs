using System;
using System.Runtime.InteropServices;
using Sacred.Assets.Paks.Sound;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Sound;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed partial class ItemSelectionSoundPlayer : IDisposable
{
    private const float PlaybackVolume = 0.5f;
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    private readonly SoundPakArchive _archive;
    private GCHandle _pinnedWave;
    private bool _disposed;
    private bool _muted;

    public ItemSelectionSoundPlayer(string soundPakPath)
    {
        _archive = SoundPakArchive.Load(soundPakPath);
    }

    public void Play(SacredItemCategory category)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_muted)
            return;

        var sound = SacredInventorySoundResolver.Resolve(category);
        var bytes = WaveVolumeScaler.Scale(_archive.Read((uint)sound), PlaybackVolume);

        StopAndReleaseWave();
        _pinnedWave = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        if (!PlaySound(_pinnedWave.AddrOfPinnedObject(), 0, SndAsync | SndNoDefault | SndMemory))
        {
            _pinnedWave.Free();
            Console.WriteLine($"Could not play inventory sound {(uint)sound} ({sound}).");
            return;
        }

        Console.WriteLine($"Playing inventory sound {(uint)sound} ({sound}); item category {category} ({(byte)category}).");
    }

    public void SetMuted(bool muted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _muted = muted;
        if (muted)
            StopAndReleaseWave();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAndReleaseWave();
        _archive.Dispose();
        _disposed = true;
    }

    private void StopAndReleaseWave()
    {
        PlaySound(0, 0, 0);
        if (_pinnedWave.IsAllocated)
            _pinnedWave.Free();
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(nint sound, nint module, uint flags);
}
