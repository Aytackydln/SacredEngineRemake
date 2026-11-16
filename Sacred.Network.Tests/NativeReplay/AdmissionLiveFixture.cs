using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

/// <summary>Supplied service bodies for the full native outer-branch comparison.
/// Roster construction/application uses established pure projections. This fixture
/// does not implement live per-row services or provide Engine bindings.</summary>
internal sealed class AdmissionLiveFixture
{
    private readonly JsonElement _row;
    private readonly byte[] _control = new byte[0x3ad0], _connection = new byte[0x41000];
    private readonly string _scenario;
    private int _queries;
    public WorldControlMemory Controller { get; }
    public WorldControlMemory Connection { get; }
    public WorldAdmissionLiveServices Services { get; }
    public List<string> Effects { get; } = [];
    public List<(string Stage, string Header)> Boundaries { get; } = [];
    public List<GoldLobbyUiRequest> Ui { get; } = [];
    public List<string> Chats { get; } = [];
    public List<bool> Queries { get; } = [];
    public byte[] Header => _control[..0x30];
    public byte[] Roster => _control.AsSpan(0x54, GoldPlayerRosterLayout.Size).ToArray();

    public AdmissionLiveFixture(JsonElement row, byte[] entryZero)
    {
        _row = row.Clone(); _scenario = row.GetProperty("scenario").GetString()!;
        Controller = Memory(_control); Connection = Memory(_connection);
        entryZero.CopyTo(_control, 0x54);
        Controller.WriteByte(1, row.GetProperty("old_host").GetByte());
        Controller.WriteByte(2, row.GetProperty("flag2").GetByte());
        Controller.WriteByte(3, 0xa5);
        Controller.WriteByte(9, (byte)(row.GetProperty("flag9").GetBoolean() ? 1 : 0));
        Controller.WriteByte(0x28, 1);
        Controller.WriteByte(0x381b, (byte)(row.GetProperty("flag381b").GetBoolean() ? 1 : 0));
        Controller.WriteWord(0x14, 31); Controller.WriteWord(0x24, 0x12345678);
        Connection.WriteWord(0x48, 31);
        Services = new(Controller, () => Connection, Reset, Query, AcceptedRoster, DispatchUi,
            Controller117, FinalizeConnection, Chat);
    }

    private void Boundary(string stage, int length = 0x30) =>
        Boundaries.Add((stage, Convert.ToHexString(_control.AsSpan(0, length)).ToLowerInvariant()));
    private void Gate(string stage)
    {
        if (_scenario.StartsWith(stage + "-gate-"))
            Controller.WriteByte(9, (byte)(_scenario.EndsWith("on") ? 1 : 0));
    }
    private void Reset()
    {
        Boundary("reset", 4); Gate("reset"); Effects.Add("controller-reset");
    }
    private bool Query()
    {
        var stage = "query" + (_queries + 1);
        Boundary(stage); Gate(stage);
        var value = _row.GetProperty("queries")[_queries++].GetBoolean();
        Queries.Add(value); return value;
    }
    private void AcceptedRoster(GoldAdmissionEvent admission)
    {
        var prefix = WorldAdmissionControllerPrefix.Apply(new(Controller.ReadByte(3), Controller.ReadByte(1),
            Controller.ReadWord(0x14), Controller.ReadWord(0x24), Connection.ReadWord(0x48)), admission, false, false);
        Controller.WriteWord(0x14, prefix.State.LocalReference);
        Controller.WriteWord(0x24, prefix.State.HostToken);
        Controller.WriteByte(1, prefix.State.HostByte1);
        Connection.WriteWord(0x48, prefix.State.ConnectionLocalReference);
        var previous = GoldPlayerRoster.FromPayload(Roster);
        var inputs = new WorldAdmissionRosterInputs(0x76543210, 7, 0x4b,
            _row.GetProperty("connection_flags").GetUInt32(), 0x22222222,
            Encoding.Unicode.GetBytes("Hero"), Encoding.Unicode.GetBytes("Saved"));
        var staged = WorldAdmissionRosterBuilder.Build(prefix, admission, previous.GetEntry(0), inputs);
        Controller.WriteByte(0x48, staged.Flag48); Controller.WriteByte(0x3819, staged.Flag3819);
        var applied = WorldRosterApplication.Apply(staged.Roster, staged.Roster, admission.PlayerSlot,
            Controller.ReadByte(0x28) != 0, true, name => name);
        applied.Roster.ToPayload().CopyTo(_control, 0x54);
        Effects.Add("roster"); Boundary("roster"); Gate("roster");
    }
    private void DispatchUi(GoldLobbyUiRequest request)
    {
        Effects.Add("ui:" + Ui.Count); Ui.Add(request); Boundary("ui");
        if (request.Target != "UI_WND_NETWORK") return;
        if (_scenario.StartsWith("network-role-")) Controller.WriteByte(0x381b, (byte)(_scenario.EndsWith("on") ? 1 : 0));
        if (_scenario.StartsWith("network-host-")) Controller.WriteByte(1, (byte)(_scenario.EndsWith("on") ? 1 : 0));
    }
    private void Controller117()
    {
        Boundary("117"); Effects.Add("send:117");
        if (_scenario != "117-identities") return;
        Controller.WriteWord(0x14, 91); Connection.WriteWord(0x48, 92); Controller.WriteWord(0x24, 93);
    }
    private void FinalizeConnection()
    {
        Boundary("finalizer"); Effects.Add("finalizer");
        if (_scenario.StartsWith("finalizer-host-")) Controller.WriteByte(1, (byte)(_scenario.EndsWith("on") ? 1 : 0));
    }
    private void Chat(string key)
    {
        Effects.Add("chat:" + Chats.Count); Chats.Add(key);
        if (_scenario == "chat-flag2") Controller.WriteByte(2, 7);
    }
    private static WorldControlMemory Memory(byte[] bytes) => new(at => bytes[at], (at, value) => bytes[at] = value,
        at => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
        (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
        (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
}
