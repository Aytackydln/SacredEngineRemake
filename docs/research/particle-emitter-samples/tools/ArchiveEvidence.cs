using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.World;

namespace ParticleEmitterDataset;

internal sealed class ArchiveEvidence(string game, string output)
{
    public async Task Export(IReadOnlyList<Scene> scenes)
    {
        var pak = Path.Combine(game, "pak");
        var items = ItemsPakArchive.Load(Path.Combine(pak, "Items.pak")).ToDictionary(i => i.ItemIndex);
        var mixed = MixedPakArchive.Load(Path.Combine(pak, "mixed.pak"));
        var statics = new PakTable(Path.Combine(game, "World/Static.PAK"));
        var mixedRaw = new PakTable(Path.Combine(pak, "mixed.pak"));
        using var textures = TexturePakArchive.LoadFromDirectory(pak);
        using var world = SacredWorldArchiveFactory.Load(game);
        var usedItems = new HashSet<ushort>();
        var instanceRows = new List<object>();
        var itemBytes = File.ReadAllBytes(Path.Combine(pak, "Items.pak"));
        var names = ExecutableEvidence.TypeNames(game, items.Values.ToArray(), output);
        var terrain = new TerrainEvidence(game, output);
        foreach (var scene in scenes)
        {
            var seen = new HashSet<uint>();
            for (var sy = scene.WorldY / 64 - 1; sy <= scene.WorldY / 64 + 1; sy++)
            for (var sx = scene.WorldX / 64 - 1; sx <= scene.WorldX / 64 + 1; sx++)
            {
                if (await world.TryLoadSector(new SectorCoord(sx, sy)) is not { } sector) continue;
                terrain.Add(scene, sector);
                foreach (var obj in sector.StaticObjects.Objects)
                {
                    if (!seen.Add(obj.StaticId) || !items.TryGetValue((ushort)obj.TypeId, out var item)) continue;
                    var groupId = item.MixedBaseGroupId == 0 ? null : mixed.ResolveGroupId(item.MixedBaseGroupId);
                    var pieces = groupId is { } gid ? mixed.GetGroup(gid) : null;
                    var footX = (obj.ProjectedX + 47.8 - (scene.WorldX - scene.WorldY) * 48.0) * .6 + scene.PlayerX;
                    var footY = (obj.ProjectedY - .3 - (scene.WorldX + scene.WorldY) * 24.0) * .6 + scene.PlayerY;
                    var left = footX - 100; var top = footY - 200; var right = footX + 100; var bottom = footY + 100;
                    if (pieces is { Count: > 0 })
                    {
                        left = footX + pieces.Min(p => Math.Min(p.Left, p.Right)) * .6;
                        top = footY + pieces.Min(p => Math.Min(p.Top, p.Bottom)) * .6;
                        right = footX + pieces.Max(p => Math.Max(p.Left, p.Right)) * .6;
                        bottom = footY + pieces.Max(p => Math.Max(p.Top, p.Bottom)) * .6;
                    }
                    if (right < -100 || bottom < -100 || left > scene.Width + 100 || top > scene.Height + 100) continue;
                    usedItems.Add(item.ItemIndex);
                    var raw = statics.Get(obj.StaticId);
                    instanceRows.Add(new
                    {
                        scene_id = scene.Id, record_id = obj.StaticId, item_id = obj.TypeId,
                        model_name = item.ModelName, executable_type_name = names.GetValueOrDefault(obj.TypeId),
                        label = "unreviewed_context", archive = "World/Static.PAK",
                        file_offset = raw.Offset, descriptor_type = raw.Type, raw_record_hex = Convert.ToHexString(raw.Bytes),
                        known = obj, raw_elevation_tier_33 = raw.Bytes.Length > 0x33 ? (int?)raw.Bytes[0x33] : null,
                        projected_world_inverse = new { x = obj.ProjectedX / 96.0 + obj.ProjectedY / 48.0, y = obj.ProjectedY / 48.0 - obj.ProjectedX / 96.0 },
                        approximate_screen_bounds = new { left, top, right, bottom, foot_x = footX, foot_y = footY },
                        bounds_basis = pieces is { Count: > 0 } ? "mixed piece bounds; approximate camera" : "fallback search envelope; not measured visible extent",
                        resolved_mixed_group_id = groupId,
                        nearby_underlines = scene.Marks.Select(m => new { observation_id = m.Id,
                            distance_to_bounds = Distance(m.X + m.Width / 2.0, m.Y, left, top, right, bottom) })
                            .Where(m => m.distance_to_bounds <= 100).ToArray()
                    });
                }
            }
            Console.WriteLine($"Scene {scene.Id}: exported nearby authored objects.");
        }
        JsonOutput.Lines(Path.Combine(output, "Static.pak.jsonl"), instanceRows);
        terrain.Save();
        JsonOutput.Lines(Path.Combine(output, "Items.pak.jsonl"), usedItems.Order().Select(id =>
        {
            var item = items[id]; var d = item.ModelDesc;
            var raw = itemBytes.AsSpan((int)item.EntryInfo.ModelDescOffset, 128).ToArray();
            return new
            {
                record_id = id, model_name = item.ModelName, executable_type_name = names.GetValueOrDefault(id),
                archive = "pak/Items.pak", descriptor_file_offset = item.EntryInfo.ModelDescOffset,
                raw_model_descriptor_hex = Convert.ToHexString(raw),
                entry_descriptor_hex = Convert.ToHexString(itemBytes.AsSpan(0x102 + id * 12, 12)),
                raw_bytes_u8 = raw.Select(b => (int)b).ToArray(),
                known = new
                {
                    graphic_type_raw = (ushort)d.GraphicType, graphic_flags_raw = (ushort)d.GraphicFlags,
                    d.MiniObjectTextureId, miniobject_texture_name = textures.TryGetTextureName(d.MiniObjectTextureId, out var miniName) ? miniName : null,
                    d.TextureId, general_texture_name = textures.TryGetTextureName(d.TextureId, out var name) ? name : null,
                    d.MixedBaseGroupId, resolved_mixed_group_id = d.MixedBaseGroupId == 0 ? null : mixed.ResolveGroupId(d.MixedBaseGroupId),
                    d.ItemId, d.SoundProfileId, d.StaticSpriteFrameCount, category_raw = (byte)d.Category,
                    d.StaticSpriteFrameDuration10Ms, descriptor_flags_raw = (byte)d.DescriptorFlags, d.ModelExtent,
                    d.StaticShadowAtlasCellIndex, d.StaticShadowAnchorX, d.StaticShadowAnchorY,
                    static_shadow_projection_raw = (byte)d.StaticShadowProjection, d.StaticShadowContactExtent,
                    d.EffectTextureId,
                    effect_texture_interpretation = "Union field. A numeric texture match does not establish a particle association for a mixed static object."
                }
            };
        }));
        var usedGroups = usedItems.Select(id => items[id].MixedBaseGroupId).Where(id => id != 0)
            .Select(mixed.ResolveGroupId).OfType<uint>().Distinct().Order().ToArray();
        JsonOutput.Lines(Path.Combine(output, "mixed.pak.jsonl"), usedGroups.Select(id =>
        {
            var raw = mixedRaw.Get(id);
            return new { record_id = id, archive = "pak/mixed.pak", file_offset = raw.Offset, descriptor_type = raw.Type,
                raw_record_hex = Convert.ToHexString(raw.Bytes), group = mixed.GetGroupInfo(id) };
        }));
        await new TextureEvidence(pak, output).Export(textures);
        JsonOutput.Write(Path.Combine(output, "export-counts.json"), new { scenes = scenes.Count, underlines = scenes.Sum(s => s.Marks.Count),
            scene_static_rows = instanceRows.Count, item_definitions = usedItems.Count, mixed_groups = usedGroups.Length });
    }

    private static double Distance(double x, double y, double l, double t, double r, double b) =>
        Math.Sqrt(Math.Pow(Math.Max(l-x, Math.Max(0, x-r)), 2) + Math.Pow(Math.Max(t-y, Math.Max(0, y-b)), 2));
}
