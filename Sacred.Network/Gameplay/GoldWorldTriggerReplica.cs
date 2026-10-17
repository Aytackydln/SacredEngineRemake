using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Received trigger slots and ordered work requiring native world callbacks.</summary>
public sealed class GoldWorldTriggerReplica
{
    private readonly Dictionary<uint, GoldWorldTriggerState> _records = [];
    private readonly List<GoldWorldTriggerEvent> _pending = [];
    public GoldWorldTriggerReplica()
    {
        Records = new ReadOnlyDictionary<uint, GoldWorldTriggerState>(_records);
        Pending = _pending.AsReadOnly();
    }
    public IReadOnlyDictionary<uint, GoldWorldTriggerState> Records { get; }
    public IReadOnlyList<GoldWorldTriggerEvent> Pending { get; }

    /// <summary>Receive-side 0x6387B0. Gapped growth waits for support for native
    /// partially uninitialized gap records; contiguous append/replacement is exact.</summary>
    public bool TryCreate(GoldWorldTriggerEvent update)
    {
        if (update.Message.Opcode != GoldWorldTriggerCodec.CreateOpcode) throw new ArgumentException("Expected trigger creation.", nameof(update));
        if (_pending.Count != 0 || update.Reference > (uint)_records.Count) { Defer(update); return false; }
        var slot = update.Reference == 0 ? (uint)_records.Count : update.Reference;
        var created = new GoldWorldTriggerState(slot, unchecked((ushort)(update.Value | 0x80)), unchecked((ushort)update.InitialState), 0, 0);
        if (!GoldWorldTriggerTransition.TryApply(created, update.InitialState, true, out var next)) { Defer(update); return false; }
        _records[slot] = next;
        return true;
    }

    public void Apply(GoldWorldTriggerEvent update)
    {
        if (update.Message.Opcode == GoldWorldTriggerCodec.CreateOpcode) { TryCreate(update); return; }
        // Once world effects are pending, preserve subsequent trigger message order.
        // A later bank cannot prove that callbacks from an earlier message executed.
        if (_pending.Count != 0)
        {
            Defer(update);
            return;
        }
        if (!_records.TryGetValue(update.Reference, out var previous)) return; // Native missing-slot no-op.
        if (GoldWorldTriggerTransition.TryApply(previous, update.Value, update.Message.Opcode == GoldWorldTriggerCodec.SetOpcode, out var next))
            _records[update.Reference] = next;
        else Defer(update);
    }

    private void Defer(GoldWorldTriggerEvent update) => _pending.Add(update with
    { Message = update.Message with { Payload = update.Message.Payload.ToArray() } });
    public void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank1Opcode || bank.Sections[6].Records.Count == 0) return;
        var records = bank.Sections[6].Records.Select(row => GoldWorldTriggerState.FromPayload(row.Data)).ToArray();
        _records.Clear();
        for (uint slot = 0; slot < records.Length; slot++) _records.Add(slot, records[slot]);
    }
    public void SetReferent(uint reference, uint resource)
    {
        if (_records.TryGetValue(reference, out var previous)) _records[reference] = previous with { Referent = resource };
    }

    /// <summary>cObject3D action 0x21: network dispatch removes control locks before
    /// the ordinary set/clear. Unsupported callback triggers remain pending.</summary>
    public bool TryActivateDoor(uint reference, bool open)
    {
        if (_pending.Count != 0 || !_records.TryGetValue(reference, out var previous)) return false;
        var unlocked = previous;
        if ((previous.State & 0x8000) != 0 &&
            !GoldWorldTriggerTransition.TryApply(previous, 0xc000, false, out unlocked)) return false;
        if (!GoldWorldTriggerTransition.TryApply(unlocked, 1, open, out var next)) return false;
        _records[reference] = next;
        return true;
    }
    public void Clear() { _records.Clear(); _pending.Clear(); }
}
