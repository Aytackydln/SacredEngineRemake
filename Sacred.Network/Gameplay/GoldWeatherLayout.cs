namespace Sacred.Network.Gameplay;

/// <summary>Gold SaveNetworkData (0x41C080). This is not the Demo weather structure.</summary>
public static class GoldWeatherLayout
{
    public const int Size = 32;
    public const int FlagsOffset = 0;
    public const int RainParticlesOffset = 4;
    public const int FogOpacityOffset = 8;
    public const int SnowParticlesOffset = 12;
    public const int QuakeStrengthOffset = 16;
    // +20 and +24 are zero-filled by Gold, not ground fog or stomp strength.
    public const int LavaBurstTimerOffset = 28;
    public const uint RainFlag = 1;
    public const uint FogFlag = 2;
    public const uint LightningFlag = 4;
    public const uint SnowFlag = 0x10;
    public const uint QuakeFlag = 0x80;
}
