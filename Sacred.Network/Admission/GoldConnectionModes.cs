namespace Sacred.Network.Admission;

public static class GoldConnectionModes
{
    public static string Label(GoldConnectionMode mode) => mode switch
    {
        GoldConnectionMode.LowBandwidth => "Low bandwidth",
        GoldConnectionMode.LanHighBandwidth => "LAN / high bandwidth",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static string Argument(GoldConnectionMode mode) => mode switch
    {
        GoldConnectionMode.LowBandwidth => "low",
        GoldConnectionMode.LanHighBandwidth => "lan",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static bool TryParse(string value, out GoldConnectionMode mode)
    {
        mode = value.ToLowerInvariant() switch
        {
            "lan" or "high" => GoldConnectionMode.LanHighBandwidth,
            "low" => GoldConnectionMode.LowBandwidth,
            _ => 0
        };
        return mode != 0;
    }
}
