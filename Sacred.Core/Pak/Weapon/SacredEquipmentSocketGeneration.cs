using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D2E40's zero-quality branch: sockets, bonus clearing and rarity.
/// Nonzero budgets need the separate magic-generation loop and are rejected before
/// mutation or RNG. This stage does not construct, price, name or install an item.</summary>
public static class SacredEquipmentSocketGeneration
{
    public static void ApplyZeroQuality(Span<byte> instance, SacredItemCategory category, int maximumSockets,
        uint quality, uint difficulty, SacredEquipmentSocketBalance balance, SacredRandomSequence random)
    {
        if (instance.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected 130-byte item values.", nameof(instance));
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(random);
        if (category == SacredItemCategory.Book) return;
        if (unchecked((ushort)balance.Budget(quality & 0xFFFF)) != 0)
            throw new InvalidOperationException("Nonzero equipment quality requires the native magic-generation loop.");
        if (maximumSockets is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(maximumSockets));
        var jewelry = category is SacredItemCategory.Ring or SacredItemCategory.Amulet;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(instance[4..]);
        var budget = (float)balance.Budget((flags >> 16) & 15);
        var multiplier = 1f;
        var draw = random.Next(); // Native draws even for jewelry and empty socket pools.
        if ((uint)(draw % 1000) < balance.FirstChance && !jewelry && maximumSockets != 0)
        {
            var count = 1;
            while (count < maximumSockets && (uint)(random.Next() % 1000) < balance.AdditionalChance) count++;
            for (var i = 0; i < count; i++)
            {
                var difficultyBase = difficulty == 0 ? 0 : difficulty == 1 ? 1 : 2;
                var kind = Math.Clamp(difficultyBase + random.Next() % 251 / 100, 1, 3);
                instance[12 + i] = (byte)kind;
                multiplier += balance.Costs[kind - 1];
            }
        }
        // Existing trailing sockets remain untouched; the normal generator clears
        // its instance before invoking this stage, while update callers may not.
        budget *= multiplier;
        var firstCleared = jewelry ? 0 : 1;
        instance.Slice(58 + firstCleared * 2, (8 - firstCleared) * 2).Clear();
        instance.Slice(74 + firstCleared * 4, (8 - firstCleared) * 4).Clear();
        instance.Slice(106 + firstCleared * 2, (8 - firstCleared) * 2).Clear();
        var rarity = unchecked((uint)(long)Math.Min(budget, 14f));
        BinaryPrimitives.WriteUInt32LittleEndian(instance[4..], (flags & 0xFFF0FFFF) | (rarity << 16));
    }
}
