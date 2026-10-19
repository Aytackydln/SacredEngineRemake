using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.Core.World.Sector;

namespace Sacred.GameplayHost.Networking;

/// <summary>Gold 0x49426E..0x494642, including its shoreline-neighborhood tests
/// and otherwise unused random draw. Creation executes at each scan position,
/// so constructor and post-creation random draws stay interleaved with the scan.</summary>
public sealed class WorldAmbientPopulationGenerator(SacredAmbientPopulationDefinitions definitions,
    SacredRandomSequence random, Func<int, int, SacredAmbientTile?> tile,
    Func<int, int, bool> groundFog, Action<uint, int, int> create)
{
    public int Created { get; private set; }
    public void Execute(SectorCoord coordinate, byte region, byte liquidStyleA, byte liquidStyleB)
    {
        if (liquidStyleA is >= 4 and <= 6 || liquidStyleB is >= 4 and <= 6) return;
        var originX = coordinate.X * Sector.TileCount;
        var originY = coordinate.Y * Sector.TileCount;
        for (var row = random.Next() % 3; row < Sector.TileCount; row += 2)
            for (var column = random.Next() % 3; column < Sector.TileCount; column += 2)
            {
                var x = originX + column; var y = originY + row;
                if (!Eligible(x, y, -14) || random.Next() % 11 != 0) continue;
                var inner = 0;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && Eligible(x + dx, y + dy, -25)) inner++;
                if (inner != 8) continue;
                var outer = 0;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        if ((dx is < -1 or > 1 || dy is < -1 or > 1) && Eligible(x + dx, y + dy, -25)) outer++;
                if (outer >= 14) continue;
                var special = definitions.UsesSpecialType(region);
                var fog = groundFog(x, y);
                if (fog && !special) continue;
                uint type;
                if (random.Next() % 3 == 0) type = definitions.FirstType;
                else { _ = random.Next(); type = definitions.SecondType; }
                if (special && (fog || random.Next() % 3 == 0)) type = definitions.SpecialType;
                create(type, x, y);
                Created++;
            }
    }
    private bool Eligible(int x, int y, sbyte lower) => tile(x, y)?.IsEligible(-2, lower) == true;
}
