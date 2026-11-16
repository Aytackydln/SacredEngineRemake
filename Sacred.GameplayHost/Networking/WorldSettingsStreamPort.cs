using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Networking;

/// <summary>One open settings-stream lifetime, with the compared CRT buffering
/// and translation. The caller owns path/mode opening, descriptor classification
/// and IOException-to-Win32 mapping; no filename decoding or OS policy is guessed.</summary>
public sealed class WorldSettingsStreamPort(Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Stream?> open,
    Func<IOException, uint> mapError, byte descriptorFlags, int bufferSize = 4096)
{
    private Stream? _stream;
    private SacredCrtBufferedInput? _input;
    private SacredCrtBufferedOutput? _output;

    public WorldSettingsFileServices Reader => new((path, mode) => Open(path, mode, false),
        (handle, capacity) => { Require(handle); return _input!.ReadLine(capacity); },
        handle => { _ = Close(handle); });
    public WorldSettingsSaveServices Writer => new((path, mode) => Open(path, mode, true),
        (handle, bytes) => { Require(handle); return _output!.Write(bytes); }, Close);

    private uint Open(ReadOnlyMemory<byte> path, ReadOnlyMemory<byte> mode, bool writing)
    {
        if (_stream != null) throw new InvalidOperationException("Settings stream is already open.");
        _stream = open(path, mode);
        if (_stream is null) return 0;
        if (writing) _output = new(new(descriptorFlags), new(Write, Seek), bufferSize);
        else _input = new(new(descriptorFlags), new(Read, Seek), bufferSize);
        return 1;
    }
    private SacredCrtReadResult Read(Memory<byte> destination)
    {
        try { return new(true, _stream!.Read(destination.Span)); }
        catch (IOException error) { return new(false, 0, mapError(error)); }
    }
    private SacredCrtWriteResult Write(ReadOnlyMemory<byte> source)
    {
        try { _stream!.Write(source.Span); return new(true, source.Length); }
        catch (IOException error) { return new(false, 0, mapError(error)); }
    }
    private bool Seek(int offset, int origin)
    {
        try { _stream!.Seek(offset, (SeekOrigin)origin); return true; }
        catch (IOException error) { _ = mapError(error); return false; }
    }
    private int Close(uint handle)
    {
        Require(handle); var result = _output?.Flush() ?? 0;
        try { _stream!.Dispose(); }
        catch (IOException error) { _ = mapError(error); result = -1; }
        _stream = null; _input = null; _output = null; return result;
    }
    private void Require(uint handle)
    {
        if (handle != 1 || _stream is null) throw new InvalidOperationException("Settings stream lifetime is not open.");
    }
}
