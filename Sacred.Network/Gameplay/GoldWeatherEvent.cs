using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldWeatherEvent(GoldMessage Message) : GoldGameEvent(Message)
{
    public GoldWeatherEventKind Kind => (GoldWeatherEventKind)Word(GoldWeatherEventLayout.KindOffset);
    public uint Intensity => Word(GoldWeatherEventLayout.IntensityOffset);
    public uint Parameter => Word(GoldWeatherEventLayout.ParameterOffset);
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Message.Payload.AsSpan(offset));
}
