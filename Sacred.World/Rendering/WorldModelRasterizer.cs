using System.Numerics;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Meshes;
using Sacred.World.Geometry;
using Sacred.World.Objects;

namespace Sacred.World.Rendering;

/// <summary>Software-rasterizes authored GRN world models over a deterministic terrain frame.</summary>
public sealed class WorldModelRasterizer(SacredWorldArchive world, IReadOnlyDictionary<ushort, ItemsPakEntry> items, ModelsPakArchive models, TexturePakArchive textures)
{
    private readonly Dictionary<(uint TypeId, bool Open), ModelGeometry?> _meshes = [];
    public WorldStaticSpriteProvider? StaticSprites { get; init; }

    public async Task<RgbaImage> RenderAsync(
        RgbaImage terrain,
        Vector2 center,
        float zoom,
        bool openDoors = false,
        IndoorTileGroup? activeIndoorGroup = null)
    {
        var pixels = (byte[])terrain.Pixels.Clone();
        var sectors = await LoadSectors(center);
        var indoorGroups = sectors.SelectMany(static sector => sector.IndoorTileGroups.Groups)
            .DistinctBy(static group => group.Id).ToArray();
        var placements = sectors.SelectMany(sector => sector.WorldObjects.Objects.Concat(sector.StaticObjects.Objects))
            .Where(placement => IsVisibleModel(placement, indoorGroups, activeIndoorGroup))
            .OrderByDescending(p => p.PreciseWorldPosition.HasValue)
            .DistinctBy(p => (p.TypeId, p.TileWorldX, p.TileWorldY)).ToArray();
        var triangles = new List<Triangle>();
        var occlusion = StaticSprites is null ? null : await WorldModelOcclusion.BuildAsync(
            StaticSprites, sectors, center, terrain.Width, terrain.Height, zoom, activeIndoorGroup);
        foreach (var placement in placements)
        {
            if (!items.TryGetValue((ushort)placement.TypeId, out var item) || string.IsNullOrWhiteSpace(item.ModelName))
                continue;
            var geometry = await LoadMesh(placement.TypeId, item, openDoors);
            if (geometry is null) continue;
            AddTriangles(triangles, geometry.Value, placement, item.ModelDesc.Angle3D, center, terrain.Width, terrain.Height, zoom);
        }
        var depths = new float[terrain.Width * terrain.Height];
        Array.Fill(depths, float.PositiveInfinity);
        foreach (var triangle in triangles)
            DrawTriangle(pixels, depths, occlusion, terrain.Width, terrain.Height, triangle);
        Console.WriteLine($"Model overlay: {placements.Length} authored placements, {triangles.Count:N0} GRN triangles.");
        return new RgbaImage(terrain.Width, terrain.Height, pixels);
    }

