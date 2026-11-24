using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameServer.Auth.Hero;

namespace Sacred.GameServer.Tests;

/// <summary>Developer tool, skipped unless PRICEDUMP_GAME and PRICEDUMP_DIR are set: the stored price of the items of the decoded hero
/// snapshots (HeroDump) against SacredEquipmentPricing.CalculateOrdinary with the installation's Balance.bin.</summary>
[TestClass]
public class PriceDump
{
    [TestMethod]
    public void ComparePrices()
    {
        var game = Environment.GetEnvironmentVariable("PRICEDUMP_GAME");
        var dir = Environment.GetEnvironmentVariable("PRICEDUMP_DIR");
        if (game is null || dir is null) return;
        var data = SacredEquipmentGenerationData.Load(game);
        var lines = new List<string>();
        foreach (var path in Directory.GetFiles(dir, "snap-*.objects.bin"))
        {
            var list = File.ReadAllBytes(path);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(list);
            var position = 4;
            for (var i = 0; i < count && position + 4 <= list.Length; i++)
            {
                var resource = BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(position));
                position += 4;
                if (resource == 0) continue;
                var span = list.AsSpan(position);
                var size = SavedObjects.RecordSize(span, 0x0E);
                if (size < 0) break;
                if (SavedObjects.IsItem(span))
                {
                    var content = SavedObjects.SavedContent(span).ToArray();
                    var own = content.AsSpan(content.Length - 130, 130).ToArray();
                    var type = BinaryPrimitives.ReadUInt32LittleEndian(own);
                    var stored = BinaryPrimitives.ReadUInt32LittleEndian(own.AsSpan(8));
                    string result;
                    try
                    {
                        var context = data.PriceContext(type);
                        var single = own.ToArray();
                        var priceSingle = SacredEquipmentPricing.CalculateOrdinary(single, context, data.Tuning.Pricing, data.Tuning.Levels, data.Tuning.Bonuses, data.Tuning.Normalization, data.Tuning.Sockets, SacredX87Precision.Single);
                        var extended = own.ToArray();
                        var priceExt = SacredEquipmentPricing.CalculateOrdinary(extended, context, data.Tuning.Pricing, data.Tuning.Levels, data.Tuning.Bonuses, data.Tuning.Normalization, data.Tuning.Sockets, SacredX87Precision.Extended);
                        result = $"computed single {priceSingle} extended {priceExt} category {context.Category}";
                    }
                    catch (Exception e) { result = "error " + e.GetType().Name + ": " + e.Message; }
                    lines.Add($"{Path.GetFileName(path)} type 0x{type:X} records {(content.Length / 130) - 1} stored {stored} {result}");
                }
                position += size;
            }
        }
        File.WriteAllLines(Path.Combine(dir, "prices.txt"), lines);
    }
}
