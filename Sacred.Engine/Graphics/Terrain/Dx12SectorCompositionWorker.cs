using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Schedules and collects fence-safe work on the dedicated sector-composition queue.</summary>
internal sealed class Dx12SectorCompositionWorker : IDisposable
{
    private readonly Dx12SectorComposer _composer;
    private readonly Dx12SectorTargetPool _targets = new();
    private readonly SectorCompositionRequestQueue _requests;
    private readonly ConcurrentQueue<SubmittedSectorComposition> _completed = new();
    private readonly List<InFlightSectorComposition> _inFlight =
        new(Dx12SectorComposer.MaximumInFlightCompositions);
    private readonly AutoResetEvent _compositionOpportunity = new(false);
    private readonly Func<TerrainSectorComposition, bool> _isWanted;
    private Thread? _thread;
    private volatile bool _stopped;
    private SectorCompositionRequest? _preparing;

    public Dx12SectorCompositionWorker(
        ID3D12Device device,
        Dx12TextureUploader uploader,
        Dx12TextureUploadWorker uploads,
        int queueCapacity,
        Func<TerrainSectorComposition, bool> isWanted)
    {
        _composer = new Dx12SectorComposer(device, uploader, _targets, uploads);
        _requests = new SectorCompositionRequestQueue(queueCapacity);
        _isWanted = isWanted;
        _thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Sacred GPU sector compositor",
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    public void UpdateSchedule(Vector2 cameraWorldCenter, Vector2 movementDirection) =>
        _requests.UpdateSchedule(cameraWorldCenter, movementDirection);

    public bool TryEnqueue(SectorCompositionRequest request) => _requests.TryEnqueue(request);

    public bool TryDequeueCompleted(out SubmittedSectorComposition composition) =>
        _completed.TryDequeue(out composition!);

    public void OnForegroundFrameSubmitted() => _compositionOpportunity.Set();

    public void RecycleTargets(Dx12ComposedSector targets) => _targets.Return(targets);

    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _compositionOpportunity.Set();
        _thread?.Join();
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _compositionOpportunity.Dispose();
        _composer.Dispose();
        _targets.Dispose();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _compositionOpportunity.WaitOne();
            _targets.CollectReturned();
            CollectFinishedCompositions();

            foreach (var obsolete in _requests.RemoveWhere(
                         request => _stopped || !_isWanted(request.Composition)))
            {
                _completed.Enqueue(Skip(obsolete));
            }

            if (_stopped)
            {
                if (_preparing is { } abandoned) _completed.Enqueue(Skip(abandoned));
                _preparing = null;
                CompleteInFlightCompositions();
                return;
            }

            if (_preparing is { } obsoleteRequest && !_isWanted(obsoleteRequest.Composition))
            {
                _completed.Enqueue(Skip(obsoleteRequest));
                _preparing = null;
            }
            if (_inFlight.Count >= Dx12SectorComposer.MaximumInFlightCompositions)
            {
                continue;
            }
            if (_preparing is null)
            {
                if (!_requests.TryDequeue(request => _isWanted(request.Composition), out var request)) continue;
                _preparing = request;
            }
            try
            {
                if (!_composer.PrepareSources(_preparing.Composition)) continue;
                Submit(_preparing);
            }
            catch (Exception exception)
            {
                _preparing.Composition.ReleaseSourceTiles();
                _completed.Enqueue(Failed(_preparing, exception));
            }
            _preparing = null;
        }
    }

    private void Submit(SectorCompositionRequest request)
    {
        try
        {
            var submission = _composer.Submit(request.Composition);
            _inFlight.Add(new InFlightSectorComposition(request, submission));
        }
        catch (Exception exception)
        {
            _completed.Enqueue(Failed(request, exception));
        }
        finally
        {
            // Recording copies all source pixels and instances into fence-tracked GPU resources.
            request.Composition.ReleaseSourceTiles();
        }
    }

    private void CollectFinishedCompositions()
    {
        for (var index = _inFlight.Count - 1; index >= 0; index--)
        {
            var inFlight = _inFlight[index];
            if (!_composer.TryComplete(inFlight.Submission, out var composed))
                continue;

            _completed.Enqueue(Complete(inFlight.Request, composed!));
            _inFlight.RemoveAt(index);
        }
    }

    private void CompleteInFlightCompositions()
    {
        foreach (var inFlight in _inFlight)
        {
            try
            {
                _completed.Enqueue(Complete(
                    inFlight.Request,
                    _composer.Complete(inFlight.Submission)));
            }
            catch (Exception exception)
            {
                _completed.Enqueue(Failed(inFlight.Request, exception));
            }
        }

        _inFlight.Clear();
    }

    private static SubmittedSectorComposition Complete(
        SectorCompositionRequest request,
        Dx12ComposedSector composed) =>
        CreateResult(request, composed, null);

    private static SubmittedSectorComposition Failed(
        SectorCompositionRequest request,
        Exception exception) =>
        CreateResult(request, null, exception);

    private static SubmittedSectorComposition Skip(SectorCompositionRequest request)
    {
        request.Composition.ReleaseSourceTiles();
        return CreateResult(request, null, null);
    }

    private static SubmittedSectorComposition CreateResult(
        SectorCompositionRequest request,
        Dx12ComposedSector? composed,
        Exception? error) =>
        new(
            request.Composition.Coord,
            request.Composition,
            composed,
            request.BaseSrvSlot,
            request.LiquidCoverSrvSlot,
            error);

    private sealed record InFlightSectorComposition(
        SectorCompositionRequest Request,
        Dx12SectorComposer.Submission Submission);
}
