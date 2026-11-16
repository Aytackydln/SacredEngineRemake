using System.Buffers.Binary;
using System.Text;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Reached region activation profile: literal selectors and empty
/// setup/activation callbacks. Normal activation retains a private 0x1BA request,
/// converting all five native function indices to original name hashes.</summary>
internal static class WorldScriptRegionQuestActivation
{
    public static byte[] WriteBankSection(WorldScriptExecutionContext context)
    {
        if (!context.StartupCompleted || context.IsInvalidated || context.Quests is not { } quests)
            throw new InvalidDataException("Quest bank source requires completed valid startup and original quest rows.");
        var bytes = new byte[checked(quests.Count * SacredScriptQuestLayout.SerializedSize)];
        for (var index = 0; index < quests.Count; index++)
            WriteDefinition(context, quests.Get(index)).CopyTo(bytes, index * SacredScriptQuestLayout.SerializedSize);
        return bytes;
    }

    public static bool Supports(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (context.Quests is not { } quests) return false;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            int slot;
            if (operand.Kind == SacredScriptArgumentKind.Integer && operand.Integer is { } number)
                slot = Math.Max(0, quests.FindNumber(number));
            else if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                if (string.IsNullOrEmpty(operand.Text)) continue;
                slot = Enumerable.Range(0, quests.Count).FirstOrDefault(index =>
                    WorldScriptNameComparer.Instance.Equals(quests.Get(index).Name, operand.Text), -1);
                if (slot < 0) continue;
            }
            else return false;
            if (slot >= quests.Count) return false;
            var quest = quests.Get(slot);
            if ((quest.Flags & SacredScriptQuestLayout.ActiveFlag) != 0) continue;
            if (!Empty(quest.SetupFunction) || !Empty(quest.ActivationFunction)) return false;
        }
        return true;
        bool Empty(int index) => index == 0 || (uint)index >= context.Functions.Count || context.Functions[index].ByteLength == 0;
    }

    public static GoldMessage Create(WorldScriptExecutionContext context, int index, SacredScriptQuestDefinition quest)
    {
        if (context.LocalPlayerSlot is not (>= 1 and <= 16))
            throw new InvalidDataException("Region quest publication requires the actual host slot.");
        var payload = new byte[4 + SacredScriptQuestLayout.SerializedSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, checked((uint)index));
        var row = WriteDefinition(context, quest);
        row.CopyTo(payload, 4);
        var slot = (byte)context.LocalPlayerSlot.Value;
        return new(GoldQuestLayout.EntryOpcode, slot, slot, 0, payload);
    }

    private static byte[] WriteDefinition(WorldScriptExecutionContext context, SacredScriptQuestDefinition quest)
    {
        var row = quest.ToPayload();
        foreach (var offset in GoldQuestLayout.EntryCallbackOffsets)
        {
            var function = BinaryPrimitives.ReadUInt32LittleEndian(row.AsSpan(offset));
            var hash = function < context.Functions.Count
                ? SacredResourceHash.ComputeDefaultCLocale(Encoding.Latin1.GetBytes(context.Functions[(int)function].Name)) : 0;
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(offset), hash);
        }

        return row;
    }
}
