using System.Buffers.Binary;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x48F7F0/0x46DB20 startup quest state. Failed callbacks invalidate
/// their owning executor; partially changed quest rows must never be uploaded.</summary>
public sealed class WorldScriptQuests
{
    private readonly List<byte[]> _rows;
    public WorldScriptQuests(IEnumerable<SacredScriptQuestDefinition> definitions) => _rows = definitions.Select(row => row.ToPayload()).ToList();
    public WorldScriptQuestJournal Journal { get; init; } = new();
    /// <summary>Restore explicitly supplied native runtime inputs, without inferring scratch defaults.</summary>
    public static WorldScriptQuests FromRuntime(IEnumerable<SacredScriptQuestDefinition> definitions,
        WorldScriptQuestJournal journal, IReadOnlyList<uint> scratch)
    {
        if (scratch.Count != SacredScriptJournalLayout.CompassBlockWordCount)
            throw new InvalidDataException("Expected five original pending compass words.");
        return new(definitions) { Journal = journal, Scratch = Array.AsReadOnly(scratch.ToArray()) };
    }
    internal void ClearScratch() => Scratch = Array.AsReadOnly(new uint[5]);
    internal void SetPendingCompass(uint x, uint y, uint radius)
    {
        if (Scratch is null) throw new InvalidDataException("Pending compass requires established native scratch words.");
        var words = Scratch.ToArray(); words[0] = x; words[1] = y; words[4] = radius;
        Scratch = Array.AsReadOnly(words);
    }
    internal void SetPendingObjectCompass(uint resource, uint radius, uint? reference)
    {
        if (Scratch is null) throw new InvalidDataException("Pending object compass requires established scratch words.");
        var words = Scratch.ToArray();
        words[0] = reference.HasValue ? SacredScriptJournalLayout.ObjectTargetMarker : SacredScriptJournalLayout.ResourceTargetMarker;
        words[1] = reference ?? resource;
        words[4] = radius;
        Scratch = Array.AsReadOnly(words);
    }
    public int Count => _rows.Count;
    public IReadOnlyList<uint>? Scratch { get; private set; }
    public SacredScriptQuestDefinition Get(int index) => SacredScriptQuestDefinition.FromPayload(_rows[index]);
    public bool IsActive(int number)
    {
        var slot = FindNumber(number);
        // Native 0x46BC68 reserves slot zero, even if an opcode-21 missing ID activated it.
        return slot > 0 && (Get(slot).Flags & SacredScriptQuestLayout.ActiveFlag) != 0;
    }
    public void Execute(SacredScriptCommand command, bool startup, Action<int> invoke)
    {
        if (!startup) throw new NotSupportedException("Quest activation live entry requires its native notification lifecycle.");
        Scratch = Array.AsReadOnly(new uint[5]); // Native clears 0xAB782C..0xAB783C at command entry.
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            int slot;
            if (operand.Kind == SacredScriptArgumentKind.Integer)
            {
                if (operand.Integer is null) throw new InvalidDataException("Quest number expression requires native resolution.");
                slot = Math.Max(0, FindNumber(operand.Integer.Value));
            }
            else if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                if (string.IsNullOrEmpty(operand.Text)) continue;
                slot = _rows.FindIndex(row => WorldScriptNameComparer.Instance.Equals(SacredScriptQuestDefinition.FromPayload(row).Name, operand.Text));
                if (slot < 0) continue;
            }
            else continue;
            Activate(slot, invoke);
        }
    }
    internal int FindNumber(int number) => _rows.FindIndex(row => BinaryPrimitives.ReadInt32LittleEndian(row) == number);
    private void Activate(int slot, Action<int> invoke)
    {
        if ((uint)slot >= _rows.Count) throw new InvalidDataException("Quest activation requires an established reserved quest row.");
        var flags = Get(slot).Flags;
        if ((flags & SacredScriptQuestLayout.ActiveFlag) != 0) return;
        Write(slot, SacredScriptQuestLayout.FlagsOffset, flags | SacredScriptQuestLayout.ActiveFlag | SacredScriptQuestLayout.ActivatingFlag);
        invoke(Get(slot).SetupFunction);
        Write(slot, SacredScriptQuestLayout.SetupFunctionOffset, 0);
        invoke(Get(slot).ActivationFunction);
        Write(slot, SacredScriptQuestLayout.FlagsOffset, Get(slot).Flags & ~SacredScriptQuestLayout.ActivatingFlag);
    }
    internal void WriteWord(int slot, int offset, uint value) => Write(slot,offset,value);
    private void Write(int slot, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_rows[slot].AsSpan(offset), value);
}
