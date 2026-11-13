using System.Buffers.Binary;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x48E438..0x48E4B0: SetHP percentage, maximum fallback and
/// current-health write. Zero does not dispatch death. Positive dead-mode revival
/// remains guarded; no result recomputation or startup publication occurs.</summary>
public static class WorldCreatureScriptHitPoints
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, int percentage)
    {
        var health = actor.Component.Health.ToArray();
        var maximum = BinaryPrimitives.ReadInt32LittleEndian(health.AsSpan(SacredCreatureHealthLayout.MaximumHitPointsOffset));
        if (maximum <= 0)
        {
            maximum = (BinaryPrimitives.ReadUInt16LittleEndian(actor.Component.Values.AsSpan(78)) + 1) * 100;
            BinaryPrimitives.WriteInt32LittleEndian(health.AsSpan(SacredCreatureHealthLayout.MaximumHitPointsOffset), maximum);
        }
        var current = unchecked(maximum * Math.Clamp(percentage, 0, 100)) / 100;
        if (current > 0 && (BinaryPrimitives.ReadUInt16LittleEndian(actor.KiState) == 9 ||
            BinaryPrimitives.ReadUInt16LittleEndian(actor.EiState) == 6))
            throw new NotSupportedException("SetHP positive health on a dead KI/EI mode requires native revival.");
        BinaryPrimitives.WriteInt32LittleEndian(health.AsSpan(SacredCreatureHealthLayout.CurrentHitPointsOffset), current);
        return actor with { Component = actor.Component with { Health = health, ResultValid = 1 } };
    }
}
