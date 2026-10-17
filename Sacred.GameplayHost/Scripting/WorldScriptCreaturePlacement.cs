using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold CreateNPC placement before factory/level initialization (0x482D79..0x482EFF).
/// A resolved position does not establish collision acceptance or a constructed creature.</summary>
public static class WorldScriptCreaturePlacement
{
    public static GoldObjectPosition? Resolve(SacredScriptCreateCreature creation, SacredSectorGrid sectors)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(sectors);
        var input = creation.PlacementInput;
        if (creation.TypeId == 0 || input.X == -1) return null;
        var worldUnits = input.X < 0 && input.Y < 0;
        var x = worldUnits ? unchecked(-input.X) : SacredWorldCoordinates.TileCenterToWorld(input.X);
        var y = worldUnits ? unchecked(-input.Y) : SacredWorldCoordinates.TileCenterToWorld(input.Y);
        var surface = input.Z < 0 ? (byte)0 : unchecked((byte)input.Z);
        return new(x, y, surface, sectors.GetSector(SacredWorldCoordinates.WorldToTile(x), SacredWorldCoordinates.WorldToTile(y)));
    }
}
