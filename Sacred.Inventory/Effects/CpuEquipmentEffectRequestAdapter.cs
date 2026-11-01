using System;

namespace Sacred.Inventory.Effects;

/// <summary>Consumes ordered effect commands using the existing simulation implementation.</summary>
public sealed class CpuEquipmentEffectRequestAdapter
    : IEquipmentEffectRequestSink
{
    private readonly EquipmentEffectScene _scene;
    internal CpuEquipmentEffectRequestAdapter(EquipmentEffectScene scene) => _scene = scene;
    public void Submit(in EquipmentEffectRequest request)
    {
        if (request.Instance != _scene.Playback.Instance)
            throw new InvalidOperationException("Equipment request belongs to an expired or different instance.");
        switch (request.Operation)
        {
            case EquipmentEffectOperation.Pose:
                var source = request.PoseSource ?? throw new InvalidOperationException("Effect pose source is missing.");
                if (request.PoseRequest is { } captured && captured.Instance != source.Playback.Instance)
                    throw new InvalidOperationException("Equipment request references an expired pose generation.");
                // Async owners deliberately render a completed pose behind the latest request.
                // Forcing that request synchronously both duplicates worker work and
                // makes a CPU render mesh stale. Attachments must follow the same
                // published pose as the body.
                if ((!source.Playback.Cpu.Asynchronous || !source.Pose.IsEvaluated) &&
                    request.PoseRequest is { } pose && source.Playback.Cpu.EvaluatedSequence != pose.Sequence)
                    source.Playback.Cpu.Evaluate(pose with { MaterializeCpuVertices = source.MaterializeCpuVertices });
                _scene.ApplyPoseCpu(source, request.DeltaSeconds);
                break;
            case EquipmentEffectOperation.Advance: _scene.AdvanceCpu(request.DeltaSeconds); break;
            case EquipmentEffectOperation.Rebase: _scene.RebaseCpu(request.PreviousWorld, request.CurrentWorld); break;
            case EquipmentEffectOperation.Reset: _scene.ResetCpu(); break;
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
    }
}

