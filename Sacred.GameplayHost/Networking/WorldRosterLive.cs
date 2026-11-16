using Sacred.Core.Pax.Hero;
using Sacred.Network.Admission;

namespace Sacred.GameplayHost.Networking;

/// <summary>Gold 0x7E4C20 complete valid-table consumer with a present resource
/// singleton. Reads current fields between synchronous services. The admission
/// owner supplies lifetime checks; services own allocation/presentation bodies.</summary>
internal static class WorldRosterLive
{
    private const uint Table = 0x54;
    private const uint TableEnd = Table + GoldPlayerRosterLayout.Size;

    internal static void Apply(WorldRosterLiveServices services, GoldPlayerRoster incoming, Action validateCurrent)
    {
        var control = services.Controller;
        var strings = services.Strings;
        var previousPresence = new bool[GoldPlayerRosterLayout.EntryCount];
        for (byte slot = 1; slot <= 16; slot++)
            previousPresence[slot] = control.ReadByte(Row(slot) + GoldPlayerDataLayout.PresenceByteOffset) != 0;
        var profileZero = Read(Table + GoldPlayerDataLayout.ProfileOffset, GoldPlayerDataLayout.ProfileBytes);
        var bytes = incoming.ToPayload();
        profileZero.CopyTo(bytes, GoldPlayerDataLayout.ProfileOffset);
        for (var index = 0; index < bytes.Length; index++) control.WriteByte(Table + (uint)index, bytes[index]);

        Ui(new("UI_NET_LBPLAYERS", 1, 1, 0));
        Ui(new("UI_LAN_LBPLAYERS", 1, 1, 0));
        Call(services.PrepareModal);
        validateCurrent();
        var resources = services.ResolveResources();
        validateCurrent();
        if (resources is null) throw new NotSupportedException("Live roster requires the compared present resource singleton.");
        for (byte slot = 1; slot <= 16; slot++)
        {
            strings.WriteName(slot, ReadName(Row(slot) + GoldPlayerDataLayout.NameOffset));
            validateCurrent();
            var identifier = services.Identifier(strings.ReadName(slot));
            validateCurrent();
            strings.WriteIdentifier(slot, identifier);
            if (strings.ReadName(slot).Length != 0)
            {
                Ui(new("UI_NET_LBPLAYERS", 2, slot, 0, strings.ReadName(slot)));
                Ui(new("UI_LAN_LBPLAYERS", 2, slot, Style(slot), strings.ReadName(slot)));
                Tooltip("UI_NET_LBPLAYERS", slot);
                Tooltip("UI_LAN_LBPLAYERS", slot);
            }
            if (control.ReadWord(Row(slot) + GoldPlayerDataLayout.ProfileOffset + SacredHeroProfileLayout.RosterUiGateOffset) != 0)
            {
                var profile = Read(Row(slot) + GoldPlayerDataLayout.ProfileOffset, GoldPlayerDataLayout.ProfileBytes);
                var request = new WorldRosterProfileRequest(slot, strings.ReadName(slot), profile);
                Call(() => services.Profile(request));
            }
        }
        Ui(new("UI_NET_LBPLAYERS", 16, 0, 0));
        Ui(new("UI_LAN_LBPLAYERS", 16, 0, 0));
        Call(services.Refresh);
        // Native samples +28 once after refresh; callbacks in the notice walk
        // can change current local/presence fields but do not re-test this gate.
        if (control.ReadByte(0x28) == 0) return;
        for (byte slot = 1; slot <= 16; slot++)
            if (slot != control.ReadWord(0x14) && control.ReadByte(Row(slot) + GoldPlayerDataLayout.PresenceByteOffset) != 0 && !previousPresence[slot])
            {
                var noticeSlot = slot;
                Call(() => services.JoinNotice(noticeSlot));
            }

        void Call(Action action) { validateCurrent(); action(); validateCurrent(); }
        void Ui(WorldRosterUiRequest request) => Call(() => services.Ui(request));
        bool LowBandwidth(byte slot) => services.ReadMultiplayer() && control.ReadWord(Row(slot) + GoldPlayerDataLayout.ConnectionSpeedOffset) == 1;
        uint Style(byte slot) => LowBandwidth(slot) ? 0x1b5u : 0;
        void Tooltip(string target, byte slot)
        {
            string? tooltip = null;
            if (LowBandwidth(slot))
            {
                validateCurrent(); tooltip = resources.Localize("UI_TOOLTIP_ZZZ"); validateCurrent();
            }
            Ui(new(target, 17, slot, Style(slot), Tooltip: tooltip));
        }
        byte[] Read(uint at, int count)
        {
            var result = new byte[count];
            for (var index = 0; index < count; index++) result[index] = control.ReadByte(at + (uint)index);
            return result;
        }
        string ReadName(uint at)
        {
            var result = new List<char>();
            for (; at + 1 < TableEnd; at += 2)
            {
                var unit = (char)(control.ReadByte(at) | control.ReadByte(at + 1) << 8);
                if (unit == 0) return new string(result.ToArray());
                result.Add(unit);
            }
            throw new NotSupportedException("Live roster name requires termination beyond the compared table storage.");
        }
    }

    private static uint Row(byte slot) => Table + (uint)(slot * GoldPlayerDataLayout.Size);
}
