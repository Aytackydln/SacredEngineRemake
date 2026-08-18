using System.Diagnostics;

namespace Sacred.Granny.Native;

internal sealed class GrannyDllWorkerProcess : IDisposable
{
    private const int MaximumResponseBytes = 512 * 1024 * 1024;

    private readonly object _sync = new();
    private readonly Process _process;
    private readonly BinaryWriter _input;
    private readonly BinaryReader _output;
    private bool _disposed;

    public GrannyDllWorkerProcess(string grannyDllPath, string workerPath)
    {
        grannyDllPath = Path.GetFullPath(grannyDllPath);
        workerPath = Path.GetFullPath(workerPath);
        if (!File.Exists(grannyDllPath))
            throw new FileNotFoundException("The game's Granny.dll was not found.", grannyDllPath);
        if (!File.Exists(workerPath))
            throw new FileNotFoundException("The x86 Granny worker was not found.", workerPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(grannyDllPath);
        _process = Process.Start(startInfo) ??
                   throw new InvalidOperationException("The x86 Granny worker could not be started.");
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.BeginErrorReadLine();
        try
        {
            GrannyDllWorkerProtocol.ValidateHandshake(_process.StandardOutput.BaseStream);
            _input = new BinaryWriter(_process.StandardInput.BaseStream);
            _output = new BinaryReader(_process.StandardOutput.BaseStream);
        }
        catch (Exception exception)
        {
            StopFailedProcess();
            throw new InvalidDataException(
                "The x86 Granny worker could not initialize the game's Granny 1 DLL.",
                exception);
        }
    }

    public GrannyDllMeshData Extract(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureRunning();
            _input.Write(payload.Length);
            _input.Write(payload);
            _input.Flush();

            int responseLength;
            try
            {
                responseLength = _output.ReadInt32();
            }
            catch (EndOfStreamException exception)
            {
                throw WorkerStopped(exception);
            }

            if (responseLength is <= 0 or > MaximumResponseBytes)
                throw new InvalidDataException($"The Granny worker returned an invalid response length: {responseLength}.");
            var response = _output.ReadBytes(responseLength);
            if (response.Length != responseLength)
                throw WorkerStopped(new EndOfStreamException("The Granny worker response ended early."));
            return GrannyDllWorkerProtocol.ReadResponse(response);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _input.Dispose();
            _output.Dispose();
            if (!_process.HasExited && !_process.WaitForExit(2_000))
                _process.Kill(entireProcessTree: true);
            _process.ErrorDataReceived -= OnErrorDataReceived;
            _process.Dispose();
        }
    }

    private void EnsureRunning()
    {
        if (_process.HasExited)
            throw WorkerStopped();
    }

    private InvalidDataException WorkerStopped(Exception? innerException = null) =>
        new(
            _process.HasExited
                ? $"The x86 Granny worker exited with code {_process.ExitCode}."
                : "The x86 Granny worker stopped responding.",
            innerException);

    private void StopFailedProcess()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(2_000);
        }
        _process.ErrorDataReceived -= OnErrorDataReceived;
        _process.Dispose();
    }

    private static void OnErrorDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            Console.WriteLine($"[Granny.dll] {eventArgs.Data}");
    }
}
