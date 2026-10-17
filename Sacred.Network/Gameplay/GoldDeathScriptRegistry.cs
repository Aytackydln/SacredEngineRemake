namespace Sacred.Network.Gameplay;

/// <summary>Retain native append order and duplicate registrations. Script execution is a separate world responsibility.</summary>
public sealed class GoldDeathScriptRegistry
{
    private readonly List<GoldDeathScriptRegistration> _registrations = [];
    public GoldDeathScriptRegistry() => Registrations = _registrations.AsReadOnly();
    public IReadOnlyList<GoldDeathScriptRegistration> Registrations { get; }

    public void Register(GoldDeathScriptRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registrations.Add(registration);
    }

    public void Clear() => _registrations.Clear();
}
