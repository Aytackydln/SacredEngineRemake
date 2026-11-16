using Sacred.Core.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Networking;

/// <summary>One FILE lifetime using compared fopen policy, lazy native buffering,
/// input/output and close. Platform services own the actual OS handle.</summary>
public sealed class WorldSettingsNativeFilePort(SacredCrtFilePool files, SacredCrtDescriptorPool descriptors,
    SacredCrtBufferDefinition bufferDefinition, WorldSettingsNativeFileServices services,
    uint defaultCommitFlags, uint defaultFileMode, uint umask)
{
    private SacredCrtFileSlot? _file;
    private SacredCrtDescriptor? _descriptor;
    private SacredCrtInputState? _inputState;
    private SacredCrtOutputState? _outputState;
    private SacredCrtBufferedInput? _input;
    private SacredCrtBufferedOutput? _output;
    public WorldSettingsFileServices Reader => new(Open, ReadLine, handle => { _ = Close(handle); });
    public WorldSettingsSaveServices Writer => new(Open, Write, Close);
    public SacredCrtFileSlot? CurrentFile => _file;

    private uint Open(ReadOnlyMemory<byte> path, ReadOnlyMemory<byte> mode)
    {
        if (_file != null) throw new InvalidOperationException("Native settings FILE is already open.");
        _file = SacredCrtFileOpen.Open(path, mode, files, descriptors, services.Open, defaultCommitFlags, defaultFileMode, umask);
        if (_file is null) return 0;
        _descriptor = descriptors.OpenDescriptors().Single(item => item.Index == _file.Descriptor).Slot;
        return 1;
    }
    private ReadOnlyMemory<byte>? ReadLine(uint handle, int capacity)
    {
        Require(handle);
        if (capacity <= 0) return null;
        if (capacity == 1) return Array.Empty<byte>();
        if ((_file!.Flags & 2) != 0) { _file.Flags |= 0x20; return null; }
        EnsureBuffer();
        _inputState ??= new(_descriptor!.Flags) { Lookahead = _descriptor.Lookahead };
        _input ??= SacredCrtBufferedInput.Create(_inputState, new(destination => services.Read(_descriptor!.Handle, destination), Seek), checked((int)_file.BufferSize), _file.Flags | 1);
        var result = _input.ReadLine(capacity); _file.Flags = _input.FileFlags;
        _descriptor!.Flags = _inputState.Flags; _descriptor.Lookahead = _inputState.Lookahead;
        return result;
    }
    private int Write(uint handle, ReadOnlyMemory<byte> value)
    {
        Require(handle);
        if (value.IsEmpty) return 0;
        if ((_file!.Flags & 1) != 0) { _file.Flags |= 0x20; return -1; }
        EnsureBuffer(); _outputState ??= new(_descriptor!.Flags);
        var writer = new SacredCrtWriteServices(bytes => services.Write(_descriptor!.Handle, bytes), Seek);
        int result;
        if (_file.Storage == SacredCrtBufferStorage.Inline)
        {
            result = value.Length;
            for (var index = 0; index < value.Length; index++)
                if (SacredCrtOutput.Write(_outputState, writer, value.Slice(index, 1)) != 1) { _file.Flags |= 0x20; result = -1; break; }
        }
        else
        {
            _output ??= SacredCrtBufferedOutput.Create(_outputState, writer, checked((int)_file.BufferSize), _file.Flags | 2);
            result = _output.Write(value); _file.Flags = _output.FileFlags;
        }
        _descriptor!.Flags = _outputState.Flags; return result;
    }
    private int Close(uint handle)
    {
        Require(handle); var result = _output?.Flush() ?? 0;
        if (_output != null) _file!.Flags = _output.FileFlags;
        SacredCrtFileBuffer.Free(_file!, services.Free);
        if (!services.Open.Close(_descriptor!.Handle)) { _ = services.Open.GetLastError(); result = -1; }
        _descriptor.Handle = uint.MaxValue; _descriptor.Flags = 0; _file!.Flags = 0;
        _file = null; _descriptor = null; _input = null; _output = null; _inputState = null; _outputState = null; return result;
    }
    private bool Seek(int offset, int origin) => services.Seek(_descriptor!.Handle, offset, origin) != uint.MaxValue;
    private void EnsureBuffer()
    {
        if (_file!.Storage == SacredCrtBufferStorage.None) SacredCrtFileBuffer.Allocate(_file, bufferDefinition, services.Open.Allocate);
    }
    private void Require(uint handle)
    {
        if (handle != 1 || _file is null) throw new InvalidOperationException("Native settings FILE is not open.");
    }
}
