using System.Buffers.Binary;
using System.Numerics;
using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>Native special FX field mutations. KEYX resolves logical anchors; special particle playback remains a consumer.</summary>
public sealed class GoldSpecialFxReplica
{
    private readonly Dictionary<uint, GoldReplicatedObject> _objects;
    private readonly Action _lateChange;
    private SacredSectorGrid? _grid;
    internal GoldSpecialFxReplica(Dictionary<uint, GoldReplicatedObject> objects, Action lateChange) => (_objects, _lateChange) = (objects, lateChange);
    public long AppliedCount { get; private set; }
    public long MissingOrInactiveCount { get; private set; }
    public int PendingRelocationCount => _objects.Values.Count(CanRelocate);
    private static bool CanRelocate(GoldReplicatedObject obj) => obj.SpecialFx?.RelocationPending == true &&
        obj.Fx is not null && (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0;
    public void ConfigureSectors(SacredSectorIndex index)
    {
        ArgumentNullException.ThrowIfNull(index); _grid = index.CreateGrid();
        var changed = PendingRelocationCount != 0;
        foreach (var obj in _objects.Values.ToArray())
            if (CanRelocate(obj)) _objects[obj.BaseState.ObjectReference] = Relocate(obj, obj.SpecialFx!);
        if (changed) _lateChange();
    }
    internal bool Apply(GoldSpecialFxEvent incoming)
    {
        var entry = GoldSpecialFxCodec.Decode(incoming.Message);
        if (!_objects.TryGetValue(entry.ObjectReference, out var obj) || obj.Fx is null ||
            (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
        { MissingOrInactiveCount++; return false; }
        var state = obj.SpecialFx ?? new GoldSpecialFxRuntime();
        state = state with { Version = state.Version + 1 };
        var payload = obj.Fx.ToPayload();
        switch (entry)
        {
            case GoldSpecialFxValueEvent value:
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldObjectFxStateLayout.WearerReferenceOffset), value.Value);
                break;
            case GoldPingPongBallEvent ball:
                entry.Message.Payload.AsSpan(16, 12).CopyTo(payload.AsSpan(GoldObjectFxStateLayout.MoveDirectionOffset));
                state = state with
                {
                    PositionRequest = new(ball.X, ball.Y, ball.Surface, 0), PositionOpcode = entry.Message.Opcode, RelocationPending = true,
                    ParticlePosition = ball.ParticlePosition, ParticleClockWord = GoldSpecialFxCodec.ParticleClockWord(ball.ClockBits),
                    ParticleFlags = ball.SetsParticleFlag ? (ushort)8 : state.ParticleFlags
                };
                break;
            case GoldPacmanGhostEvent ghost:
                entry.Message.Payload.AsSpan(16, 12).CopyTo(payload.AsSpan(GoldObjectFxStateLayout.VectorData1Offset));
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldObjectFxStateLayout.FloatDataOffset), ghost.FloatDataBits);
                entry.Message.Payload.AsSpan(32, 12).CopyTo(payload.AsSpan(GoldObjectFxStateLayout.IntegerDataOffset));
                state = state with { PositionRequest = new(ghost.X, ghost.Y, ghost.Surface, 0), PositionOpcode = entry.Message.Opcode,
                    RelocationPending = true, ParticlePosition = null,
                    ProjectedPositionInput = new Vector3(ghost.X, ghost.Y, 0) };
                break;
        }
        _objects[entry.ObjectReference] = Relocate(obj with { Fx = GoldObjectFxState.FromPayload(payload) }, state);
        AppliedCount++; return true;
    }
    private GoldReplicatedObject Relocate(GoldReplicatedObject obj, GoldSpecialFxRuntime state)
    {
        if (state.PositionRequest is not { } request || !state.RelocationPending || _grid is null) return obj with { SpecialFx = state };
        // Gold 0x6224E0 divides signed native coordinates in x87 precision and truncates toward zero, without the KEYX-origin bias.
        var sector = _grid.GetSector((int)(request.X / (double)SacredSectorCoordinates.WorldUnitsPerTile),
            (int)(request.Y / (double)SacredSectorCoordinates.WorldUnitsPerTile));
        state = state with { RelocationPending = false };
        if (sector == 0 || !_grid.HasSector(sector)) return obj with { SpecialFx = state };
        var basis = obj.BaseState with { X = request.X, Y = request.Y, Surface = (byte)request.Surface, Sector = (ushort)sector };
        var bytes = obj.Fx!.ToPayload(); basis.ToPayload().CopyTo(bytes, 0);
        return obj with { BaseState = basis, Fx = GoldObjectFxState.FromPayload(bytes), SpecialFx = state,
            PositionOpcode = state.PositionOpcode,
            PositionVersion = obj.PositionVersion + 1 };
    }
    internal void Clear() { _grid = null; AppliedCount = MissingOrInactiveCount = 0; }
}
