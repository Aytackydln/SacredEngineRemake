using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compose only established source producers from retained execution.
/// Pending sources are explicit; no full header, missing empty section or upload
/// is manufactured. Required callback/sector effects still precede final banks.</summary>
internal static class WorldHostBankSourcePreparation
{
    public static IReadOnlyList<WorldHostBankSource> Prepare(WorldHostStartupState startup)
    {
        var context = startup.Execution;
        if (!context.StartupCompleted || context.IsInvalidated)
            throw new InvalidOperationException("Bank sources require completed valid startup on its current lifetime.");
        var sources = new List<WorldHostBankSource>();
        Add("startup parameters", GoldWorldBankLayout.Bank1Opcode, null, 1, context.StartupParameters.ToBankBlock);
        Add("regional replacement block", GoldWorldBankLayout.Bank1Opcode, null, 1, context.WriteRegionReplacementBankBlock);
        Add("script regions", GoldWorldBankLayout.Bank1Opcode, 5,
            (uint)(context.ScriptRegions?.Records.Count ?? 0), () =>
                (context.ScriptRegions ?? throw new InvalidDataException("Script-region producer is absent.")).WriteBankSection().Bytes);
        Add("runtime triggers", GoldWorldBankLayout.Bank1Opcode, 6, startup.Triggers.Count, startup.Triggers.WriteBankSection);
        Add("script quest definitions", GoldWorldBankLayout.Bank3Opcode, 1,
            (uint)(context.Quests?.Count ?? 0), () =>
                (context.Quests ?? throw new InvalidDataException("Quest producer is absent.")).WriteBankSection(context));
        Add("normalized dialog associations", GoldWorldBankLayout.Bank3Opcode, 5,
            (uint)context.DialogAssociations.Records.Count, context.DialogAssociations.WriteNormalizedBankSection);
        sources.Add(new("region singleton final flags", GoldWorldBankLayout.Bank1Opcode, null, GoldWorldRegionTableLayout.Count, ReadOnlyMemory<byte>.Empty,
            "Final pre-upload region initialization/entry effects are not established; constructor defaults are insufficient."));
        return sources.AsReadOnly();

        void Add(string name, ushort bank, int? section, uint count, Func<byte[]> write)
        {
            try { sources.Add(new(name, bank, section, count, write(), null)); }
            catch (InvalidDataException error) { sources.Add(new(name, bank, section, count, ReadOnlyMemory<byte>.Empty, error.Message)); }
        }
    }
}
