namespace Sacred.Network.Gameplay;

/// <summary>Current observed role, seeded by admission and changed independently of world-start approvals.</summary>
public sealed class GoldClientHostRoleReplica
{
    public bool? IsClientHost { get; private set; }
    public long NotificationCount { get; private set; }
    public long ChangeCount { get; private set; }
    public string? LastNotificationKey { get; private set; }
    internal void Apply(GoldClientHostRoleEvent entry)
    {
        var decoded = GoldClientHostRoleCodec.Decode(entry.Message);
        var previous = IsClientHost ?? false;
        IsClientHost = decoded.IsClientHost; NotificationCount++;
        if (previous == decoded.IsClientHost) return;
        ChangeCount++;
        LastNotificationKey = decoded.IsClientHost ? "UI_ERROR_HOST_TRANSFERED" : "UI_ERROR_HOST_TRANSFERED2";
    }
    internal void Seed(GoldAdmissionEvent admission) => IsClientHost = (admission.Flags & GoldWorldEntryCodec.AssignedHostFlag) != 0;
    internal void Clear() { IsClientHost = null; NotificationCount = ChangeCount = 0; LastNotificationKey = null; }
}
