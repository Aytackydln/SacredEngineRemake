using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Sacred.Engine.Graphics;

/// <summary>Completes screenshot readback and file encoding away from frame submission.</summary>
internal sealed class Dx12ScreenshotWriterQueue : IDisposable
{
    private readonly string _gameDirectory;
    private readonly ConcurrentQueue<ScreenshotRequest> _requests = new();
    private readonly SemaphoreSlim _workAvailable = new(0);
    private readonly Thread _worker;
    private int _accepting = 1;

    public Dx12ScreenshotWriterQueue(string gameDirectory)
    {
        _gameDirectory = gameDirectory;
        _worker = new Thread(Process)
        {
            IsBackground = true,
            Name = "Sacred screenshot writer",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public void Enqueue(Dx12PendingScreenshot screenshot, string? label)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        if (Volatile.Read(ref _accepting) == 0)
        {
            screenshot.Dispose();
            return;
        }

        _requests.Enqueue(new ScreenshotRequest(screenshot, label));
        _workAvailable.Release();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _accepting, 0) == 0)
            return;

        _workAvailable.Release();
        _worker.Join();
        _workAvailable.Dispose();
    }

    private void Process()
    {
        while (Volatile.Read(ref _accepting) != 0 || !_requests.IsEmpty)
        {
            if (!_requests.TryDequeue(out var request))
            {
                _workAvailable.Wait();
                continue;
            }

            using (request.Screenshot)
            {
                try
                {
                    var image = request.Screenshot.WaitAndRead();
                    var path = Dx12ScreenshotWriter.CreatePath(_gameDirectory, request.Label, image);
                    Dx12ScreenshotWriter.Save(image, path);
                    EngineLog.WriteLine(
                        $"Screenshot saved to {path} ({Dx12ScreenshotWriter.DescribeColorSpace(image)}).");
                }
                catch (Exception exception)
                {
                    EngineLog.WriteLine($"Screenshot failed: {exception.Message}");
                }
            }
        }
    }

    private sealed record ScreenshotRequest(Dx12PendingScreenshot Screenshot, string? Label);
}
