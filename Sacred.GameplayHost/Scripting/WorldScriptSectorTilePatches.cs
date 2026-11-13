using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x493440 pending patch pass after actor re-placement. Storage queries
/// and active registration are caller services; both lists retain native order.</summary>
public static class WorldScriptSectorTilePatches
{
    public static void Apply(int gridX,int gridY,IEnumerable<GoldSectorScriptPatch> mouseovers,
        IEnumerable<(int X,int Y,byte Surface)> positions,Action<int,int,ushort,byte> mark,
        Action<GoldSectorScriptPatch> register)
    {
        foreach (var patch in mouseovers)
        {
            if (patch.X/64 != gridX || patch.Y/64 != gridY) continue;
            mark(patch.X,patch.Y,patch.Surface,0x80);
            register(patch);
        }
        foreach (var position in positions)
        {
            if (position.X/64 != gridX || position.Y/64 != gridY) continue;
            if (position.Surface <= 1) mark(position.X,position.Y,0,4);
            if (position.Surface > 0) mark(position.X,position.Y,position.Surface,4);
        }
    }
}
