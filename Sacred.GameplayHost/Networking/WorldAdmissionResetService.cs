namespace Sacred.GameplayHost.Networking;

/// <summary>Compared 0x417E70 lazy publication, 0x417EE0 clear-before-free, and
/// 0x7F5E10 entry reset. Factory/free own real resource storage and may reenter;
/// release does not destroy a replacement published by that reentry.</summary>
public sealed class WorldAdmissionResetService
{
    private readonly Func<WorldAdmissionResetState?> _create;
    private readonly Action<WorldAdmissionResetState> _free;
    public WorldAdmissionResetService(Func<WorldAdmissionResetState?> create, Action<WorldAdmissionResetState> free)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _free = free ?? throw new ArgumentNullException(nameof(free));
    }
    public WorldAdmissionResetState? Current { get; private set; }
    public WorldAdmissionResetState? Resolve()
    {
        if (Current is null) Current = _create();
        return Current;
    }
    public void ResetEntry()
    {
        var service = Resolve() ?? throw new NotSupportedException("Native reset requires a constructed resource service.");
        service.ResetEntry();
    }
    public void Release()
    {
        var previous = Current;
        if (previous is null) return;
        Current = null;
        _free(previous);
    }
}
