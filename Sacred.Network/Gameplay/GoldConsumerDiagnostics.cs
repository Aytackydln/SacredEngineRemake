namespace Sacred.Network.Gameplay;

/// <summary>Bounded observation metadata; no payload history or mutation of gameplay state.</summary>
public sealed class GoldConsumerDiagnostics
{
    private readonly Dictionary<ushort, long> _opcodes = [];
    private readonly Dictionary<string, long> _outcomes = [];
    private readonly Dictionary<string, long> _faults = [];
    public Action<string>? Log { get; set; }
    public string Trace { get; set; } = "off";
    public string Mark { get; private set; } = "session";
    public long Received { get; private set; }
    public IReadOnlyDictionary<ushort, long> Opcodes => _opcodes;
    public IReadOnlyDictionary<string, long> Outcomes => _outcomes;
    public IReadOnlyDictionary<string, long> Faults => _faults;
    internal void ClearFault(string consumer) => _faults.Remove(consumer);
    internal void Fault(string consumer, long lost)
    {
        if (!_faults.ContainsKey(consumer))
            Log?.Invoke($"Consumer delivery fault: {consumer} lost retained work; consumption stopped. Reconnect or explicitly rebuild its baseline. Networking continues.");
        _faults[consumer] = lost;
    }
    public void SetMark(string label)
    {
        Mark = label; Received = 0; _opcodes.Clear(); _outcomes.Clear();
        Log?.Invoke($"Consumer observation mark: {label}; replicas preserved.");
    }
    internal void Observe(GoldGameEvent entry, bool changed)
    {
        Received++; _opcodes[entry.Message.Opcode] = _opcodes.GetValueOrDefault(entry.Message.Opcode) + 1;
        if (Trace == "all") Log?.Invoke($"Consumer receipt [{Mark}]: 0x{entry.Message.Opcode:X3}, sender {entry.Message.Sender}, origin {entry.Message.Origin}, sequence {entry.Message.Sequence}; replica changed {changed}; {entry.GetType().Name}.");
    }
    internal void Publish(GoldConsumerWork work)
    {
        if (Matches(work)) Log?.Invoke($"Consumer work [{Mark}]: session {work.Session}, order {work.Order}, 0x{work.Opcode:X3}, sender {work.Sender}, origin {work.Origin}, sequence {work.Sequence}; {Describe(work.Value)}.");
    }
    internal void Report(GoldConsumerReport report)
    {
        var key = $"{report.Consumer}/{report.Outcome}";
        _outcomes[key] = _outcomes.GetValueOrDefault(key) + 1;
        if (Matches(report.Work)) Log?.Invoke($"Consumer result [{Mark}]: {report.Consumer}, order {report.Work.Order}, 0x{report.Work.Opcode:X3}; {report.Outcome}: {report.Detail}.");
    }
    private bool Matches(GoldConsumerWork work) => Trace == "all" || Trace == work.Family
        || Trace == "texts" && work.Value is GoldCombatNotification { Event: GoldCreatureTextEvent or GoldCreatureDamageEvent };
    private static string Describe(object value) => value switch
    {
        GoldCombatNotification notice => $"subject {notice.Event.ObjectReference}, instance {notice.InstanceVersion}; {notice.Event.GetType().Name}",
        GoldCreatureReceiveWork action => $"subject {action.Object.BaseState.ObjectReference}, instance {action.Object.InstanceVersion}; {action.Event.GetType().Name}",
        GoldAttachedFxRequest fx => $"wearer {fx.Event.WearerReference}, instance {fx.WearerInstance}, generation {fx.Generation}, resource {fx.Event.Resource}, subtype {fx.Event.FxSubtype}",
        GoldCounterHudMessage hud => $"HUD mode {hud.Mode}, flags 0x{hud.Flags:X8}, text resource {hud.TextResource}, target resource {hud.TargetResource}",
        GoldRegionCountdownWarning warning => $"region {warning.RegionIndex}, resource key {warning.ResourceKey}",
        _ => value.GetType().Name
    };
    internal void Clear() { Received = 0; _opcodes.Clear(); _outcomes.Clear(); _faults.Clear(); Mark = "session"; }
}
