using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Projects retained file values into the opcode-specific network layout.</summary>
public static class GoldSavedCreatureValuesImporter
{
    public static GoldCreatureValuesState Import(SacredSavedCreatureValues source, ushort opcode, uint runtimeWord148 = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        var records22 = new byte[source.Count22 * GoldCreatureValuesLayout.Record22Size];
        var records38 = new byte[source.Count38 * GoldCreatureValuesLayout.Record38Size];
        for (var i = 0; i < source.Count22; i++) source.GetRecord22(i).CopyTo(records22.AsSpan(i * GoldCreatureValuesLayout.Record22Size));
        for (var i = 0; i < source.Count38; i++) source.GetRecord38(i).CopyTo(records38.AsSpan(i * GoldCreatureValuesLayout.Record38Size));
        return GoldCreatureValuesWriter.Write(opcode, source.Block08, source.Block128, records22, records38, runtimeWord148);
    }

}
