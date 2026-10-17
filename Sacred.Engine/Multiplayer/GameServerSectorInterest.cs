using System;
using System.Threading.Tasks;
using Sacred.Core.World;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Serializes authored interest changes and follows the owned hero's reported sector.</summary>
internal sealed class GameServerSectorInterest
{
    private readonly GoldGameConnection _connection;
    private readonly GoldWorldReplica _replicas;
    private readonly SacredSectorIndex _index;
    private readonly Func<uint> _freeReferences;
    private readonly long _sessionVersion;
    private Task? _send;
    private bool _waiting;
    private ushort _desired;
    private ushort _observedHero;
    public string Status { get; private set; } = "not requested";

    public GameServerSectorInterest(GoldGameConnection connection, GoldWorldReplica replicas,
        SacredSectorIndex index, Func<uint> freeReferences)
    {
        (_connection, _replicas, _index, _freeReferences) = (connection, replicas, index, freeReferences);
        _sessionVersion = replicas.SessionVersion;
        _observedHero = replicas.Objects[replicas.LocalPlayerSlot].BaseState.Sector;
    }

    public string Request(ushort sector)
    {
        if (!_index.TryGetSector(sector, out _) || sector == 0) throw new ArgumentException("Requested sector is absent from authored KEYX data.", nameof(sector));
        if (_replicas.SessionVersion != _sessionVersion) throw new InvalidOperationException("Sector-interest session ended.");
        _desired = sector;
        Advance();
        return Status;
    }

    public void Update()
    {
        if (_replicas.SessionVersion != _sessionVersion || _connection.Completion.IsCompleted) return;
        var heroSector = _replicas.Objects[_replicas.LocalPlayerSlot].BaseState.Sector;
        if (heroSector != _observedHero) { _observedHero = heroSector; _desired = heroSector; }
        Advance();
    }

    private void Advance()
    {
        if (_send is { IsCompleted: false }) return;
        if (_send is not null)
        {
            try { _send.GetAwaiter().GetResult(); }
            catch (Exception error)
            {
                Status = "failed: " + error.Message;
                EngineLog.WriteLine($"Network sector interest {Status}.");
                _connection.Stop();
                _send = null;
                return;
            }
            _send = null;
            Status = $"waiting for sector {_replicas.SectorInterest.Center} confirmations";
        }
        if (_waiting)
        {
            if (!_replicas.SectorInterest.IsConfirmed) return;
            _waiting = false;
            Status = $"sector {_replicas.SectorInterest.Center} confirmed: {_replicas.Sectors.Count} active sectors";
            EngineLog.WriteLine($"Network interest: {Status}.");
        }
        if (_desired == 0 || _desired == _replicas.SectorInterest.Center) return;
        var request = GoldSectorInterestPlanner.Create(_index, _desired, _replicas.SectorInterest.Center, _freeReferences());
        _send = _connection.SendAsync(request.CreateMessage(_replicas.LocalPlayerSlot)).AsTask();
        _replicas.BeginSectorInterest(request);
        _waiting = true;
        Status = $"sector interest {_desired}, previous {request.PreviousSector}; sending";
        EngineLog.WriteLine($"Network interest: {Status}.");
    }

    public void ObserveCompletion() => _ = _send?.ContinueWith(task => { _ = task.Exception; },
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
}
