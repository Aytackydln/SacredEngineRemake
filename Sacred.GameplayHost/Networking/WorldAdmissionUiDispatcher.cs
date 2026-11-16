using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Original 0x553210 UI request constructor installs vtable 0x890DD0;
/// its 0x4B2490 virtual family getter returns five. Listener ownership remains
/// with the caller's existing presentation/message manager.</summary>
public sealed class WorldAdmissionUiDispatcher
{
    private readonly WorldImmediateMessageDispatcher<GoldLobbyUiRequest> _dispatcher;
    public WorldAdmissionUiDispatcher(Func<int> listenerCount, Func<int, Action<GoldLobbyUiRequest>?> readListener)
    {
        ArgumentNullException.ThrowIfNull(listenerCount);
        ArgumentNullException.ThrowIfNull(readListener);
        _dispatcher = new(new(listenerCount, readListener, _ => 5,
            _ => throw new InvalidOperationException("UI request has an unsupported native message family.")));
    }
    public uint DirectCalls => _dispatcher.DirectCalls;
    public void Dispatch(GoldLobbyUiRequest request) => _dispatcher.Dispatch(request);
}
