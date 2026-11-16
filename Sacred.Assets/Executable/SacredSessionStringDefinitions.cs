using Sacred.Core.Executable;
using Sacred.Core.Executable.Layouts;

namespace Sacred.Assets.Executable;

/// <summary>Original string getter definitions for the importer's actual
/// arguments. Actual Settings.cfg parsing and alternate CD-key arguments are
/// outside this reader; no setting keys, host names or defaults are copied.</summary>
public static class SacredSessionStringDefinitions
{
    public static SacredStringSettingDefinition Read(byte[] image, SacredSessionStringSetting setting)
    {
        using var pe = new SacredSessionExecutableImage(image);
        var getter = pe.FollowCall(pe.ImporterStart + (pe.Gold ? SacredExecutableSessionStringLayout.GoldCall(setting) : SacredExecutableSessionStringLayout.PureHdCall(setting)));
        if (setting is SacredSessionStringSetting.CdKey or SacredSessionStringSetting.CdKey2)
        {
            var lengthAt = getter + SacredExecutableSessionStringLayout.CdKeyComparisonLengthOperand;
            if (!pe.Bytes(lengthAt - 2, 2).SequenceEqual<byte>([0x83, 0xf8])) throw new InvalidDataException("Unknown original CD-key length comparison.");
            if (pe.Literal(getter, SacredExecutableSessionStringLayout.CdKeyReplacementOperand, 0x68).Length != 0)
                throw new InvalidDataException("Nonempty CD-key replacement requires another native comparison.");
            return new(pe.Literal(getter, SacredExecutableSessionStringLayout.CdKeyReadOperand, 0x68), default, null,
                pe.Literal(getter, SacredExecutableSessionStringLayout.CdKeyEmptyReturnOperand, 0x68),
                new(pe.Literal(getter, SacredExecutableSessionStringLayout.CdKeySeparatorOperand, 0xbf), unchecked((uint)(sbyte)pe.Bytes(lengthAt, 1)[0])));
        }
        if (setting is SacredSessionStringSetting.Login or SacredSessionStringSetting.LobbyPassword)
            return new(pe.Range(getter, SacredExecutableSessionStringLayout.DirectKeyOperand), default, null, null, null);
        if (setting is SacredSessionStringSetting.Ip or SacredSessionStringSetting.AseAddress)
            return new(pe.Range(getter, SacredExecutableSessionStringLayout.AddressKeyOperand), default, null,
                pe.Range(getter, SacredExecutableSessionStringLayout.AddressEmptyReturnOperand), null);
        return new(pe.Literal(getter, SacredExecutableSessionStringLayout.LiteralReadKeyOperand, 0x68),
            pe.Literal(getter, SacredExecutableSessionStringLayout.DefaultKeyOperand, 0x68),
            pe.Literal(getter, SacredExecutableSessionStringLayout.DefaultWriteLiteralOperand, 0x68),
            pe.Literal(getter, SacredExecutableSessionStringLayout.DefaultReturnLiteralOperand, 0x68), null);
    }

    public static SacredStringSettingDefinition Load(string directory, SacredSessionStringSetting setting)
    {
        Exception? failure = null;
        foreach (var name in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(directory, name); if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path), setting); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { failure = error; }
        }
        throw new InvalidDataException("Original session string definition unavailable.", failure);
    }
}
