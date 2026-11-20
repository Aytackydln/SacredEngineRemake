using Sacred.Assets.Executable;
using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

public sealed partial class WorldHostInitializationSession
{
    private GoldHostUploadSession? _minimalUpload;
    private GoldWorldBankSnapshot[]? _minimalBanks;
    public GoldSectorInterestRequest? MinimalInitialInterest { get; private set; }
    public GoldParticipantCharacterUpload? MinimalCharacterPublication { get; private set; }
    public GoldSavedCharacterReferencePlan? MinimalCharacterReferences { get; private set; }

    public async Task SendMinimalSectorInterestAsync(GoldSectorInterestRequest request)
    {
        await _gate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            if (Phase != WorldHostInitializationPhase.MinimalUploadApproved || !_connection.HostUploadApproved ||
                !Replicas.SectorInterest.IsConfirmed)
                throw new InvalidOperationException("Host sector interest requires an approved upload and a confirmed previous neighborhood.");
            Replicas.BeginSectorInterest(request);
            await _connection.SendAsync(request.CreateMessage(Admission.PlayerSlot), _stop.Token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Upload the entered character and initial world storage without executing campaign callbacks.</summary>
    public async Task JoinMinimalAsync(CancellationToken cancellationToken = default)
    {
        await WaitForReservationsAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        GoldHostUploadSession upload;
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            if (_minimalUpload is null)
            {
                if (Character is not null || _characterPublicationStarted || _startupAttempted || Replicas.SectorInterest.HasRequest)
                    throw new InvalidOperationException("Minimal host upload needs a connection before separate character or world publication.");

                var source = SelectedCharacter ?? throw new InvalidOperationException("Minimal hosting requires a selected saved character.");
                var installation = source.Installation;
                var sectors = SacredSectorIndex.Read(Path.Combine(installation, "World", "sectors.keyx"));
                var slot = Admission.PlayerSlot;
                var allocated = References.PeekAvailable(source.ObjectCount);
                var plan = GoldSavedCharacterReferencePlan.Create(source.Records, slot, allocated);
                var character = GoldSavedCharacterPacketPreparation.Create(GoldSavedCharacterImportPreparation.Create(plan));
                var hero = (GoldObjectEvent)GoldGameEventDecoder.Decode(character.CreateMessages().Single(message => message.Opcode == 0x169));
                var interest = GoldSectorInterestPlanner.Create(sectors, hero.BaseState.Sector, 0, checked((uint)(References.Available - allocated.Length)));
                var banks = CreateMinimalBanks(installation);
                var prepared = GoldHostWorldUpload.Create(character,
                    [interest.CreateMessage(slot)], banks, [], sectors, allocated);
                await _connection.SendAsync(GoldWorldEnvironmentCodec.CreateRequest(slot), linked.Token).ConfigureAwait(false);
                _log("Host startup: requested current GameServer environment before world upload.");
                References.Consume(allocated);
                _minimalUpload = GoldHostUploadSession.Start(_connection, prepared, _stop.Token);
                _minimalBanks = banks;
                Replicas.BeginSectorInterest(interest);
                MinimalInitialInterest = interest;
                MinimalCharacterPublication = character;
                MinimalCharacterReferences = plan;
            }
            upload = _minimalUpload;
        }
        finally { _gate.Release(); }

        await upload.WaitForApprovalAsync(linked.Token).ConfigureAwait(false);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            RequireCurrent();
            if (Phase == WorldHostInitializationPhase.MinimalUploadApproved) return;
            Replicas.SeedCharacter(MinimalCharacterPublication!);
            if (!TrySeedApprovedMinimalBanks(Replicas))
                throw new IOException("Host approval ended before the local world banks could be retained.");
            _characterPublicationStarted = _characterPublicationSent = true;
            SetPhase(WorldHostInitializationPhase.MinimalUploadApproved);
            _log($"Minimal host upload approved for slot {Admission.PlayerSlot}. Local actor {Replicas.LocalPlayerSlot} registered from the validated saved-character publication.");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Use the acknowledged upload as the host's starting world bank state.
    /// Any server snapshot already received takes precedence.</summary>
    public bool TrySeedApprovedMinimalBanks(GoldWorldReplica replicas)
    {
        ArgumentNullException.ThrowIfNull(replicas);
        var slot = Admission.PlayerSlot;
        if (!_connection.HostUploadApproved || _minimalBanks is not { } banks || replicas.LocalPlayerSlot != slot)
            return false;
        foreach (var bank in banks)
        {
            if (replicas.WorldBanks.Current.ContainsKey(bank.Opcode)) continue;
            var message = GoldWorldBankCodec.Create(slot, slot, bank);
            replicas.Apply(new GoldWorldBankEvent(message, bank));
        }
        return true;
    }

    private static GoldWorldBankSnapshot[] CreateMinimalBanks(string installation)
    {
        var reset = SacredScriptInitializationDefinitions.Load(installation);
        var bank1 = new byte[GoldWorldBankLayout.HeaderSize + GoldWorldBankGlobals.Size];
        reset.CreateParameterBlock().CopyTo(bank1, GoldWorldBankLayout.HeaderSize);
        reset.CreateRegionReplacements().CopyTo(bank1, GoldWorldBankLayout.HeaderSize + GoldWorldBankGlobals.FirstBlockSize);
        GoldWorldRegionTable.CreateInitial().ToPayload().CopyTo(bank1,
            GoldWorldBankLayout.HeaderSize + GoldWorldBankGlobals.FirstBlockSize + GoldWorldBankGlobals.SecondBlockSize);
        return
        [
            Empty(GoldWorldBankLayout.Bank2Opcode),
            Empty(GoldWorldBankLayout.Bank4Opcode),
            Empty(GoldWorldBankLayout.Bank3Opcode),
            GoldWorldBankSnapshot.FromPayload(GoldWorldBankLayout.Bank1Opcode, bank1)
        ];

        static GoldWorldBankSnapshot Empty(ushort opcode) =>
            GoldWorldBankSnapshot.FromPayload(opcode, new byte[GoldWorldBankLayout.HeaderSize]);
    }
}
