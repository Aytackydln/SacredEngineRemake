namespace Sacred.GameplayHost.Networking;

/// <summary>Compared 0x8092F0 zero-delay local message delivery. Listeners are
/// caller-owned, synchronous and reentrant. This is neither a packet sender nor
/// the nonzero-delay clone/queue path of the original message manager.</summary>
public sealed class WorldImmediateMessageDispatcher<TMessage>(WorldImmediateMessageServices<TMessage> services,
    uint initialDirectCalls = 0) where TMessage : class
{
    public uint DirectCalls { get; private set; } = initialDirectCalls;

    public void Dispatch(TMessage message, uint receiverMask = 0)
    {
        ArgumentNullException.ThrowIfNull(message);
        DirectCalls = unchecked(DirectCalls + 1);
        var count = Count();
        if (count == 0) return;
        if (receiverMask == 0)
        {
            var family = services.ReadFamily(message);
            switch (family)
            {
                case 5 or 11: services.ReadListener(0)?.Invoke(message); break;
                // Native default routing does not check count >= 2 here.
                case 6 or 7: services.ReadListener(1)?.Invoke(message); break;
                default: services.ReportUnsupportedFamily(family); break;
            }
            return;
        }
        if ((receiverMask & 1) != 0 && Count() >= 1) services.ReadListener(0)?.Invoke(message);
        // The first callback may change both extent and backing, or dispatch a
        // nested message. Read current storage again, rather than a snapshot.
        if ((receiverMask & 2) != 0 && Count() >= 2) services.ReadListener(1)?.Invoke(message);
    }

    private int Count()
    {
        var count = services.ListenerCount();
        if (count < 0) throw new NotSupportedException("Immediate delivery requires established nonnegative listener extent/backing.");
        return count;
    }
}
