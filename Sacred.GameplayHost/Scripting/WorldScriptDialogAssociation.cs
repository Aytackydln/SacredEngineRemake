using System.Buffers.Binary;
using Layout = Sacred.Core.GameBin.Scripts.SacredScriptDialogAssociationLayout;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Association metadata with explicit raw-byte provenance. Raw output
/// requires complete storage; the separate normalized path clears only the
/// audited nonsemantic caller bytes.</summary>
public sealed class WorldScriptDialogAssociation
{
    private readonly byte[]? _bytes;
    private WorldScriptDialogAssociation(int slot, uint type, ushort qualifier, uint word, byte[]? bytes)
        => (RegistrationSlot, TypeReference, Qualifier, DefaultWord72, _bytes) = (slot, type, qualifier, word, bytes);
    public int RegistrationSlot { get; }
    public uint TypeReference { get; }
    public ushort Qualifier { get; }
    public uint DefaultWord72 { get; }
    public bool HasCompleteBytes => _bytes is not null;
    internal static WorldScriptDialogAssociation Create(int slot, uint type, ushort qualifier) => new(slot, type, qualifier, 0xFFFF, null);
    public static WorldScriptDialogAssociation FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Layout.SerializedSize) throw new InvalidDataException("Dialog association requires 88 bytes.");
        return new(BinaryPrimitives.ReadInt32LittleEndian(bytes[Layout.RegistrationSlotOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[Layout.TypeReferenceOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[Layout.QualifierOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[Layout.DefaultWordOffset..]), bytes.ToArray());
    }
    internal WorldScriptDialogAssociation Bind(int slot, ushort qualifier)
    {
        var bytes = _bytes?.ToArray();
        if (bytes is not null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Layout.RegistrationSlotOffset), slot);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Layout.QualifierOffset), qualifier);
        }
        return new(slot, TypeReference, qualifier, DefaultWord72, bytes);
    }
    public byte[] ToPayload() => _bytes?.ToArray() ?? throw new InvalidDataException("Raw dialog association serialization requires native caller-scratch byte provenance.");

    /// <summary>Explicit bank normalization: preserve all constructor-written fields and
    /// clear the 74 caller-scratch bytes not read by native association selection.
    /// These zeros are a canonical representation, not claimed native defaults.</summary>
    public byte[] ToNormalizedPayload()
    {
        var bytes = new byte[Layout.SerializedSize];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Layout.RegistrationSlotOffset), RegistrationSlot);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Layout.DefaultWordOffset), DefaultWord72);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Layout.TypeReferenceOffset), TypeReference);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Layout.QualifierOffset), Qualifier);
        return bytes;
    }
}
