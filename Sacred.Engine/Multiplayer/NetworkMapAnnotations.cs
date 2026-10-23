using System.Collections.Generic;
using System.Linq;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;
using Sacred.World.Map;

namespace Sacred.Engine.Multiplayer;

/// <summary>Received map-name records resolve against original localized resources, independently of the offline cache.</summary>
internal sealed class NetworkMapAnnotations
{
    private GoldScriptSpatialReplica? _source;
    private long _revision = -1;
    private WorldMapAnnotations? _annotations;
    public WorldMapAnnotations Resolve(GoldScriptSpatialReplica spatial, WorldMapAnnotations offline, GameResStore resources)
    {
        if (!spatial.HasMapState) return offline;
        if (ReferenceEquals(_source, spatial) && _revision == spatial.MapRevision) return _annotations!;
        var labels = spatial.MapIcons.Where(icon => icon.IsLabel).Select(icon => new WorldMapLabel(
            new(icon.X, icon.Y), icon.Curvature, icon.AngleDegrees,
            resources.Strings.GetValueOrDefault(icon.TextResource, string.Empty)))
            .Where(label => !string.IsNullOrWhiteSpace(label.Text)).ToArray();
        (_source, _revision) = (spatial, spatial.MapRevision);
        _annotations = new(labels, offline.Npcs);
        EngineLog.WriteLine($"Network map annotations loaded: {labels.Length} localized labels / {spatial.MapIcons.Count} received records.");
        return _annotations;
    }
}
