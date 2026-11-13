using System.Text;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Original 0x401A70 / 0x401AE0 slot binding kind and initial target
/// bone. Reads original branch tables and strings without executing native code.
/// connectGranny's model-dependent fallbacks, source pivot, actual binding and
/// attachment effects are separate obligations; this is not a binding result.</summary>
public sealed class SacredEquipmentBindingDefinitions
{
    private readonly SacredNativeTypeSwitch _switch;
    private readonly ulong _kind;
    private readonly ulong _target;
    private SacredEquipmentBindingDefinitions(SacredNativeTypeSwitch getter, ulong kind, ulong target) =>
        (_switch, _kind, _target) = (getter, kind, target);

    public SacredEquipmentBindingKind Kind(ushort slot)
    {
        var result = _switch.Read(_kind, slot);
        if (result > 2) throw new InvalidDataException("Unknown native equipment binding kind.");
        return (SacredEquipmentBindingKind)result;
    }
    public string? TargetBone(ushort slot, SacredItemCategory category)
    {
        var pointer = _switch.Read(_target, slot, (uint)category);
        if (pointer == 0) return null;
        var bytes = _switch.At(pointer, 32);
        var length = bytes.IndexOf((byte)0);
        if (length < 0) throw new InvalidDataException("Unterminated native equipment bone name.");
        return Encoding.Latin1.GetString(bytes[..length]);
    }

    public static SacredEquipmentBindingDefinitions Read(byte[] image)
    {
        ReadOnlySpan<byte> kindSignature = [0x8B, 0x44, 0x24, 4, 0x66, 0x3D, 0xF4, 1, 0x74];
        ReadOnlySpan<byte> targetSignature = [0x66, 0x8B, 0x44, 0x24, 4, 0x66, 0x3D, 0x0C, 0, 0x75];
        var kind = image.AsSpan().IndexOf(kindSignature);
        var target = image.AsSpan().IndexOf(targetSignature);
        if (kind < 0 || target < 0) throw new InvalidDataException("Original equipment binding getters not found.");
        var getter = new SacredNativeTypeSwitch(image);
        var result = new SacredEquipmentBindingDefinitions(getter, getter.Address(kind), getter.Address(target));
        _ = result.Kind(0);
        _ = result.TargetBone(0, 0);
        return result;
    }
    public static SacredEquipmentBindingDefinitions Load(string installation)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, name);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original equipment binding definitions unavailable.", failure);
    }
}
