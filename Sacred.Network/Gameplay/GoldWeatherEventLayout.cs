namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x14C, decoded at 0x7DEE14 into cEventWeather. Three UInt32 words.</summary>
public static class GoldWeatherEventLayout
{
    public const ushort Opcode = 0x14C;
    public const int Size = 12;
    public const int KindOffset = 0;
    public const int IntensityOffset = 4;
    public const int ParameterOffset = 8;
}

public enum GoldWeatherEventKind : uint
{
    None = 0,
    Reset = 1,
    Rain = 2,
    Fog = 3,
    GroundFog = 4,
    Snow = 5,
    Reserved = 6,
    Quake = 7,
    Night = 8,
    FlashOnce = 9,
    EnterSector = 10,
    ExitSector = 11,
    Stomp = 12
}
