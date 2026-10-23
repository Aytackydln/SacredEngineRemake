namespace Sacred.Network.Gameplay;

/// <summary>Retain native append order and duplicate registrations. Script execution is a separate world responsibility.</summary>
public sealed class GoldDeathScriptRegistry
{
    private readonly List<GoldDeathScriptRegistration> _registrations = [];
    public GoldDeathScriptRegistry() => Registrations = _registrations.AsReadOnly();
    public IReadOnlyList<GoldDeathScriptRegistration> Registrations { get; }
    public long CleanupNotificationCount { get; private set; }
    public long RemovedRegistrationCount { get; private set; }
    public long OversizedResetCount { get; private set; }

    internal void Apply(GoldGameEvent entry)
    {
        if (entry is GoldDeathScriptEvent registration)
        {
            Register(GoldDeathScriptCodec.Decode(registration.Message).Registration);
            return;
        }
        var reference = GoldObjectCallbackCleanupCodec.Decode(entry.Message).ObjectReference;
        CleanupNotificationCount++;
        if (_registrations.Count > 60000)
        {
            RemovedRegistrationCount += _registrations.Count;
            _registrations.Clear(); OversizedResetCount++;
            return;
        }
        for (var index = 0; index < _registrations.Count;)
        {
            var row = _registrations[index];
            var name = row.ScriptText;
            // Receive passes death=true: ordinary names and See: qualify. Other colon callbacks survive.
            // wegw is a native retained marker. Removal swaps in the last record and restarts the scan.
            if (row.ObjectReference != reference || name.Contains(':') && !name.StartsWith("See:", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("wegw", StringComparison.OrdinalIgnoreCase)) { index++; continue; }
            _registrations[index] = _registrations[^1];
            _registrations.RemoveAt(_registrations.Count - 1);
            RemovedRegistrationCount++;
            index = 0;
        }
    }

    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldObjectScriptCallbackCodec.BankOpcode) return;
        var rows = bank.Sections[GoldObjectScriptCallbackCodec.BankSection].Records;
        if (rows.Count == 0) return; // Gold 0x7E5F86 skips a zero-count import.
        var records = rows.Select(row => GoldDeathScriptRegistration.FromPayload(row.Data)).ToArray();
        _registrations.Clear(); _registrations.AddRange(records);
    }

    public void Register(GoldDeathScriptRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registrations.Add(registration);
    }

    public void Clear()
    {
        _registrations.Clear(); CleanupNotificationCount = 0; RemovedRegistrationCount = 0; OversizedResetCount = 0;
    }
}
