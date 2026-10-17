namespace Sacred.Network.Gameplay;

/// <summary>Typed access to the object callback vector in original host bank 2.</summary>
public static class GoldObjectScriptCallbackCodec
{
    public const ushort BankOpcode = GoldWorldBankLayout.Bank2Opcode;
    public const int BankSection = 3;
    public static IReadOnlyList<GoldObjectScriptCallback> Read(GoldWorldBankSnapshot bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (bank.Opcode != BankOpcode) throw new ArgumentException("Object callbacks require host bank 2.", nameof(bank));
        var section = bank.Sections[BankSection];
        return Array.AsReadOnly(section.Records.Select(record => GoldObjectScriptCallback.FromPayload(record.Data)).ToArray());
    }
}
