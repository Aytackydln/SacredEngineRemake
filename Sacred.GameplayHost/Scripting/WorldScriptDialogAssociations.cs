using System.Buffers.Binary;
using System.Text;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x465280 registration-slot lookup and ordered association updates.</summary>
public sealed class WorldScriptDialogAssociations
{
    private readonly List<WorldScriptDialogAssociation> _records = [];
    public WorldScriptDialogAssociations() => Records = _records.AsReadOnly();
    public IReadOnlyList<WorldScriptDialogAssociation> Records { get; }
    /// <summary>Gold 0x549A23: first matching type whose stored qualifier is zero or
    /// matches the query. No match is distinct from matched slots zero and -1;
    /// native actor eligibility and random dialog fallback belong to the caller.</summary>
    public int? FindRegistrationSlot(uint typeReference, ushort qualifier) => _records
        .FirstOrDefault(row => row.TypeReference == typeReference &&
            (row.Qualifier == 0 || row.Qualifier == qualifier))?.RegistrationSlot;

    /// <summary>Only bank 3 section 5, in native row order. Its count belongs in
    /// header word 5. Explicitly normalizes the audited 74 nonsemantic caller bytes
    /// per row; raw ToPayload remains guarded and imported raw bytes stay intact.</summary>
    public byte[] WriteNormalizedBankSection()
    {
        var bytes = new byte[checked(_records.Count * SacredScriptDialogAssociationLayout.SerializedSize)];
        for (var i = 0; i < _records.Count; i++)
            _records[i].ToNormalizedPayload().CopyTo(bytes, i * SacredScriptDialogAssociationLayout.SerializedSize);
        return bytes;
    }
    public void Import(IEnumerable<WorldScriptDialogAssociation> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.ToArray();
        _records.Clear();
        _records.AddRange(copy);
    }
    public void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        var slot = -1;
        uint type = 0;
        ushort qualifier = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.TypeId:
                    type = unchecked((uint)operand.Integer!.Value);
                    break;
                case SacredScriptArgumentKind.Integer:
                    qualifier = unchecked((ushort)(operand.Integer ?? throw new InvalidDataException("Dialog qualifier requires native variable-expression resolution.")));
                    break;
                case SacredScriptArgumentKind.DialogRegistrationName:
                    slot = -1;
                    for (var index = 0; index < context.Registrations.Count; index++)
                    {
                        var bytes = context.Registrations.GetRecord(index).AsSpan(4, 64);
                        var end = bytes.IndexOf((byte)0);
                        var name = Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
                        if (!WorldScriptNameComparer.Instance.Equals(name, operand.Text!)) continue;
                        slot = index;
                        break;
                    }
                    break;
                case SacredScriptArgumentKind.DialogRegistrationNumber:
                    var number = operand.Integer!.Value;
                    slot = number == 0 ? 0 : -1;
                    if (number == 0) break;
                    for (var index = 0; index < context.Registrations.Count; index++)
                    {
                        if (BinaryPrimitives.ReadUInt32LittleEndian(context.Registrations.GetRecord(index)) != (uint)number) continue;
                        slot = index;
                        break;
                    }
                    break;
                // Other successfully decoded shapes are ignored by the native handler.
            }
        }
        var existing = _records.FindIndex(row => row.TypeReference == type && (qualifier == 0 || row.Qualifier == qualifier));
        if (existing >= 0) _records[existing] = _records[existing].Bind(slot, qualifier);
        else _records.Add(WorldScriptDialogAssociation.Create(slot, type, qualifier));
    }
}
