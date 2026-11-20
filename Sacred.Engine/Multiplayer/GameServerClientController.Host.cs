using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Core.World;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Engine.Multiplayer;

internal sealed partial class GameServerClientController
{
    private List<GoldMessage>? _hostAdmissionHistory;
    private readonly ConcurrentQueue<IReadOnlyList<GoldMessage>> _hostBatches = new();
    private WorldHostInitializationSession? _hostSession;
    private Task? _hostJoin;
    private bool _hostReplicaSeeded;
    private bool _hostReferencesConsumed;

    public bool IsAssignedFirstHost => _connection?.Admission.WorldStartup.AssignedClientHost == true;
    public bool IsMinimalHostApproved =>
        _hostReplicaSeeded && _hostReferencesConsumed && _hostSession?.IsCurrent == true && _connection?.HostUploadApproved == true;
    public bool IsWorldApproved => IsParticipantWorldApproved || IsMinimalHostApproved;

    public void TrackHostAdmission() => _hostAdmissionHistory = [];
    public void EndHostAdmissionTracking() => _hostAdmissionHistory = null;

    public Task BeginFirstHostJoin(WorldHostSavedCharacterSource source)
    {
        var connection = _connection ?? throw new InvalidOperationException("First-host transport is missing.");
        var contact = _firstContact ?? throw new InvalidOperationException("First-host admission profile is missing.");
        var history = _hostAdmissionHistory ?? throw new InvalidOperationException("First-host admission history is missing.");
        _hostSession = WorldHostInitializationSession.AdoptFirstHost(source, connection, contact, history, _hostBatches.Enqueue, EngineLog.WriteLine);
        EndHostAdmissionTracking();
        LocalMovementControl = source.Records.Hero.Creature.MovementControl;
        _startupStatistics = GoldPlayerStatisticsState.FromSavedCharacter(source.Records);
        return _hostJoin = _hostSession.JoinMinimalAsync();
    }

    private void UpdateFirstHost()
    {
        if (_hostSession is not { } host) return;
        if (!host.IsCurrent)
        {
            if (WorldReplicas.LocalPlayerSlot != 0) _gameplay.Clear();
            _hostReplicaSeeded = false;
            return;
        }
        if (_hostJoin?.IsCompletedSuccessfully != true || host.MinimalCharacterReferences is not { } plan) return;
        if (!_hostReplicaSeeded)
        {
            if (host.MinimalCharacterPublication is not { } publication) return;
            WorldReplicas.BeginSectorInterest(host.MinimalInitialInterest!);
            _gameplay.SeedCharacter(publication);
            if (!host.TrySeedApprovedMinimalBanks(WorldReplicas)) return;
            _hostReplicaSeeded = true;
        }
        for (var i = 0; i < 16 && _hostBatches.TryDequeue(out var batch); i++)
            GoldGameDispatcher.Dispatch(batch, Receive);
        if (!_hostReferencesConsumed)
        {
            var references = plan.References.Values.Where(reference => reference != plan.PlayerSlot).ToArray();
            ObjectReferences.Consume(references);
            _hostReferencesConsumed = true;
        }
        _sectorInterest?.Update();
    }

    public void TrackFirstHostSectorInterest(SacredSectorIndex index)
    {
        if (!IsMinimalHostApproved || !WorldReplicas.SectorInterest.IsConfirmed)
            throw new InvalidOperationException("Host sector tracking requires an approved host and confirmed terrain.");
        _sectorInterest = new GameServerSectorInterest(_connection!, WorldReplicas, index, () => checked((uint)ObjectReferences.Available))
        {
            SendHostRequest = _hostSession!.SendMinimalSectorInterestAsync
        };
        EngineLog.WriteLine($"Network host interest: {_sectorInterest.Status}.");
    }

    private async Task SendPeerReadyForSessionAsync(GoldGameConnection connection, byte slot, uint flags)
    {
        var statistics = _startupStatistics ?? throw new InvalidOperationException("Peer readiness requires the selected character's startup statistics.");
        await connection.SendAsync(GoldPlayerStatisticsCodec.Create(slot, statistics)).ConfigureAwait(false);
        EngineLog.WriteLine($"Network: slot {slot} sent saved startup statistics before peer readiness.");
        if (GoldSessionGameFlagsCodec.IsCampaignGame(flags))
        {
            await connection.SendAsync(GoldTeamCodec.CreateCampaignJoin(slot)).ConfigureAwait(false);
            EngineLog.WriteLine($"Network: slot {slot} sent campaign team join before peer readiness.");
        }
        await connection.SendAsync(GoldWorldEntryCodec.CreatePeerReady(slot)).ConfigureAwait(false);
    }

    private void DisposeFirstHost()
    {
        if (_hostSession is { } host)
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (_hostJoin is { } joining)
            {
                try { joining.GetAwaiter().GetResult(); }
                catch (Exception error) { EngineLog.WriteLine($"First-host join ended: {error.Message}"); }
            }
            _connection = null;
        }
        _hostSession = null;
        _hostJoin = null;
        _hostReplicaSeeded = false;
        _hostReferencesConsumed = false;
        EndHostAdmissionTracking();
        _hostBatches.Clear();
    }
}
