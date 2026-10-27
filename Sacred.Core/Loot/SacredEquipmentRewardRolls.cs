using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

public readonly record struct SacredEquipmentRewardRules(uint MoneyResource, int MoneyCalls, int EquipmentAttempts);
public readonly record struct SacredEquipmentRewardQuery(SacredItemCategory Category, ushort Level, uint HeroPreference,
    byte Flags, uint MaximumQuality);
public readonly record struct SacredEquipmentRewardGeneration(int Level, int Quality, byte Sockets);
public readonly record struct SacredEquipmentRewardLaunch(float X, float Y, float Z, float SpinX, float SpinY, float SpinZ);

/// <summary>Gold 0x5516B0 reward choices. These rolls do not create items or
/// replace the RNG consumed by selection, construction or physical effects.</summary>
public static class SacredEquipmentRewardRolls
{
    public static SacredEquipmentRewardQuery Query(ushort sourceLevel, uint heroResource, uint maximumQuality,
        IReadOnlyList<SacredItemCategory> categories, SacredRandomSequence random)
    {
        var flags = random.Next() % 3 != 0 ? (byte)2 : (byte)0;
        var preference = random.Next() % 2 == 0 ? heroResource : 0;
        return new(SacredItemCategorySelection.Slots(categories, random), sourceLevel, preference, flags, maximumQuality);
    }

    public static SacredEquipmentRewardGeneration Generation(int sourceLevel, SacredEquipmentRewardRequest request,
        SacredRandomSequence random)
    {
        byte sockets = 0;
        if (random.Next() % 4 == 0)
            sockets = random.Next() % 4 != 0 ? (byte)1 : random.Next() % 4 != 0 ? (byte)3 : (byte)2;
        var quality = SacredRandomRange.Inclusive(unchecked((int)request.MinimumQuality), unchecked((int)request.MaximumQuality), random);
        var increment = SacredRandomRange.Inclusive(0, request.LevelIncrement, random);
        return new(unchecked(sourceLevel + increment), quality, sockets);
    }

    public static SacredEquipmentRewardLaunch Launch(SacredRandomSequence random) => new(
        (random.Next() % 301 - 150) * 10f, (random.Next() % 301 - 150) * 10f, 190f,
        random.Next() % 13 - 6, random.Next() % 13 - 6, random.Next() % 13 - 6);

    public static SacredEquipmentRewardRequest NextAttempt(SacredEquipmentRewardRequest request, bool chancePassed) =>
        request with
        {
            Chance = chancePassed ? request.Chance >> 1 : unchecked(request.Chance * 2),
            MinimumQuality = Halve(request.MinimumQuality), MaximumQuality = Halve(request.MaximumQuality)
        };

    private static uint Halve(uint value) => (value & 0xFFFFFFFE) < 2 ? 1 : value >> 1;
}
