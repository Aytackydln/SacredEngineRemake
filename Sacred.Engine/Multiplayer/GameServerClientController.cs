using System;
using System.Collections.Generic;
using System.IO;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Main-thread owner of the transport and verified gameplay dispatch.</summary>
internal sealed class GameServerClientController : IDisposable
{
    private GoldGameConnection? _connection;
    private readonly Dictionary<ushort, int> _opcodeCounts = [];
    private readonly Dictionary<uint, uint[]> _movements = [];
    private readonly Dictionary<uint, GoldObjectBaseState> _objects = [];
    private readonly Dictionary<uint, bool> _doors = [];
    private string _status = "offline";
    private string _admission = "transport diagnostics; native player profile and world entry pending";
    private int _eventsReceived;

    public string Status => $"{_status}; {_eventsReceived} events; {_objects.Count} base object states; {_movements.Count} movement refs; {_doors.Count} door refs; {_admission}";

    public bool TrySetOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = Status;
            foreach (var entry in _opcodeCounts) EngineLog.WriteLine($"Network opcode 0x{entry.Key:X4}: {entry.Value} events.");
        }
        else if (value.Equals("objects", StringComparison.OrdinalIgnoreCase))
        {
            message = $"{_objects.Count} retained Gold base object states; subtype import pending";
            foreach (var entry in _objects)
            {
                var state = entry.Value;
                EngineLog.WriteLine($"Network object {entry.Key}: type {state.ObjectType}, owner {state.Owner}, sector {state.Sector}, position {state.X}/{state.Y}, surface {state.Surface}, flags 0x{state.Flags:X8}.");
            }
        }
        else if (value.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            Disconnect();
            message = "server disconnected";
        }
        else if (Uri.TryCreate("tcp://" + value, UriKind.Absolute, out var endpoint) &&
                 endpoint.Port is > 0 and <= 65535 && endpoint.AbsolutePath == "/" &&
                 string.IsNullOrEmpty(endpoint.UserInfo) && string.IsNullOrEmpty(endpoint.Query) && string.IsNullOrEmpty(endpoint.Fragment))
        {
            Disconnect();
            _connection = new GoldGameConnection();
            _connection.Start(endpoint.DnsSafeHost, endpoint.Port);
            _status = $"connecting to {endpoint.Host}:{endpoint.Port}";
            message = _status;
        }
        else message = "use set server <host:port|status|objects|disconnect>; select the gameplay TCP port";
        return true;
    }

    public void Update()
    {
        if (_connection is not { } connection) return;
        for (var i = 0; i < 16 && connection.Events.TryRead(out var entry); i++)
        {
            switch (entry)
            {
                case GoldTransportConnected connected:
                    _status = $"TinCat connected, transport ID {connected.ConnectionId}";
                    EngineLog.WriteLine($"Network: {_status}; Sacred admission has not completed.");
                    break;
                case GoldFirstContactSent:
                    _admission = "FIRSTCONTACT sent; waiting for Sacred slot response";
                    EngineLog.WriteLine($"Network admission: {_admission}.");
                    break;
                case GoldMessagesReceived batch:
                    try { GoldGameDispatcher.Dispatch(batch.Messages, Receive); }
                    catch (InvalidDataException e)
                    {
                        EngineLog.WriteLine($"Network batch rejected: {e.Message}");
                        connection.Stop();
                    }
                    break;
                case GoldConnectionClosed closed:
                    _status = $"closed: {closed.Reason}";
                    EngineLog.WriteLine($"Network: {_status}.");
                    break;
            }
        }
        if (connection.Completion.IsCompleted && connection.Events.Completion.IsCompleted &&
            !_status.StartsWith("closed:", StringComparison.Ordinal))
            _status = "closed: transport ended";
    }

    private void Receive(GoldGameEvent gameEvent)
    {
        var opcode = gameEvent.Message.Opcode;
        _opcodeCounts.TryGetValue(opcode, out var count);
        _opcodeCounts[opcode] = count + 1;
        _eventsReceived++;
        // Bounded diagnostics until native object admission/removal and reference mappings are implemented.
        switch (gameEvent)
        {
            case GoldAdmissionEvent admission:
                _admission = $"Sacred slot {admission.PlayerSlot}, flags 0x{admission.Flags:X2}; initial character/world import pending";
                EngineLog.WriteLine($"Network admission: {_admission}.");
                break;
            case GoldRejectionEvent rejection:
                _admission = $"rejected: reason {rejection.Reason}, data mismatch mask 0x{rejection.DataMismatchMask:X8}";
                EngineLog.WriteLine($"Network admission: {_admission}.");
                _connection?.Stop();
                break;
            case GoldPlayerDataEvent playerData:
                EngineLog.WriteLine($"Network player information: character type {playerData.PlayerData.CharacterType}, version {playerData.PlayerData.Version}; profile import pending.");
                break;
            case GoldPlayerRosterEvent roster:
                EngineLog.WriteLine($"Network server roster: {roster.Roster.Count} retained records; server metadata at index 0, player slots 1..16; actor/world binding pending.");
                break;
            case GoldTimeProbeEvent:
                EngineLog.WriteLine($"Network time probe: {(_connection?.Admission.Phase == GoldAdmissionPhase.SlotAssigned ? "native reply sent" : "retained in transport diagnostics")}.");
                break;
            case GoldReferenceBatchEvent references:
                EngineLog.WriteLine($"Network allocated object references: {references.Batch.Count}; hero/player references are separate; world import pending.");
                break;
            case GoldReferenceRangeEvent range:
                EngineLog.WriteLine($"Network object reference range: {range.Start} + {range.Count}; retained native bounds, world import pending.");
                break;
            case GoldPasswordResultEvent password:
                EngineLog.WriteLine($"Network game password: {(password.Accepted ? "accepted" : "rejected")}; character/world entry pending.");
                break;
            case GoldMovementEvent movement when _movements.ContainsKey(movement.ObjectReference) || _movements.Count < 4096:
                _movements[movement.ObjectReference] = [movement.Word1, movement.Word2, movement.Word3, movement.Word4, movement.Word5];
                break;
            case GoldMovementEndEvent end:
                _movements.Remove(end.ObjectReference);
                break;
            case GoldIdleEvent idle:
                _movements.Remove(idle.ObjectReference);
                break;
            case GoldDoorEvent door when _doors.ContainsKey(door.ObjectReference) || _doors.Count < 4096:
                _doors[door.ObjectReference] = door.Open;
                break;
            case GoldObjectEvent obj when _objects.ContainsKey(obj.ObjectReference) || _objects.Count < 4096:
                _objects[obj.ObjectReference] = obj.BaseState;
                break;
        }
        if (count == 0)
            EngineLog.WriteLine($"Network dispatch: 0x{opcode:X4}, {gameEvent.GetType().Name}, {gameEvent.Message.Payload.Length} bytes; retained for admission/reference mapping.");
    }

    private void Disconnect()
    {
        if (_connection is { } connection) connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _connection = null;
        _movements.Clear(); _objects.Clear(); _doors.Clear(); _opcodeCounts.Clear();
        _eventsReceived = 0;
        _status = "offline";
        _admission = "transport diagnostics; native player profile and world entry pending";
    }

    public void Dispose() => Disconnect();
}
