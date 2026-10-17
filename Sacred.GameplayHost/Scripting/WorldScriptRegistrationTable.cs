using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Executed opcode-40 registrations. Native reserves entry zero by duplicating
/// the first record and clearing only its first word and first name byte.</summary>
public sealed class WorldScriptRegistrationTable
{
    private readonly List<byte[]> _records = [];
    public int Count => _records.Count;
    public byte[] GetRecord(int index) => _records[index].ToArray();
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
            _records.Add(reserved);
        }
        _records.Add(record);
    }
}
