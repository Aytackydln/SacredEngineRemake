using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Admission;

internal sealed class RosterLiveFixture
{
    private readonly byte[] _control = new byte[0x3ad0];
    private readonly string _mutation;
    private byte _row;
    private string _tooltipTarget = "net";
    private bool _multiplayer;
    public WorldRosterLiveServices Services { get; }
    public WorldControlMemory Controller { get; }
    public string[] Names { get; } = Enumerable.Repeat("", 17).ToArray();
    public string[] Identifiers { get; } = Enumerable.Repeat("", 17).ToArray();
    public List<object?[]> Events { get; } = [];
    public byte[] Roster => _control.AsSpan(0x54, GoldPlayerRosterLayout.Size).ToArray();
    public bool Multiplayer => _multiplayer;
    public uint ExpectedUiFamily { get; init; } = 5;
    public uint ExpectedUiEvent { get; init; } = 0x10;
    public uint ExpectedUiCommand { get; init; } = 0x38;
    public string ExpectedUiScope { get; init; } = "NETMGR";

    public RosterLiveFixture(JsonElement row)
    {
        _mutation = row.GetProperty("mutation").GetString()!;
        _multiplayer = row.GetProperty("multiplayer").GetBoolean();
        Controller = new(at => _control[at], (at, value) => _control[at] = value,
            at => BinaryPrimitives.ReadUInt32LittleEndian(_control.AsSpan((int)at)),
            (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(_control.AsSpan((int)at), value),
            (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(_control.AsSpan((int)at), value));
        Controller.WriteWord(0x14, row.GetProperty("local").GetUInt32());
        Controller.WriteByte(0x28, (byte)(row.GetProperty("flag28").GetBoolean() ? 1 : 0));
        var mask = row.GetProperty("old_mask").GetUInt32();
        for (byte slot = 1; slot <= 16; slot++) Controller.WriteByte(Row(slot) + 6, (byte)((mask >> (slot - 1)) & 1));
        Convert.FromHexString(row.GetProperty("staging_hex").GetString()!).CopyTo(_control, 0x16a);
        Services = new(Controller, new(slot => Names[slot], (slot, value) => Names[slot] = value,
            (slot, value) => Identifiers[slot] = value), Identifier, () => _multiplayer,
            Ui, Profile, () => Events.Add(["modal"]), () =>
            {
                Events.Add(["resources"]); return new(Localize);
            }, Refresh, Notice);
    }

    private string Identifier(string input)
    {
        _row++; _tooltipTarget = "net";
        Events.Add(["identifier", _row, Hex(input)]);
        if (_mutation == "identifier-name" && _row == 2) Names[_row] = "Changed";
        return input;
    }
    private void Ui(WorldRosterUiRequest request)
    {
        if (request.Family != ExpectedUiFamily || request.MessageType != ExpectedUiEvent ||
            request.EventId != ExpectedUiCommand || request.Scope != ExpectedUiScope)
            throw new Exception("Roster UI virtual getter/scope mismatch.");
        var target = request.Target == "UI_NET_LBPLAYERS" ? "net" : "lan";
        Events.Add(["ui", target, request.Mode, request.Slot, request.Style,
            request.Mode == 2 ? Hex(request.Name!) : "", request.Mode == 17 && request.Tooltip is not null]);
        if (request.Mode == 1 && target == "net" && _mutation == "reset-row")
        { Controller.WriteByte(Row(2) + 22, 0); Controller.WriteWord(Row(2) + 282, 0); }
        if (request.Mode == 2 && target == "net" && request.Slot == 2)
        {
            if (_mutation == "name-speed") Controller.WriteWord(Row(2) + 12, 0);
            if (_mutation == "name-multiplayer") _multiplayer = false;
            if (_mutation == "name-profile") Controller.WriteWord(Row(2) + 282, 0);
        }
        if (request.Mode == 17 && target == "net")
        {
            _tooltipTarget = "lan";
            if (request.Slot == 2 && _mutation == "tooltip-next")
            { Encoding.Unicode.GetBytes("Late\0").CopyTo(_control, (int)Row(3) + 22); Controller.WriteWord(Row(3) + 282, 1); }
        }
        if (request.Mode == 16 && target == "lan" && _mutation == "finalize-profile") Controller.WriteWord(Row(16) + 282, 0);
    }
    private string Localize(string key)
    {
        if (key != "UI_TOOLTIP_ZZZ") throw new Exception("Unexpected roster resource key.");
        Events.Add(["tooltip", _row, _tooltipTarget]);
        if (_mutation == "tooltip-speed" && _row == 2) Controller.WriteWord(Row(2) + 12, 0);
        return "K";
    }
    private void Profile(WorldRosterProfileRequest request)
    {
        Events.Add(["profile", request.Slot, Hex(request.Name), Convert.ToHexString(SHA256.HashData(request.Profile.Span)).ToLowerInvariant()]);
        if (_mutation == "profile-next" && request.Slot == 2)
        { Controller.WriteWord(Row(3) + 282, 0); Controller.WriteByte(Row(3) + 22, 0); }
    }
    private void Refresh()
    {
        Events.Add(["refresh"]);
        if (_mutation == "refresh-gate-on") Controller.WriteByte(0x28, 1);
        if (_mutation == "refresh-gate-off") Controller.WriteByte(0x28, 0);
        if (_mutation == "refresh-local") Controller.WriteWord(0x14, 2);
        if (_mutation == "refresh-presence") Controller.WriteByte(Row(3) + 6, 1);
    }
    private void Notice(byte slot)
    {
        Events.Add(["notice", slot]);
        if (_mutation == "notice-gate-off") Controller.WriteByte(0x28, 0);
        if (_mutation == "notice-local") Controller.WriteWord(0x14, 3);
        if (_mutation == "notice-presence") Controller.WriteByte(Row(3) + 6, 0);
    }
    public static string Hex(string value) => Convert.ToHexString(Encoding.Unicode.GetBytes(value)).ToLowerInvariant();
    private static uint Row(byte slot) => 0x54u + (uint)(slot * GoldPlayerDataLayout.Size);
}
