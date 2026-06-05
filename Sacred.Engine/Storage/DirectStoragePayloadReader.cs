using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets;
using Vortice.DirectStorage;

namespace Sacred.Engine.Storage;

public sealed class DirectStoragePayloadReader : IPakPayloadReader, IDisposable
{
    private const ushort QueueCapacity = 32;
    private const uint StatusIndex = 0;

    private readonly IDStorageFactory _factory;
    private readonly IDStorageQueue _queue;
    private readonly IDStorageStatusArray _statusArray;
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly Dictionary<string, IDStorageFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unsupportedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private DirectStoragePayloadReader(
        IDStorageFactory factory,
        IDStorageQueue queue,
        IDStorageStatusArray statusArray)
    {
        _factory = factory;
        _queue = queue;
        _statusArray = statusArray;
    }

    public static DirectStoragePayloadReader? TryCreate()
    {
        try
        {
            var factory = DirectStorage.DStorageGetFactory<IDStorageFactory>();
            var queue = factory.CreateQueue<IDStorageQueue>(new QueueDesc
            {
                SourceType = RequestSourceType.File,
                Capacity = QueueCapacity,
                Priority = Priority.Low,
                Name = "Sacred texture payload reads"
            });
            var statusArray = factory.CreateStatusArray(1, "Sacred texture payload status");
            return new DirectStoragePayloadReader(factory, queue, statusArray);
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask<byte[]?> TryReadAsync(
        string path,
        long offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        if (length == 0)
            return [];
        if (offset < 0 || length < 0)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        await _queueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = TryOpenFile(path);
            if (file is null)
                return null;

            var payload = new byte[length];
            var payloadHandle = GCHandle.Alloc(payload, GCHandleType.Pinned);
            try
            {
                var request = new Request
                {
                    Options = new RequestOptions
                    {
                        CompressionFormat = CompressionFormat.None,
                        SourceType = RequestSourceType.File,
                        DestinationType = RequestDestinationType.Memory
                    },
                    Source = new Source
                    {
                        File = new SourceFile
                        {
                            Source = file,
                            Offset = checked((ulong)offset),
                            Size = checked((uint)length)
                        }
                    },
                    Destination = new Destination
                    {
                        Memory = new DestinationMemory
                        {
                            Buffer = payloadHandle.AddrOfPinnedObject(),
                            Size = checked((uint)length)
                        }
                    },
                    UncompressedSize = checked((uint)length),
                    Name = "Texture payload"
                };

                _queue.EnqueueRequest(request);
                _queue.EnqueueStatus(_statusArray, StatusIndex);
                _queue.Submit();

                while (!_statusArray.IsComplete(StatusIndex))
                    await Task.Delay(1).ConfigureAwait(false);

                _statusArray.GetHResult(StatusIndex);
                cancellationToken.ThrowIfCancellationRequested();
                return payload;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                try
                {
                    _queue.RetrieveErrorRecord();
                }
                catch
                {
                }

                return null;
            }
            finally
            {
                payloadHandle.Free();
            }
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private IDStorageFile? TryOpenFile(string path)
    {
        if (_unsupportedPaths.Contains(path))
            return null;

        if (_files.TryGetValue(path, out var cached))
            return cached;

        try
        {
            var file = _factory.OpenFile(path);
            _files.Add(path, file);
            return file;
        }
        catch
        {
            _unsupportedPaths.Add(path);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var file in _files.Values)
            file.Dispose();

        _queue.Close();
        _queue.Dispose();
        _statusArray.Dispose();
        _factory.Dispose();
        _queueLock.Dispose();
    }
}
