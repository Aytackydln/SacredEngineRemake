using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Original environment bytes are retained, including uninterpreted world blocks; weather has a separate typed cache.</summary>
public sealed record GoldWorldEnvironmentEvent(GoldMessage Message) : GoldGameEvent(Message)
{
    public bool IsRequest => Message.Payload.Length == 0;
    public GoldCalendarState? Calendar => !IsRequest && Message.Payload[GoldWorldEnvironmentLayout.CalendarPresentOffset] != 0
        ? GoldCalendarState.FromPayload(Message.Payload.AsSpan(GoldWorldEnvironmentLayout.CalendarOffset, GoldCalendarState.Size)) : null;
    public ReadOnlySpan<byte> Weather => !IsRequest && Message.Payload[GoldWorldEnvironmentLayout.WeatherPresentOffset] != 0
        ? Message.Payload.AsSpan(GoldWorldEnvironmentLayout.WeatherOffset, GoldWorldEnvironmentLayout.WeatherSize) : [];
}
