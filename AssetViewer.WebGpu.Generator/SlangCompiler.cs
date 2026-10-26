using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AssetViewer.WebGpu.Generator;

/// <summary>Runs build-host Slang, independently of the application's browser runtime.</summary>
internal sealed class SlangCompiler : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "assetviewer-wgsl-" + Guid.NewGuid().ToString("N"));
    private readonly string _executable;

    internal SlangCompiler(string root, CancellationToken cancellation)
    {
        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
            : throw new PlatformNotSupportedException("Unsupported Slang build host.");
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException("Slang requires an x64 or ARM64 build host.")
        };
        _executable = Path.Combine(root, platform + "-" + architecture, "bin",
            platform == "windows" ? "slangc.exe" : "slangc");
        if (!File.Exists(_executable))
            throw new FileNotFoundException("Restored Slang compiler is missing. Restore NuGet packages before building.", _executable);
        // NuGet extraction does not reliably preserve the executable permission on Unix.
        if (platform != "windows") Run("/bin/chmod", "+x \"" + _executable + "\"", cancellation);
        Directory.CreateDirectory(_directory);
    }

    internal string Compile(ShaderProgram program, string stage, CancellationToken cancellation)
    {
        var input = Path.Combine(_directory, program.Name + ".hlsl");
        var output = Path.Combine(_directory, program.Name + "." + stage + ".wgsl");
        File.WriteAllText(input, program.Source);
        var entry = stage == "compute" ? "skin_main" : stage == "vertex" ? "vs_main"
            : stage == "opaque" ? "ps_sdr_opaque" : program.Name == "PreviewGroundGrid" ? "ps_main" : "ps_sdr";
        var compilerStage = stage == "opaque" ? "fragment" : stage;
        var bindings = stage == "compute" ? "-fvk-t-shift 0 0 -fvk-u-shift 8 0"
            : "-fvk-b-shift 0 0 -fvk-t-shift 2 0 -fvk-s-shift 6 0";
        Run(_executable, $"\"{input}\" -lang hlsl -target wgsl -entry {entry} -stage {compilerStage} {bindings} -o \"{output}\"", cancellation);
        var source = File.ReadAllText(output).Replace("\r\n", "\n");
        // Slang may retain HLSL's precise qualifier; WGSL has no such keyword.
        if (stage == "compute") return source.Replace("precise var ", "var ");
        // Mip-zero preview textures do not depend on derivatives after alpha discard.
        return "diagnostic(off, derivative_uniformity);\n" + source;
    }

    private static void Run(string executable, string arguments, CancellationToken cancellation)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable, arguments)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (!process.WaitForExit(100))
            {
                cancellation.ThrowIfCancellationRequested();
                if (elapsed.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Slang exceeded its 60 second compilation timeout.");
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Slang exited with code {process.ExitCode}: {errors.GetAwaiter().GetResult()} {output.GetAwaiter().GetResult()}");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
