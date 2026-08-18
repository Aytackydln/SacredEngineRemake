using System.Runtime.InteropServices;

namespace Sacred.Granny.Native.Worker;

internal sealed class Granny1NativeApi : IDisposable
{
    private const string Version = "1.2b";
    private const string Platform = "win32";
    private const string ReleaseDate = "10-4-2000";
    private const string Copyright = "(C) Copyright 1999-2000 RAD Game Tools, Inc.  All Rights Reserved.";

    private readonly nint _module;
    private readonly GrannyClose _close;
    private bool _disposed;

    public Granny1NativeApi(string dllPath)
    {
        if (!Environment.Is64BitProcess && RuntimeInformation.ProcessArchitecture == Architecture.X86)
        {
            // The game DLL and its handles are strictly 32-bit.
        }
        else
        {
            throw new PlatformNotSupportedException("The Granny 1 worker must run as an x86 process.");
        }

        if (!File.Exists(dllPath))
            throw new FileNotFoundException("The game's Granny.dll was not found.", dllPath);

        _module = NativeLibrary.Load(dllPath);
        OpenModel = Load<GrannyOpenModel>("_GrannyOpenModel@12");
        CloseModel = Load<GrannyCloseModel>("_GrannyCloseModel@8");
        OpenSequence = Load<GrannyOpenSequence>("_GrannyOpenSequence@12");
        CloseSequence = Load<GrannyCloseSequence>("_GrannyCloseSequence@8");
        LockSequenceForRendering = Load<GrannyLockSequenceForRendering>("_GrannyLockSequenceForRendering@16");
        GetRenderingStatesLeft = Load<GrannyGetRenderingStatesLeft>("_GrannyGetRenderingStatesLeft@12");
        LockNextRenderingState = Load<GrannyLockNextRenderingState>("_GrannyLockNextRenderingState@12");
        UnlockRenderingState = Load<GrannyUnlockRenderingState>("_GrannyUnlockRenderingState@4");
        UnlockRendering = Load<GrannyUnlockRendering>("_GrannyUnlockRendering@8");
        GetLastResult = Load<GrannyGetLastResult>("_GrannyGetLastResult@0");
        ExplainErrorCode = Load<GrannyExplainErrorCode>("_GrannyExplainErrorCode@8");
        _close = Load<GrannyClose>("_GrannyClose@4");

        var openVersion = Load<GrannyOpenVersion>("_GrannyOpenVersion@20");
        var resetFilesystem = Load<GrannyResetFilesystem>("_GrannyResetFilesystem@4");
        ThrowIfFailed(openVersion(Version, Platform, ReleaseDate, Copyright, out var granny), "open Granny 1.2b");
        Handle = granny;
        try
        {
            ThrowIfFailed(resetFilesystem(Handle), "initialize Granny's filesystem");
        }
        catch
        {
            _close(Handle);
            throw;
        }
    }

    public uint Handle { get; }

    public GrannyOpenModel OpenModel { get; }
    public GrannyCloseModel CloseModel { get; }
    public GrannyOpenSequence OpenSequence { get; }
    public GrannyCloseSequence CloseSequence { get; }
    public GrannyLockSequenceForRendering LockSequenceForRendering { get; }
    public GrannyGetRenderingStatesLeft GetRenderingStatesLeft { get; }
    public GrannyLockNextRenderingState LockNextRenderingState { get; }
    public GrannyUnlockRenderingState UnlockRenderingState { get; }
    public GrannyUnlockRendering UnlockRendering { get; }
    public GrannyGetLastResult GetLastResult { get; }
    public GrannyExplainErrorCode ExplainErrorCode { get; }

    public void ThrowIfFailed(int result, string operation)
    {
        if (result == 0)
            return;

        ExplainErrorCode(result, out var messageAddress);
        var message = Marshal.PtrToStringAnsi(messageAddress) ?? $"Granny error {result}";
        throw new InvalidDataException($"Could not {operation}: {message}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _close(Handle);
        NativeLibrary.Free(_module);
    }

    private T Load<T>(string exportName) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, exportName));

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal delegate int GrannyOpenVersion(string version, string platform, string date, string copyright, out uint granny);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GrannyClose(uint granny);
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
    internal delegate int GrannyGetLastResult();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GrannyExplainErrorCode(int result, out nint message);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct GrannyHandle(uint Granny, uint Value);
