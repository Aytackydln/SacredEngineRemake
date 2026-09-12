using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CaptureMemory <Sacred.exe path> <output module dump>");
    Environment.ExitCode = 2;
    return;
}
var executable = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
using var process = Process.Start(new ProcessStartInfo(executable)
{
    WorkingDirectory = Path.GetDirectoryName(executable)!,
    WindowStyle = ProcessWindowStyle.Hidden,
    UseShellExecute = false
}) ?? throw new InvalidOperationException("Could not start Sacred.");
Console.WriteLine($"Started Sacred process {process.Id} for a module dump.");
try
{
    var handle = NativeMethods.OpenProcess(0x0010 | 0x0400, false, process.Id);
    if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        ProcessModule? module = null;
        var ready = false;
        var pointer = new byte[4];
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(1000);
            if (process.HasExited) throw new InvalidOperationException("Sacred exited before initialization.");
            process.Refresh();
            module = process.MainModule ?? throw new InvalidOperationException("Sacred main module unavailable.");
            // Verified Sacred Gold version: type-manager pointer becomes nonzero after unpacking/loading.
            if (NativeMethods.ReadProcessMemory(handle, module.BaseAddress + 0x6AB5E4, pointer, 4, out var read) &&
                read == 4 && BitConverter.ToUInt32(pointer) != 0)
            {
                ready = true;
                break;
            }
            if (attempt % 5 == 0) Console.WriteLine($"Waiting for native initialization ({attempt + 1}s).");
        }
        if (!ready || module == null) throw new TimeoutException("Native type manager was not initialized within 60 seconds.");
        if (module.BaseAddress != (nint)0x400000)
            throw new InvalidDataException($"Unexpected module base 0x{module.BaseAddress:X}; extractor expects 0x400000.");
        var bytes = new byte[module.ModuleMemorySize];
        if (!NativeMethods.ReadProcessMemory(handle, module.BaseAddress, bytes, bytes.Length, out var bytesRead))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (bytesRead != (nuint)bytes.Length) throw new IOException("Incomplete module read.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllBytesAsync(output, bytes);
        Console.WriteLine($"Dumped {bytes.Length:N0} bytes at base 0x{module.BaseAddress:X}: {output}");
    }
    finally
    {
        NativeMethods.CloseHandle(handle);
    }
}
finally
{
    if (!process.HasExited)
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
    Console.WriteLine($"Capture process {process.Id} closed.");
}

internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadProcessMemory(nint process, nint address, byte[] buffer, int size, out nuint bytesRead);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
