using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;
using Sacred.Network.Gameplay;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold CreateNPC placement before factory/level initialization (0x482D79..0x482EFF).
/// A resolved position does not establish collision acceptance or a constructed creature.</summary>
public static class WorldScriptCreaturePlacement
{
    public static GoldObjectPosition? Resolve(SacredScriptCreateCreature creation, SacredSectorGrid sectors)
        => Resolve(creation,sectors,SacredX87Precision.Extended);

    public static GoldObjectPosition? Resolve(SacredScriptCreateCreature creation,SacredSectorGrid sectors,SacredX87Precision precision)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(sectors);
        var input = creation.PlacementInput;
        if (creation.TypeId == 0 || input.X == -1) return null;
        var worldUnits = input.X < 0 && input.Y < 0;
        var x = worldUnits ? unchecked(-input.X) : Convert(input.X);
        var y = worldUnits ? unchecked(-input.Y) : Convert(input.Y);
        var surface = input.Z < 0 ? (byte)0 : unchecked((byte)input.Z);
        return new(x, y, surface, sectors.GetSector(SacredWorldCoordinates.WorldToTile(x), SacredWorldCoordinates.WorldToTile(y)));
        int Convert(int tile)=>precision==SacredX87Precision.Single ? SacredWorldCoordinates.TileCenterToWorldStartup(tile) : SacredWorldCoordinates.TileCenterToWorld(tile);
    }
}
