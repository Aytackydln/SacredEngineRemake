using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Recognize verified stream layers by markers, without guessing a subtype from resource IDs.</summary>
public static class GoldObjectStateReader
{
    public static GoldPhysicalState? ReadPhysical(ReadOnlySpan<byte> payload) =>
        HasMarker(payload, GoldPhysicalStateLayout.MarkerOffset, GoldPhysicalStateLayout.Marker)
            ? GoldPhysicalState.FromPayload(payload) : null;

    public static GoldCreatureState? ReadCreature(ReadOnlySpan<byte> payload) =>
        HasMarker(payload, GoldPhysicalStateLayout.MarkerOffset, GoldPhysicalStateLayout.Marker) &&
        HasMarker(payload, GoldCreatureStateLayout.MarkerOffset, GoldCreatureStateLayout.Marker)
            ? GoldCreatureState.FromPayload(payload) : null;

    public static GoldItemState? ReadItem(ReadOnlySpan<byte> payload) =>
        HasMarker(payload, GoldPhysicalStateLayout.MarkerOffset, GoldPhysicalStateLayout.Marker) &&
        HasMarker(payload, GoldItemStateLayout.MarkerOffset, GoldItemStateLayout.Marker)
            ? GoldItemState.FromPayload(payload) : null;

    internal static void Validate(ReadOnlySpan<byte> payload, ushort opcode)
    {
        _ = GoldObjectBaseState.FromPayload(payload);
        if (!HasMarker(payload, GoldPhysicalStateLayout.MarkerOffset, GoldPhysicalStateLayout.Marker)) return;
        GoldPhysicalState.Validate(payload);
        if (HasMarker(payload, GoldItemStateLayout.MarkerOffset, GoldItemStateLayout.Marker))
            GoldItemState.Validate(payload);
        if (HasMarker(payload, GoldCreatureStateLayout.MarkerOffset, GoldCreatureStateLayout.Marker))
        {
            GoldCreatureState.Validate(payload);
            if (HasMarker(payload, GoldCreatureTailLayout.Offset, GoldCreatureValuesLayout.Marker))
                GoldCreatureTail.Validate(payload[GoldCreatureTailLayout.Offset..], opcode);
        }
    }

    internal static bool HasMarker(ReadOnlySpan<byte> payload, int offset, uint marker) =>
        payload.Length >= offset + 4 && BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]) == marker;
}
