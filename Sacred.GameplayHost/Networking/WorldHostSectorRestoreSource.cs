using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Networking;

/// <summary>The interpreter +364 source for one explicit fresh-world operation.
/// Engine 60C2DC -> 46FA09 -> 470E47 -> 461146 erases these rows before
/// StartCode. Supported startup/entry/ambient producers do not append rows;
/// 469D30 is the separate saved-interpreter importer. No restored-world source
/// or world-bank completion is inferred from this reset.</summary>
public sealed class WorldHostSectorRestoreSource
{
    private readonly WorldHostStartupRequest _request;
    private readonly ReadOnlyMemory<byte> _records;
    public int Count => _records.Length / SacredScriptSectorRestoreSaveLayout.RecordSize;

    private WorldHostSectorRestoreSource(WorldHostStartupRequest request)
    {
        request.RequireFreshWorld();
        _request = request;
        _records = ReadOnlyMemory<byte>.Empty;
    }

    internal static WorldHostSectorRestoreSource FromFreshWorld(WorldHostStartupRequest request) => new(request);

    internal void RequireEmptyFreshSource(WorldHostStartupRequest? request)
    {
        if (!ReferenceEquals(_request, request))
            throw new InvalidOperationException("Sector restoration requires the same explicit startup operation that reset its source.");
        _request.RequireFreshWorld();
        if (Count != 0) throw new NotSupportedException("Nonempty interpreter sector restoration requires its actual object restore services.");
    }
}
