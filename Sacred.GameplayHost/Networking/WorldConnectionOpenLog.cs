namespace Sacred.GameplayHost.Networking;

/// <summary>Original formatter boundaries; text/time/SDK error formatting belongs
/// to the installed definitions and supplied logger, not fixed host messages.</summary>
public enum WorldConnectionOpenLog
{
    SocketStartupFailed, Opening, SocketCreatedFailed, SocketOptionFailed, BindFailed,
    SocketBound, TransportCreated, TransportLogFailed, TransportInitializeFailed,
    ValuesApplyFailed, ValuesCreateFailed, OptionalBackendFailed
}
