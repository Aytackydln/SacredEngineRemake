using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>Gold 0x5C3130's generated name and 0x44A200's existing name lookup.
/// Resources, fragments and subtype tables come from the original files.</summary>
public sealed class SacredEquipmentNameGeneration(SacredEquipmentNameContext context)
{
    public SacredEquipmentNameResult Existing(uint visualType, uint nativeReference = 0)
    {
        var reference = nativeReference == 0 ? context.ResourceId(visualType) : nativeReference;
        var hash = (reference & 0x80000000) != 0 ? reference & 0x7FFFFFFF : Hash(reference.ToString(CultureInfo.InvariantCulture));
        return new(nativeReference, reference is 0 or uint.MaxValue ? "" : context.Resources.Strings.GetValueOrDefault(hash, ""));
    }

    public SacredEquipmentNameResult Generate(Span<byte> bytes, uint visualType, SacredRandomSequence random)
    {
        if (bytes.Length != SacredEquipmentInstanceLayout.Size) throw new ArgumentException("Expected 130-byte item values.", nameof(bytes));
        // cItemBase::getWeaponInfo(-1) orders each signed damage range before naming.
        for (var i = 0; i < 4; i++)
        {
            var low = bytes.Slice(28 + i * 2, 2);
            var high = bytes.Slice(36 + i * 2, 2);
            if (BinaryPrimitives.ReadInt16LittleEndian(high) < BinaryPrimitives.ReadInt16LittleEndian(low))
            {
                var first = BinaryPrimitives.ReadInt16LittleEndian(low);
                high.CopyTo(low);
                BinaryPrimitives.WriteInt16LittleEndian(high, first);
            }
        }
        var values = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(bytes);
        var basis = Canonical(values.ItemType);
        if (basis == 0) return Existing(visualType);
        var rules = context.Rules;
        var subtype = values.EquipmentTypeCode;
        string? selectedKey = rules.DefaultKeys[subtype < rules.DefaultKeys.Count ? subtype : 0];
        var selected = Find(selectedKey);
        var rarity = (values.Flags >> 16) & 15;
        var band = rarity < 5 ? 0 : rarity < 9 ? 1 : rarity < 13 ? 2 : 3;
        var element = SacredEquipmentNameElement.Calculate(values, context.LevelFactorHundred);
        var amount = element & 0x1FF;
        var elementBand = (element & 0x400) != 0 && amount > 50 ? 3
            : (element & 0x800) != 0 && amount > 40 ? 2 : (element & 0x1000) != 0 && amount > 30 ? 1 : 0;
        var prefix = rules.Prefix + rules.RarityPrefixes[band] + rules.ElementPrefixes[elementBand];
        var type = context.ModifierType(basis);
        if (type is null) return Result();
        if (type == -1 && context.Shared(basis) is { } shared)
        {
            var usage = shared.UsageIdentifier;
            type = usage > 0 && usage <= rules.UsageTypes.Count ? rules.UsageTypes[usage - 1] : -1;
            if (type == -2) type = SwordType(basis, usage);
        }
        if (type is >= 0 && type < rules.Families.Count)
        {
            var family = type.Value;
            if (family == (int)SacredEquipmentType.TwoHandedSword && context.Shared(basis)?.UsageIdentifier == 1)
                family = (int)SacredEquipmentType.Sword;
            if (family == (int)SacredEquipmentType.MageStaff)
            {
                var authored = AuthoredOrdinary(Canonical(basis));
                if (authored == 0)
                {
                    _ = random.Next() % 40; // Original discarded initial staff-name choice.
                    selectedKey = SacredEquipmentStaffName.Key(values, rules.DefaultKeys[(int)SacredEquipmentType.MageStaff]);
                    selected = Find(selectedKey);
                    if (!HasText(selected))
                    {
                        selectedKey = rules.DefaultKeys[(int)SacredEquipmentType.MageStaff];
                        selected = Find(selectedKey);
                    }
                    return Result();
                }
                if (HasText(authored)) { selected = authored; selectedKey = null; return Result(); }
            }
            else if (family == (int)SacredEquipmentType.Gloves && context.Category(basis) == SacredItemCategory.Weapon)
            {
                var authored = AuthoredOrdinary(Canonical(basis));
                if (HasText(authored)) { selected = authored; selectedKey = null; return Result(); }
            }
            prefix += rules.Families[family];
        }
        var originalKey = prefix;
        var original = Find(originalKey);
        var rank = SacredEquipmentNameBonuses.Rank(values, context.Category(values.ItemType) is SacredItemCategory.Ring or SacredItemCategory.Amulet, context.Curves);
        if (rank == 0)
        {
            if (random.Next() % 2 != 0 && Find(prefix + "1") is var alternate && alternate != 0)
            { selected = alternate; selectedKey = prefix + "1"; return Result(); }
        }
        else
        {
            for (var i = rank; i > 0; i--)
            {
                var key = prefix + i.ToString(CultureInfo.InvariantCulture);
                var found = Find(key);
                if (found == 0) continue;
                selected = found; selectedKey = key; return Result();
            }
        }
        if (original != 0) { selected = original; selectedKey = originalKey; }
        return Result();

        SacredEquipmentNameResult Result() => Existing(visualType, selected) with { GeneratedKey = selectedKey };
    }

    private uint Canonical(uint type)
    {
        for (var i = 0; i < context.Classes.MaximumParentLinks && context.Classes.ParentResources.Any(range => range.Contains(type)); i++)
        {
            var shared = context.Shared(type);
            if (shared is null) return 0;
            type = shared.Value.BaseItemId;
        }
        return type;
    }

    private int SwordType(uint type, byte usage)
    {
        var name = context.SymbolicName(type);
        if (name is null) return 0;
        var axe = !context.Rules.SwordPatterns.Any(pattern => name.Contains(pattern, StringComparison.Ordinal)) &&
            context.Rules.AxePatterns.Any(pattern => name.Contains(pattern, StringComparison.Ordinal));
        return usage == 2 ? axe ? 6 : 4 : axe ? 5 : 0;
    }

    private uint AuthoredOrdinary(uint type) => context.Shared(type) is { } shared && shared.SetType == 0 && (shared.RawFlags & 0xF0000) < 0x40000
        ? context.ResourceId(type) : 0;
    private uint Find(string key) { var hash = Hash(key); return hash != 0 && context.Resources.Strings.ContainsKey(hash) ? hash | 0x80000000 : 0; }
    private bool HasText(uint reference) => reference != 0 && Existing(0, reference).Text.Length != 0;
    private static uint Hash(string key) => SacredResourceHash.Compute(key);
}
