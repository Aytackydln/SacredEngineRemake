using System.Numerics;
using System.Threading;
using Sacred.Granny.Animation;

namespace Sacred.Inventory.Effects;

/// <summary>Control-only lifecycle; CPU simulation is an interchangeable explicit adapter.</summary>
public sealed class EquipmentEffectPlayback
{
    private static long _nextIdentity;
    private ulong _sequence;
    internal EquipmentEffectPlayback(EquipmentEffectScene scene)
    {
        Instance = new(Interlocked.Increment(ref _nextIdentity), 1);
        Cpu = new(scene);
        Sink = Cpu;
    }
    public EquipmentEffectHandle Instance { get; private set; }
    public CpuEquipmentEffectRequestAdapter Cpu { get; }
    public IEquipmentEffectRequestSink Sink { get; set; }
    public EquipmentEffectRequest? LastRequest { get; private set; }
    public EquipmentEffectRequest Submit(EquipmentEffectOperation operation,
        GrnAnimatedMesh? pose = null, float deltaSeconds = 0,
        Matrix4x4 previousWorld = default, Matrix4x4 currentWorld = default)
    {
        var request = new EquipmentEffectRequest(Instance, checked(++_sequence), operation,
            pose, pose?.Playback.LastRequest, deltaSeconds, previousWorld, currentWorld);
        LastRequest = request;
        Sink.Submit(request);
        return request;
    }
    public void InvalidateRequests()
    {
        Instance = Instance with { Generation = checked(Instance.Generation + 1) };
        LastRequest = null;
    }
}
