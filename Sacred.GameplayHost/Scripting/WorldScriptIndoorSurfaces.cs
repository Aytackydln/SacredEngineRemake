using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Network.Gameplay;
using Sacred.World;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x6354D0/0x635440/0x634C50 and the initial CreateNPC floor
/// search at 0x482FD6. All coordinates here are original tile coordinates.</summary>
public sealed class WorldScriptIndoorSurfaces(
    Func<int,int,WldxTileRecord?> outdoor,
    Func<uint,WldxSectorPayload> sector,
    Func<uint,StaticObjectRecord?> readStatic)
{
    private readonly Dictionary<uint,StaticObjectRecord?> _records = [];

    public StaticObjectRecord? GetStatic(uint id)
    {
        if (id == 0) return null;
        if (!_records.TryGetValue(id,out var record)) _records.Add(id,record=readStatic(id));
        return record;
    }

    public uint FindParent(int x,int y)
    {
        if (outdoor(x,y) is not { } first) return 0;
        var anchor = outdoor(x+first.IndoorAnchorDeltaX,y+first.IndoorAnchorDeltaY);
        if (anchor is not { } tile || (tile.PathFlags & WorldPathFlags.Indoor) == 0) return 0;
        var reference = tile.StaticChainHeadId;
        var visited = new HashSet<uint>();
        while (reference != 0)
        {
            if (!visited.Add(reference)) throw new InvalidDataException("Indoor parent static chain cycles.");
            if (GetStatic(reference) is not { } record) return 0;
            if ((record.Flags & StaticObjectFlags.IndoorSurfaceParent) != 0) return reference;
            reference = record.NextStaticId;
        }
        return 0;
    }

    public uint Child(uint parent,ushort level)
    {
        if (level == 0) return parent;
        if (GetStatic(parent) is not { } record || (record.Flags & StaticObjectFlags.IndoorSurfaceParent) == 0) return 0;
        var reference = record.IndoorSurfaceChainHeadId;
        for (var i=1;i<level;i++)
        {
            if (GetStatic(reference) is not { } child) return 0;
            reference = child.NextStaticId;
        }
        return reference;
    }

    public (byte[] Bytes,int Offset)? IndoorCell(int x,int y,uint floor)
    {
        if (GetStatic(floor) is not { } record) return null;
        if (record.HeightLevelId == 0) return null; // The caller uses the outdoor grid for descriptor zero.
        var payload = sector(record.SectorId);
        var index = record.HeightLevelId-1;
        if (index >= payload.IndoorGroups.Count)
            throw new InvalidDataException("Static floor references an absent original height grid.");
        var grid = payload.IndoorGroups[index];
        var localX = x-grid.WorldX; var localY = y-grid.WorldY;
        if ((uint)localX >= grid.Width || (uint)localY >= grid.Height) return null;
        return (grid.Tiles,(localY*grid.Width+localX)*WldxTileRecord.Size);
    }

    public WldxTileRecord? GetTile(int x,int y,uint floor)
    {
        if (floor == 0) return outdoor(x,y);
        if (GetStatic(floor) is not { } record) return null;
        if (record.HeightLevelId == 0) return outdoor(x,y);
        return IndoorCell(x,y,floor) is { } cell ? WldxTileRecord.FromBytes(cell.Bytes.AsSpan(cell.Offset)) : null;
    }

    public bool CanStand(int x,int y,byte level)
    {
        var parent = FindParent(x,y);
        var floor = Child(parent,level);
        if (GetTile(x,y,floor) is not { } tile) return false;
        if ((tile.PathFlags & WorldPathFlags.Trigger) != 0)
            throw new NotSupportedException("Indoor walkability requires the original trigger-cell override lookup.");
        // Native accepts a zero-filled cell inside the grid; do not substitute
        // the renderer's sparse authored-cell existence predicate here.
        return !tile.Properties.BlocksMovement && (tile.PathFlags & WorldPathFlags.RuntimeBlocked) == 0;
    }

    public GoldObjectPosition ResolveFactoryPosition(GoldObjectPosition position)
    {
        var x = SacredWorldCoordinates.WorldToTile(position.X); var y = SacredWorldCoordinates.WorldToTile(position.Y);
        if (position.Surface != 0 || outdoor(x,y) is not { } tile || (tile.PathFlags & WorldPathFlags.Indoor) == 0 ||
            Child(FindParent(x,y),1) == 0) return position;
        // This caller tries levels 0..4. The later low-level placement routine
        // has a separate four-iteration surface-1 probe; they are not interchangeable.
        for (byte level=0;level<5;level++)
            if (CanStand(x,y,level)) return position with { Surface=level };
        return position;
    }
}
