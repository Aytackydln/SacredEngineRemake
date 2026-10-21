namespace Sacred.Network.Gameplay;

/// <summary>Gold client 0x7E108F: empty 0x0B request, or a 508-byte server environment snapshot.</summary>
public static class GoldWorldEnvironmentLayout
{
    public const ushort Opcode = 0x0B;
    public const int Size = 0x1FC;
    public const int CalendarPresentOffset = 0;
    public const int WeatherPresentOffset = 1;
    public const int CalendarOffset = 4;
    public const int WeatherOffset = 20;
    public const int WeatherSize = GoldWeatherLayout.Size;
}