    private async Task<ModelGeometry?> LoadMesh(uint typeId, ItemsPakEntry item, bool openDoors)
    {
        if (_meshes.TryGetValue((typeId, openDoors), out var cached)) return cached;
        try
        {
            var name = Path.GetFileName(item.ModelName);
            var asset = await models.LoadModelAsync(name, GrnMeshExtractionMode.PrimarySlice);
            var mesh = asset.Mesh;
            if (mesh is null) return null;

            if (openDoors && item.ModelDesc.Category == SacredItemCategory.Door)
            {
                var motion = new DoorMotionPlayback(mesh, asset.Skin,
                    await models.LoadModelAnimationAsync(name, WorldDoorMotion.Open), await models.LoadModelAnimationAsync(name, WorldDoorMotion.Close));
                motion.SetInitialState(true);
                mesh = motion.Mesh;

            }
            var preferItem = mesh.Surfaces.Select(s => s.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
            var surfaceTextures = new TextureAsset?[mesh.Surfaces.Count];
            for (var i = 0; i < surfaceTextures.Length; i++)
            {
                var reference = ModelTextureResolver.Resolve(textures, item.ModelDesc.TextureId, item.ModelDesc.EffectTextureId,
                    item.ModelDesc.GraphicFlags, false, preferItem, mesh.Surfaces[i].TextureName);
                if (!string.IsNullOrWhiteSpace(reference.TextureName))
                    surfaceTextures[i] = await textures.LoadTextureAsync(reference.TextureName);
            }
            var geometry = new ModelGeometry(mesh, (asset.Diagnostics?.SourceOriginOffset ?? Vector3.Zero), surfaceTextures);
            _meshes[(typeId, openDoors)] = geometry;
            return geometry;
        }
        catch (FileNotFoundException)
        {
            _meshes[(typeId, openDoors)] = null;
            return null;
        }
    }

    private async Task<Sector[]> LoadSectors(Vector2 center)
    {
        var origin = new SectorCoord((int)MathF.Floor(center.X / Sector.TileCount), (int)MathF.Floor(center.Y / Sector.TileCount));
        var loads = new List<Task<Sector?>>();
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
            loads.Add(world.TryLoadSector(new SectorCoord(origin.X + x, origin.Y + y)));
        return (await Task.WhenAll(loads)).Where(s => s is not null).Select(s => s!).ToArray();
    }

    private bool IsModel(StaticWorldObject placement) => placement.TypeId <= ushort.MaxValue &&
        items.TryGetValue((ushort)placement.TypeId, out var item) &&
        item.ModelDesc.GraphicType.HasFlag(SacredItemGraphicType.Model) && !string.IsNullOrWhiteSpace(item.ModelName) &&
        item.ModelDesc.Category is SacredItemCategory.WorldObject or SacredItemCategory.Container or
            SacredItemCategory.Door or SacredItemCategory.Effect;

    private bool IsVisibleModel(
        StaticWorldObject placement,
        IReadOnlyList<IndoorTileGroup> indoorGroups,
        IndoorTileGroup? activeIndoorGroup) =>
        IsModel(placement) &&
        WorldObjectSurfaceVisibility.IsModelVisible(
            placement,
            indoorGroups,
            activeIndoorGroup,
            items[(ushort)placement.TypeId].ModelDesc.Category);

    private static void AddTriangles(List<Triangle> target, ModelGeometry geometry, StaticWorldObject placement, float angle, Vector2 center, int width, int height, float zoom)
    {
        var mesh = geometry.Mesh;
        var origin = IsometricProjection.WorldToModel(WorldModelPose.TilePosition(placement));
        var camera = IsometricProjection.WorldToModel(center);
        var localTransform = WorldModelPose.LocalTransform(angle, geometry.SourceOriginOffset);
        // The visual pivot may be fractional, but the object is inserted into
        // the authored tile chain as one unit for painter ordering.
        var painterDepth = WorldPainterDepth.FromTile(placement.TileWorldX, placement.TileWorldY);
        var points = new Point[mesh.Vertices.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var local = Vector3.Transform(mesh.Vertices[i].Position, localTransform);
            var position = new Vector3(origin.X + local.X, origin.Y + local.Y, local.Z);
            points[i] = new Point(width * .5f + (position.X - camera.X) * zoom,
                height * .5f - ((position.Y - camera.Y) + position.Z) / MathF.Sqrt(2) * zoom,
                position.Y - position.Z,
                mesh.Vertices[i].TexCoord);
        }
        for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            var a = points[mesh.Indices[i]]; var b = points[mesh.Indices[i + 1]]; var c = points[mesh.Indices[i + 2]];
            var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            TextureAsset? texture = null;
            for (var surfaceIndex = 0; surfaceIndex < mesh.Surfaces.Count; surfaceIndex++)
            {
                var surface = mesh.Surfaces[surfaceIndex];
                if (i >= surface.IndexStart && i < surface.IndexStart + surface.IndexCount)
                { texture = geometry.Textures[surfaceIndex]; break; }
            }
            if (MathF.Abs(cross) > .01f) target.Add(new Triangle(a, b, c, painterDepth, texture));
        }
    }

    private static void DrawTriangle(byte[] pixels, float[] depths, float[]? occlusion, int width, int height, Triangle triangle)
    {
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X))));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(triangle.A.Y, MathF.Min(triangle.B.Y, triangle.C.Y))));
        var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y))));
        var area = Edge(triangle.A, triangle.B, triangle.C.X, triangle.C.Y); if (area == 0) return;
        var shade = (byte)Math.Clamp(110 + (int)(MathF.Abs(area) % 100), 0, 230);
        for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
        {
            var u = Edge(triangle.B, triangle.C, x + .5f, y + .5f) / area;
            var v = Edge(triangle.C, triangle.A, x + .5f, y + .5f) / area;
            var w = 1 - u - v;
            if (u < 0 || v < 0 || w < 0) continue;
            var pixel = y * width + x;
            if (occlusion is not null && occlusion[pixel] > triangle.PainterDepth) continue;
            var depth = triangle.A.Depth * u + triangle.B.Depth * v + triangle.C.Depth * w;
            if (depth >= depths[pixel]) continue;
            var o = pixel * 4;
            if (triangle.Texture is { } texture)
            {
                var uv = triangle.A.Uv * u + triangle.B.Uv * v + triangle.C.Uv * w;
                var tx = Math.Clamp((int)((uv.X - MathF.Floor(uv.X)) * texture.Width), 0, texture.Width - 1);
                var ty = Math.Clamp((int)((uv.Y - MathF.Floor(uv.Y)) * texture.Height), 0, texture.Height - 1);
                var t = (ty * texture.Width + tx) * 4;
                if (texture.Rgba8[t + 3] < 128) continue;
                for (var channel = 0; channel < 3; channel++) pixels[o + channel] = texture.Rgba8[t + channel];
            }
            else { pixels[o] = shade; pixels[o + 1] = (byte)(shade * .82f); pixels[o + 2] = (byte)(shade * .54f); }
            depths[pixel] = depth;
            pixels[o + 3] = 255;
        }
    }
    private static float Edge(Point a, Point b, float x, float y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
    private readonly record struct Point(float X, float Y, float Depth, Vector2 Uv);
    private readonly record struct Triangle(Point A, Point B, Point C, float PainterDepth, TextureAsset? Texture);
    private readonly record struct ModelGeometry(Mesh Mesh, Vector3 SourceOriginOffset, TextureAsset?[] Textures);
}





