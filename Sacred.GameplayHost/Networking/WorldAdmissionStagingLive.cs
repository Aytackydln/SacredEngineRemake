using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Gold 0x7E1332..0x7E15AF, with current destination reads after services.
/// Returns the submitted table; the receipt then executes its live consumer.</summary>
internal static class WorldAdmissionStagingLive
{
    internal static GoldPlayerRoster Apply(GoldAdmissionEvent admission, WorldAdmissionStagingServices services, Action validate)
    {
        var control = services.Roster.Controller;
        var strings = services.Roster.Strings;
        control.WriteWord(0x14, admission.PlayerSlot);
        Connection().WriteWord(0x48, admission.PlayerSlot);
        Log("accepted");
        if ((admission.Flags & 1) != 0) { control.WriteByte(1, 1); Log("host"); }
        if (admission.HostedServerWindow != 0) { control.WriteWord(0x24, admission.HostedServerWindow); Log("window"); }
        strings.WriteName(Slot(), Decode(services.ReadLocalName().Span));
        validate();
        var identifier = services.Roster.Identifier(Decode(services.ReadLocalName().Span));
        validate();
        strings.WriteIdentifier(Slot(), identifier);
        control.WriteByte(0x48, (byte)((Connection().ReadWord(0x40424) & 2) == 0 ? 1 : 0));
        var zero = Read(0x54, GoldPlayerDataLayout.Size);
        for (uint index = 0; index < GoldPlayerRosterLayout.Size; index++) control.WriteByte(0x54 + index, 0);
        Write(0x54, zero);
        var name = services.ReadLocalName().Span;
        Write(Row() + GoldPlayerDataLayout.NameOffset, name[..Math.Min(name.Length & ~1, GoldPlayerDataLayout.NameBytes)]);
        control.WriteByte(Row() + GoldPlayerDataLayout.CharacterTypeOffset, control.ReadByte(0x44));
        var session = services.ReadSessionWord();
        control.WriteWord(Row() + GoldPlayerDataLayout.SessionWord0Offset, session);
        control.WriteWord(Row() + GoldPlayerDataLayout.LobbyIdentityOffset, control.ReadWord(0x14));
        Write(Row() + GoldPlayerDataLayout.ProfileOffset, Read(0x16a, GoldPlayerDataLayout.ProfileBytes));
        if ((admission.Flags & 8) != 0)
        {
            control.WriteByte(0x3819, 1);
            var replacement = services.ReadReplacementName().Span;
            Write(Row() + GoldPlayerDataLayout.NameOffset, replacement[..(replacement.Length & ~1)]);
            validate(); var alternate = services.ReadAlternateIdentity(); validate();
            control.WriteWord(Row() + GoldPlayerDataLayout.LobbyIdentityOffset, unchecked((uint)alternate));
            control.WriteByte(Row() + GoldPlayerDataLayout.ProfileByte5Offset, control.ReadByte(0x37b8));
        }
        else control.WriteByte(0x3819, 0);
        return GoldPlayerRoster.FromPayload(Read(0x54, GoldPlayerRosterLayout.Size));

        void Log(string stage) { validate(); services.Log(stage); validate(); }
        WorldControlMemory Connection() => services.ReadConnection() ?? throw new NotSupportedException("Accepted admission requires current connection storage.");
        byte Slot()
        {
            var slot = control.ReadWord(0x14);
            if (slot is < 1 or > 16) throw new NotSupportedException("Accepted admission destination exceeds the compared player table.");
            return (byte)slot;
        }
        uint Row() => 0x54u + (uint)(Slot() * GoldPlayerDataLayout.Size);
        byte[] Read(uint at, int count)
        {
            var result = new byte[count];
            for (var index = 0; index < count; index++) result[index] = control.ReadByte(at + (uint)index);
            return result;
        }
        void Write(uint at, ReadOnlySpan<byte> bytes)
        {
            if (at < 0x54 || bytes.Length > 0x54 + GoldPlayerRosterLayout.Size - at)
                throw new NotSupportedException("Native admission copy exceeds the compared roster storage.");
            for (var index = 0; index < bytes.Length; index++) control.WriteByte(at + (uint)index, bytes[index]);
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (var index = 0; index < chars.Length; index++) chars[index] = (char)(bytes[index * 2] | bytes[index * 2 + 1] << 8);
        return new string(chars);
    }
}
