using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Executed opcode-40 registrations. Native reserves entry zero by duplicating
/// the first record and clearing only its first word and first name byte.</summary>
public sealed class WorldScriptRegistrationTable
{
    private readonly List<byte[]> _records = [];
    /// <summary>Original vector capacity, not List capacity. Unknown after import;
    /// fresh native insertion grows to size + max(size, 1).</summary>
    public int? EstablishedCapacity { get; private set; } = 0;
    internal bool AppendPreservesCallerIndex => _records.Count > 0 &&
        EstablishedCapacity is { } capacity && _records.Count < capacity;
    public int Count => _records.Count;
    public byte[] GetRecord(int index) => _records[index].ToArray();
    /// <summary>Import already established rows without adding startup's reserved duplicate.</summary>
    public void Import(IEnumerable<SacredScriptDialog> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.Select(record => record.ToPayload()).ToArray();
        _records.Clear();
        _records.AddRange(copy);
        EstablishedCapacity = null;
    }

    /// <summary>Gold 0x483A51: only positive selected slots bind; an out-of-range
    /// positive slot still changes the actor dialog but has no table attachment.</summary>
    public void AttachInitial(uint actorReference, uint previousSlot, int selectedSlot)
    {
        if (selectedSlot <= 0) return;
        if (previousSlot > 0 && previousSlot < _records.Count)
            BinaryPrimitives.WriteUInt32LittleEndian(_records[(int)previousSlot].AsSpan(SacredScriptDialogLayout.AttachedObjectReferenceOffset), 0);
        if (selectedSlot < _records.Count)
            BinaryPrimitives.WriteUInt32LittleEndian(_records[selectedSlot].AsSpan(SacredScriptDialogLayout.AttachedObjectReferenceOffset), actorReference);
    }

    /// <summary>Gold SetNPCState 0x461DF2..0x461F16 also detaches for a cleared or missing dialog.</summary>
    public void AttachState(uint actorReference, uint previousSlot, int selectedSlot)
    {
        if (previousSlot > 0 && previousSlot < _records.Count)
            BinaryPrimitives.WriteUInt32LittleEndian(_records[(int)previousSlot].AsSpan(SacredScriptDialogLayout.AttachedObjectReferenceOffset), 0);
        if (selectedSlot > 0 && selectedSlot < _records.Count)
            BinaryPrimitives.WriteUInt32LittleEndian(_records[selectedSlot].AsSpan(SacredScriptDialogLayout.AttachedObjectReferenceOffset), actorReference);
    }

    internal void Append(SacredScriptCommand command)
    {
        var data = command.Bytes.Span;
        if (command.Opcode != SacredScriptRegistrationLayout.Opcode || data.Length != SacredScriptRegistrationLayout.SerializedSize + 4 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length)
            throw new InvalidDataException("Opcode 40 requires one complete embedded 80-byte registration.");
        var record = data[4..].ToArray();
        if (_records.Count == 0)
        {
            var reserved = record.ToArray();
            reserved.AsSpan(0, 5).Clear();
            AddWithCapacity(reserved);
        }
        AddWithCapacity(record);
    }

    private void AddWithCapacity(byte[] record)
    {
        if (EstablishedCapacity == _records.Count)
            EstablishedCapacity = checked(_records.Count + Math.Max(_records.Count, 1));
        _records.Add(record);
    }
}
