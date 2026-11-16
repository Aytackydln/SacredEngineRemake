namespace Sacred.GameplayHost.Networking;

/// <summary>Current listener extent/backing and virtual message services.
/// Default routing can read physical slot one with logical count one; backing
/// outside the logical extent must be established explicitly by its owner.</summary>
public sealed record WorldImmediateMessageServices<TMessage>(Func<int> ListenerCount,
    Func<int, Action<TMessage>?> ReadListener, Func<TMessage, uint> ReadFamily,
    Action<uint> ReportUnsupportedFamily) where TMessage : class;
