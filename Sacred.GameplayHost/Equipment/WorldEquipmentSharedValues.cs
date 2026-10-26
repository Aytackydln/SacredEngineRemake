using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Weapon;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Mutable native shared values for one independent generation session.
/// Original defaults stay immutable; price/class changes persist across its rolls.</summary>
public sealed class WorldEquipmentSharedValues(SacredEquipmentGenerationData data)
{
    private readonly Dictionary<uint, byte[]> _values = new();

    public SacredEquipmentSelectionCandidate Candidate(uint type)
    {
        if (!_values.TryGetValue(type, out var bytes)) _values[type] = bytes = data.Templates.GetInstance(type);
        var definition = data.Templates.Equipment[type];
        return new(type, definition.UsageIdentifier, definition.SetType,
            MemoryMarshal.Read<SacredEquipmentInstanceLayout>(bytes), data.Modifiers.Find(type)?.Definition);
    }

    public SacredEquipmentSelectionCandidate Prepare(SacredEquipmentSelectionCandidate candidate)
    {
        var bytes = _values[candidate.ItemType];
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) == 0 || bytes[20] == 0)
        {
            var tuning = data.Tuning;
            var price = SacredEquipmentPricing.CalculateOrdinary(bytes, data.PriceContext(candidate.ItemType), tuning.Pricing,
                tuning.Levels, tuning.Bonuses, tuning.Normalization, tuning.Sockets);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), price);
        }
        return Candidate(candidate.ItemType);
    }

    public SacredEquipmentSelectionCandidate InferClasses(SacredEquipmentSelectionCandidate candidate)
    {
        var flags = candidate.Values.Flags & 0xC0003F;
        if (flags is not (0 or 0xC0003F)) return candidate;
        var gate = data.ClassGate;
        SacredEquipmentVariantResult Probe(uint source) => SacredEquipmentVariantLookup.ResolveCanonicalSource(candidate.ItemType,
            source, gate.MinimumReplacementSource, data.Classes, data.Variants, data.ParentResource,
            type => data.Templates.Items.TryGetValue(type, out var item) ? item.ModelDesc : null);
        var first = Probe(gate.FirstSource);
        if ((!first.Supported || first.ReplacementType != 0) && Probe(gate.SecondSource).Supported) return candidate;
        var name = data.ResourceName(candidate.ItemType);
        if (name?.Contains(gate.NameMarker, StringComparison.Ordinal) != true) return candidate;
        _ = SacredEquipmentClassInference.Apply(_values[candidate.ItemType], data.Classes, data.ParentResource, data.ResourceName);
        return Candidate(candidate.ItemType);
    }

    public byte[] Copy(uint type) { _ = Candidate(type); return _values[type].ToArray(); }
}
