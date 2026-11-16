using System.Runtime.InteropServices;
using Sacred.Assets.Executable;
using Sacred.Core.Executable;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Networking;

/// <summary>One owned Win32 handle behind the compared settings FILE lifetime.
/// Paths remain original narrow bytes. CRT startup facts/heap policy are supplied;
/// update opening includes the compared text-tail preparation. Alternating
/// buffered reads/writes still requires explicit FILE positioning/flush support.</summary>
public sealed class WorldSettingsWindowsFilePort : IDisposable
{
    private nint _handle = -1;
    private uint _error;
    private readonly WorldSettingsNativeFilePort _port;
    public WorldSettingsFileServices Reader => _port.Reader;
    public WorldSettingsSaveServices Writer => _port.Writer;
    public static WorldSettingsWindowsFilePort FromExecutable(string directory, Func<int, bool> allocate)
    {
        var initial = SacredCrtInitialFileStates.Load(directory);
        return new(SacredCrtBufferDefinitions.Load(directory), allocate, initial.CommitFlags, initial.FileMode, initial.Umask);
    }

    public WorldSettingsWindowsFilePort(SacredCrtBufferDefinition bufferDefinition, Func<int, bool> allocate,
        uint defaultCommitFlags, uint defaultFileMode, uint umask)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original settings platform binding requires Windows.");
        var reserved = Enumerable.Range(0, 32).Select(index => new SacredCrtDescriptor { Flags = index < 3 ? (byte)1 : (byte)0 }).ToArray();
        _port = new(new(new List<SacredCrtFileSlot?> { new() }), new(new List<SacredCrtDescriptor[]?> { reserved }), bufferDefinition,
            new(new(allocate, Create, Type, Close, () => _error, descriptor => SacredCrtUpdateOpen.Prepare(descriptor,
                    new((offset, origin) => Seek(descriptor.Handle, offset, origin), destination => Read(descriptor.Handle, destination),
                        () => Truncate(descriptor.Handle), () => _error))),
                Read, Write, Seek, () => { }), defaultCommitFlags, defaultFileMode, umask);
    }
    private uint Create(SacredCrtCreateRequest request)
    {
        if (_handle != -1) throw new InvalidOperationException("Settings OS handle is already owned.");
        var attributes = new SecurityAttributes { Length = (uint)Marshal.SizeOf<SecurityAttributes>(), Inherit = request.InheritHandle ? 1 : 0 };
        _handle = Native.CreateFileA(request.Path.ToArray().Append((byte)0).ToArray(), request.DesiredAccess, request.ShareMode,
            ref attributes, request.CreationDisposition, request.FlagsAndAttributes, 0);
        _error = unchecked((uint)Marshal.GetLastPInvokeError());
        return _handle == -1 ? uint.MaxValue : 1;
    }
    private uint Type(uint handle)
    {
        Require(handle); var value = Native.GetFileType(_handle); _error = unchecked((uint)Marshal.GetLastPInvokeError()); return value;
    }
    private bool Close(uint handle)
    {
        Require(handle); var success = Native.CloseHandle(_handle); _error = unchecked((uint)Marshal.GetLastPInvokeError()); _handle = -1; return success;
    }
    private SacredCrtReadResult Read(uint handle, Memory<byte> destination)
    {
        Require(handle); var buffer = new byte[destination.Length];
        var success = Native.ReadFile(_handle, buffer, (uint)buffer.Length, out var count, 0); _error = unchecked((uint)Marshal.GetLastPInvokeError());
        buffer.AsMemory(0, checked((int)count)).CopyTo(destination); return new(success, checked((int)count), _error);
    }
    private SacredCrtWriteResult Write(uint handle, ReadOnlyMemory<byte> source)
    {
        Require(handle); var success = Native.WriteFile(_handle, source.ToArray(), (uint)source.Length, out var count, 0);
        _error = unchecked((uint)Marshal.GetLastPInvokeError()); return new(success, checked((int)count), _error);
    }
    private uint Seek(uint handle, int offset, int origin)
    {
        Require(handle); var position = Native.SetFilePointer(_handle, offset, 0, (uint)origin);
        _error = unchecked((uint)Marshal.GetLastPInvokeError()); return position;
    }
    private bool Truncate(uint handle)
    {
        Require(handle); var success = Native.SetEndOfFile(_handle);
        _error = unchecked((uint)Marshal.GetLastPInvokeError()); return success;
    }
    private void Require(uint handle)
    {
        if (handle != 1 || _handle == -1) throw new InvalidOperationException("Settings OS handle is not owned.");
    }
    public void Dispose()
    {
        if (_port.CurrentFile != null) _ = _port.Writer.Close(1);
        else if (_handle != -1) _ = Close(1);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public uint Length; public nint Descriptor; public int Inherit; }
    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern nint CreateFileA(byte[] name, uint access, uint share, ref SecurityAttributes security, uint creation, uint attributes, nint template);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern uint GetFileType(nint handle);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool CloseHandle(nint handle);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ReadFile(nint handle, [Out] byte[] bytes, uint count, out uint read, nint overlapped);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool WriteFile(nint handle, byte[] bytes, uint count, out uint written, nint overlapped);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern uint SetFilePointer(nint handle, int distance, nint high, uint origin);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetEndOfFile(nint handle);
    }
}
