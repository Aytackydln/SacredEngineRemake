using Sacred.Assets.Paks.Tiles;
using Sacred.Core.World.Sector;

namespace ParticleEmitterDataset;

internal sealed class TerrainEvidence(string game, string output)
{
    private readonly TilesPakArchive _tiles = TilesPakArchive.Load(Path.Combine(game, "pak/tiles.pak"));
    private readonly PakTable _raw = new(Path.Combine(game, "pak/tiles.pak"));
    private readonly List<object> _cells = [];
    private readonly List<object> _floor = [];
    private readonly HashSet<uint> _ids = [];

    public void Add(Scene scene, Sector sector)
    {
        for (var ly = 0; ly < sector.Ground.Height; ly++)
        for (var lx = 0; lx < sector.Ground.Width; lx++)
        {
            var wx = sector.Coord.X * 64 + lx; var wy = sector.Coord.Y * 64 + ly;
            var px = ((wx-wy) - (scene.WorldX-scene.WorldY)) * 48 * .6 + scene.PlayerX;
            var py = ((wx+wy) - (scene.WorldX+scene.WorldY)) * 24 * .6 + scene.PlayerY;
            if (px < -100 || px > scene.Width+100 || py < -100 || py > scene.Height+100) continue;
            var tile = sector.Ground[lx,ly]; _ids.Add(tile);
            _cells.Add(new { scene_id = scene.Id, world_x = wx, world_y = wy, sector_x = sector.Coord.X, sector_y = sector.Coord.Y,
                local_x = lx, local_y = ly, approximate_screen_x = px, approximate_screen_y = py,
                ground_tile_id = tile, label = "terrain_context", pathing = sector.Pathing[lx,ly],
                visual_elevation = sector.VisualElevation[lx,ly], gameplay_elevation = sector.Elevation[lx,ly],
                baked_light = sector.BakedLight[lx,ly],
                interpretation = "Archive context; presence in search viewport does not guarantee visibility through floors, roofs, or occluders." });
            foreach (var overlay in sector.FloorOverlays[lx,ly])
            {
                _ids.Add(overlay.PrimaryTileId);
                if (overlay.SecondaryTileId != 0) _ids.Add(overlay.SecondaryTileId);
                _floor.Add(new { scene_id = scene.Id, world_x = wx, world_y = wy, known = overlay,
                    raw_record_unavailable_reason = "Current floor loader does not retain the Floor.PAK record index." });
            }
        }
    }

    public void Save()
    {
        JsonOutput.Lines(Path.Combine(output, "sectors.wldx.context.jsonl"), _cells);
        JsonOutput.Lines(Path.Combine(output, "Floor.pak.context.jsonl"), _floor);
        JsonOutput.Lines(Path.Combine(output, "tiles.pak.jsonl"), _ids.Order().Select(id =>
        {
            var raw = _raw.Get(id);
            return new { record_id = id, file_offset = raw.Offset, raw_record_hex = Convert.ToHexString(raw.Bytes), known = _tiles.Get(id) };
        }));
    }
}
