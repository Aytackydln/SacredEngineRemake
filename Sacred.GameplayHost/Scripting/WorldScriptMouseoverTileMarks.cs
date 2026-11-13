namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x4A6D81 direct mark: original signed XY inputs and the surface
/// input select the query; only stored record coordinates are narrowed to UInt16.</summary>
public static class WorldScriptMouseoverTileMarks
{
    public static void Mark(WorldScriptMouseoverCreation creation,
        Func<int,int,(byte[] Bytes,int Offset)?> outdoor,
        Func<int,int,ushort,(byte[] Bytes,int Offset)?> indoor)
    {
        var position = creation.Position;
        var cell = position.Z == 0 ? outdoor(position.X,position.Y) : indoor(position.X,position.Y,unchecked((ushort)position.Z));
        if (cell is { } destination) destination.Bytes[destination.Offset+0x1E] |= 0x80;
    }
}
