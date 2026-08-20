using System;
using System.Runtime.InteropServices;

namespace Sacred.Engine.Extern;

internal static partial class Kernel32
{
    private const string LibraryName = "kernel32";

    internal const uint WaitObject0 = 0;
    internal const uint WaitTimeout = 258;
    internal const uint Infinite = 0xFFFFFFFF;

    [DllImport(LibraryName, CallingConvention = CallingConvention.Winapi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPTStr)] string? lpModuleName);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetModuleHandleA(string moduleName);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(nint module, string procName);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CreateEventA(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [LibraryImport(LibraryName, EntryPoint = "CreateWaitableTimerExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWaitableTimerEx(
        nint timerAttributes,
        string? timerName,
        uint flags,
        uint desiredAccess);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWaitableTimerEx(
        nint timer,
        in long dueTime,
        int period,
        nint completionRoutine,
        nint completionRoutineArgument,
        nint wakeContext,
        uint tolerableDelay);

    [LibraryImport(LibraryName)]
    internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetEvent(nint handle);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
