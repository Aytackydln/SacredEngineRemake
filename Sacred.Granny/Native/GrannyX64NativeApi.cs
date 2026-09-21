using System.Runtime.InteropServices;

namespace Sacred.Granny.Native;

/// <summary>Direct x64 binding to the Granny 1.2b-compatible library.</summary>
internal sealed class GrannyX64NativeApi : IDisposable
{
    private const string Version = "1.2b";
    // The x64 compatibility DLL preserves the Granny 1.2b version handshake.
    private const string Platform = "win32";
    private const string ReleaseDate = "10-4-2000";
    private const string Copyright = "(C) Copyright 1999-2000 RAD Game Tools, Inc.  All Rights Reserved.";

    private readonly nint _module;
    private bool _disposed;

    public GrannyX64NativeApi(string libraryPath)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("granny_x64.dll requires a 64-bit Windows process.");

        libraryPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(libraryPath))
            throw new FileNotFoundException("granny_x64.dll was not found.", libraryPath);

        _module = NativeLibrary.Load(libraryPath);
        try
        {
            OpenModel = Load<GrannyOpenModel>("GrannyOpenModel");
            CloseModel = Load<GrannyCloseModel>("GrannyCloseModel");
            OpenSequence = Load<GrannyOpenSequence>("GrannyOpenSequence");
            CloseSequence = Load<GrannyCloseSequence>("GrannyCloseSequence");
            LockSequenceForRendering = Load<GrannyLockSequenceForRendering>("GrannyLockSequenceForRendering");
            GetRenderingStatesLeft = Load<GrannyGetRenderingStatesLeft>("GrannyGetRenderingStatesLeft");
            LockNextRenderingState = Load<GrannyLockNextRenderingState>("GrannyLockNextRenderingState");
            UnlockRenderingState = Load<GrannyUnlockRenderingState>("GrannyUnlockRenderingState");
            UnlockRendering = Load<GrannyUnlockRendering>("GrannyUnlockRendering");
            ExplainErrorCode = Load<GrannyExplainErrorCode>("GrannyExplainErrorCode");
            Close = Load<GrannyClose>("GrannyClose");

            var openVersion = Load<GrannyOpenVersion>("GrannyOpenVersion");
            var resetFilesystem = Load<GrannyResetFilesystem>("GrannyResetFilesystem");
            ThrowIfFailed(openVersion(Version, Platform, ReleaseDate, Copyright, out var handle), "open Granny 1.2b");
            Handle = handle;
            ThrowIfFailed(resetFilesystem(Handle), "initialize Granny's filesystem");
        }
        catch
        {
            if (Handle != 0)
                Close?.Invoke(Handle);
            NativeLibrary.Free(_module);
            throw;
        }
    }

    public uint Handle { get; }

    public GrannyOpenModel OpenModel { get; } = null!;
    public GrannyCloseModel CloseModel { get; } = null!;
    public GrannyOpenSequence OpenSequence { get; } = null!;
    public GrannyCloseSequence CloseSequence { get; } = null!;
    public GrannyLockSequenceForRendering LockSequenceForRendering { get; } = null!;
    public GrannyGetRenderingStatesLeft GetRenderingStatesLeft { get; } = null!;
    public GrannyLockNextRenderingState LockNextRenderingState { get; } = null!;
    public GrannyUnlockRenderingState UnlockRenderingState { get; } = null!;
    public GrannyUnlockRendering UnlockRendering { get; } = null!;
    public GrannyExplainErrorCode ExplainErrorCode { get; } = null!;
    public GrannyClose? Close { get; private set; }

    public void ThrowIfFailed(int result, string operation)
    {
        if (result == 0)
            return;

        var message = $"Granny error {result}";
        if (ExplainErrorCode(result, out var messageAddress) == 0 && messageAddress != 0)
            message = Marshal.PtrToStringAnsi(messageAddress) ?? message;
        throw new InvalidDataException($"Could not {operation}: {message}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (Handle != 0)
            Close?.Invoke(Handle);
        NativeLibrary.Free(_module);
    }

    private T Load<T>(string exportName) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, exportName));

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal delegate int GrannyOpenVersion(string version, string platform, string date, string copyright, out uint granny);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void GrannyClose(uint granny);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GrannyResetFilesystem(uint granny);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal delegate int GrannyOpenModel(uint granny, string fileName, out GrannyHandle model);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void GrannyCloseModel(uint granny, uint model);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GrannyOpenSequence(uint granny, uint model, out GrannyHandle sequence);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void GrannyCloseSequence(uint granny, uint sequence);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GrannyLockSequenceForRendering(uint granny, uint sequence, uint present, out GrannyHandle rendering);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GrannyGetRenderingStatesLeft(uint granny, uint rendering, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GrannyLockNextRenderingState(uint granny, uint rendering, nint state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void GrannyUnlockRenderingState(nint state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void GrannyUnlockRendering(uint granny, uint rendering);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GrannyExplainErrorCode(int result, out nint message);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct GrannyHandle(uint Granny, uint Value);
