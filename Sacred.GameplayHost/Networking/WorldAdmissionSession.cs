using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Networking;

/// <summary>Main-thread admission work owned by the existing gameplay replica session.
/// Uses its delivery-journal reset generation, which character import does not change.
/// No controller inputs, service delivery or actor registry are fabricated here.</summary>
public sealed class WorldAdmissionSession
{
    private readonly GoldWorldReplica _replicas;
    private WorldAdmissionReceipt? _current;
    private bool _preparing;
    private SacredTextMarkup? _identifier;
    private WorldAdmissionWork? _deliveringService;
    private (WorldAdmissionReceipt Receipt, WorldControlMemory Controller)? _acceptedRoster;

    internal WorldAdmissionSession(GoldWorldReplica replicas) => _replicas = replicas;
    public WorldAdmissionReceipt? Current => _current is { IsCurrent: true } ? _current : null;

    /// <summary>Capture established current fields for this receipt, rather
    /// than reusing constructor defaults or another admission's controller state.</summary>
    public void CaptureController(WorldAdmissionReceipt receipt, WorldAdmissionControllerSnapshot snapshot)
    {
        ValidateCurrent(receipt);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_preparing || receipt.Plan is not null || receipt.LiveStarted)
            throw new InvalidOperationException("Capture controller state before preparing admission.");
        receipt.ControllerSnapshot = snapshot;
    }

    public WorldAdmissionPlanResult Prepare(WorldAdmissionReceipt receipt, bool prefixQuery, bool laterQuery,
        WorldAdmissionRosterInputs rosterInputs)
    {
        ValidateCurrent(receipt);
        var snapshot = receipt.ControllerSnapshot ?? throw new NotSupportedException("Admission requires a current controller snapshot.");
        return Prepare(receipt, snapshot.State, snapshot.Roster, snapshot.CreateInputs(prefixQuery, laterQuery, rosterInputs));
    }

    /// <summary>Capture current established storage for this receipt. No lazy
    /// controller construction or setup defaults are performed by this adapter.</summary>
    public void CaptureCurrentController(WorldAdmissionReceipt receipt, WorldControlMemory controller,
        Func<WorldControlMemory?> readConnection, Func<byte?> readMultiplayer)
    {
        ValidateCurrent(receipt);
        var snapshot = WorldAdmissionControllerCapture.Capture(controller, readConnection, readMultiplayer);
        CaptureController(receipt, snapshot);
    }

    /// <summary>Configure executable-derived rules before admission. Like other
    /// installed definitions, these survive session reset; pending work cannot change them.</summary>
    public void ConfigureIdentifier(SacredTextMarkup identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        if (_preparing || Current is not null)
            throw new InvalidOperationException("Configure identifier rules before receiving admission.");
        _identifier = identifier;
    }

    public WorldAdmissionPlanResult Prepare(WorldAdmissionReceipt receipt, WorldAdmissionControllerState controller,
        GoldPlayerRoster previous, WorldAdmissionContinuationInputs inputs)
    {
        ValidateCurrent(receipt);
        var identifier = _identifier ?? throw new NotSupportedException("Admission requires original executable identifier rules.");
        return Prepare(receipt, controller, previous, inputs, identifier.Strip);
    }

    internal static GoldGameEvent Validate(GoldGameEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry is not GoldAdmissionEvent && entry.Message.Opcode != 0x11) return entry;
        // Typed fields and mutable caller payloads must not become lifetime authority.
        var message = entry.Message with { Payload = entry.Message.Payload.ToArray() };
        if (message.Sender != 0 || message.Origin != 0)
            throw new InvalidDataException("Admission requires the server sender and origin.");
        return GoldGameEventDecoder.Decode(message) as GoldAdmissionEvent
            ?? throw new InvalidDataException("Admission requires a valid original 0x11 response.");
    }

    internal void Observe(GoldGameEvent entry)
    {
        if (entry is GoldAdmissionEvent admission)
            _current = new(admission, _replicas.Consumers.Session, IsCurrent);
        else if (entry is GoldSessionNoticeEvent { EndsSession: true } or
            GoldPlayerKickEvent { DisconnectsClient: true } or GoldRejectionEvent)
            Clear();
    }

    internal void Clear() => _current = null;
    private bool IsCurrent(WorldAdmissionReceipt receipt) => ReferenceEquals(_current, receipt) &&
        receipt.Generation == _replicas.Consumers.Session;
    private void ValidateCurrent(WorldAdmissionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!IsCurrent(receipt)) throw new InvalidOperationException("Admission work belongs to a stale or different session.");
    }

    public WorldAdmissionPlanResult Prepare(WorldAdmissionReceipt receipt, WorldAdmissionControllerState controller,
        GoldPlayerRoster previous, WorldAdmissionContinuationInputs inputs, Func<string, string> identifier)
    {
        ValidateCurrent(receipt);
        if (_preparing || receipt.Plan is not null || receipt.LiveStarted)
            throw new InvalidOperationException("Admission preparation is already running or complete.");
        _preparing = true;
        try
        {
            ArgumentNullException.ThrowIfNull(identifier);
            var plan = WorldAdmissionPlan.Prepare(controller, previous, receipt.Admission, inputs, ConvertIdentifier);
            // Identifier conversion is a supplied boundary and may reenter the session.
            ValidateCurrent(receipt);
            var work = WorldAdmissionEffects.Create(plan)
                .Select((effect, index) => new WorldAdmissionWork(receipt, index, effect)).ToArray();
            receipt.Work = Array.AsReadOnly(work);
            receipt.Plan = plan;
            return plan;

            string ConvertIdentifier(string name)
            {
                ValidateCurrent(receipt);
                var result = identifier(name);
                ValidateCurrent(receipt);
                return result;
            }
        }
        finally { _preparing = false; }
    }

    public bool TryPeek(WorldAdmissionReceipt receipt, out WorldAdmissionWork work)
    {
        ValidateCurrent(receipt);
        if (receipt.Plan is null || receipt.DeliveryRejected || receipt.CompletedEffects == receipt.EffectCount)
        { work = null!; return false; }
        work = receipt.Work[receipt.CompletedEffects];
        return true;
    }

    /// <summary>Execute against current storage and synchronous services. Live
    /// delivery and eager preparation are mutually exclusive for this receipt.
    /// Failure may leave native writes/services applied and cannot be retried.</summary>
    public WorldAdmissionLiveResult DeliverLive(WorldAdmissionReceipt receipt, WorldAdmissionLiveServices services)
    {
        ValidateCurrent(receipt);
        ArgumentNullException.ThrowIfNull(services);
        if (_preparing || receipt.Plan is not null || receipt.LiveStarted)
            throw new InvalidOperationException("Admission delivery is already prepared, running or attempted.");
        receipt.LiveStarted = true;
        receipt.LiveInProgress = true;
        try
        {
            var result = WorldAdmissionLive.Execute(receipt.Admission, services with { AcceptedRoster = DeliverAccepted }, ValidateLive);
            ValidateCurrent(receipt);
            receipt.LiveResult = result;
            return result;
        }
        catch
        {
            if (IsCurrent(receipt)) receipt.DeliveryRejected = true;
            throw;
        }
        finally { receipt.LiveInProgress = false; }

        void ValidateLive()
        {
            ValidateCurrent(receipt);
            if (receipt.DeliveryRejected) throw new InvalidOperationException("A nested admission service failed; delivery may be partial.");
        }

        void DeliverAccepted(GoldAdmissionEvent admission)
        {
            var previous = _acceptedRoster;
            try
            {
                _acceptedRoster = (receipt, services.Controller);
                services.AcceptedRoster(admission);
            }
            finally { _acceptedRoster = previous; }
        }
    }

    /// <summary>Execute the complete roster consumer inside this receipt's
    /// accepted service. Preparation, stale work and nested duplicate consumers
    /// cannot use this entry. Prefix/staging is still the caller's preceding work.</summary>
    public void DeliverLiveRoster(WorldAdmissionReceipt receipt, GoldPlayerRoster incoming, WorldRosterLiveServices services)
    {
        ValidateCurrent(receipt);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(services);
        if (_acceptedRoster is not { } accepted || !ReferenceEquals(accepted.Receipt, receipt) ||
            !ReferenceEquals(accepted.Controller, services.Controller) || receipt.LiveRosterStarted || receipt.DeliveryRejected)
            throw new InvalidOperationException("Live roster requires the current accepted service and captured controller.");
        receipt.LiveRosterStarted = true;
        try
        {
            WorldRosterLive.Apply(services, incoming, () => ValidateCurrent(receipt));
            ValidateCurrent(receipt);
            receipt.LiveRosterComplete = true;
        }
        catch
        {
            if (IsCurrent(receipt)) receipt.DeliveryRejected = true;
            throw;
        }
    }

    /// <summary>Stage accepted fields, then synchronously consume the table on
    /// this receipt. Actual service/setup inputs remain caller owned.</summary>
    public void DeliverLiveStaging(WorldAdmissionReceipt receipt, WorldAdmissionStagingServices services)
    {
        ValidateCurrent(receipt);
        ArgumentNullException.ThrowIfNull(services);
        if (_acceptedRoster is not { } accepted || !ReferenceEquals(accepted.Receipt, receipt) ||
            !ReferenceEquals(accepted.Controller, services.Roster.Controller) || receipt.LiveStagingStarted || receipt.LiveRosterStarted || receipt.DeliveryRejected)
            throw new InvalidOperationException("Live staging requires the current unused accepted-service scope.");
        receipt.LiveStagingStarted = true;
        try
        {
            var table = WorldAdmissionStagingLive.Apply(receipt.Admission, services, () => ValidateCurrent(receipt));
            DeliverLiveRoster(receipt, table, services.Roster);
            ValidateCurrent(receipt);
            receipt.LiveStagingComplete = true;
        }
        catch
        {
            if (IsCurrent(receipt)) receipt.DeliveryRejected = true;
            throw;
        }
    }

    /// <summary>Complete the native immediate UI service before acknowledging
    /// this receipt. Scene/render handling is the registered listener's concern.
    /// Exceptions cannot make partially delivered work eligible for silent retry.</summary>
    public void DeliverUi(WorldAdmissionWork work, WorldAdmissionUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (!TryPeek(work.Receipt, out var head) || !ReferenceEquals(work, head) || work.Effect is not WorldAdmissionEffect.Ui ui)
            throw new InvalidOperationException("Immediate UI delivery requires the current pending UI effect.");
        if (ReferenceEquals(_deliveringService, work)) throw new InvalidOperationException("This UI effect is already being delivered.");
        var previous = _deliveringService;
        try { _deliveringService = work; dispatcher.Dispatch(ui.Request); }
        catch
        {
            _deliveringService = previous;
            if (IsCurrent(work.Receipt) && TryPeek(work.Receipt, out var pending) && ReferenceEquals(pending, work))
                Report(work, GoldConsumerOutcome.Rejected, "Immediate UI listener failed; delivery may be partial.");
            throw;
        }
        finally { _deliveringService = previous; }
        Report(work, GoldConsumerOutcome.Consumed, "Native immediate UI service returned.");
    }

    /// <summary>Report after a service returns, so replacement/reset during reentry
    /// cannot acknowledge old work. Deferred retains the same head. A roster consumer
    /// must track partial progress itself; Consumed means its entire service completed.</summary>
    public void Report(WorldAdmissionWork work, GoldConsumerOutcome outcome, string detail)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidateCurrent(work.Receipt);
        if (ReferenceEquals(_deliveringService, work)) throw new InvalidOperationException("An effect cannot be acknowledged before its service returns.");
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        ArgumentNullException.ThrowIfNull(detail);
        var receipt = work.Receipt;
        if (!TryPeek(receipt, out var head) || !ReferenceEquals(head, work))
            throw new InvalidOperationException("Admission completion must match the current pending effect.");
        receipt.LastReport = new(work, outcome, detail);
        if (outcome == GoldConsumerOutcome.Consumed) receipt.CompletedEffects++;
        else if (outcome == GoldConsumerOutcome.Rejected) receipt.DeliveryRejected = true;
    }

    public void DeliverResetService(WorldAdmissionWork work, WorldAdmissionResetService service)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(service);
        if (!TryPeek(work.Receipt, out var head) || !ReferenceEquals(work, head) || work.Effect is not WorldAdmissionEffect.ResetService)
            throw new InvalidOperationException("Resource reset requires the current pending reset effect.");
        if (ReferenceEquals(_deliveringService, work)) throw new InvalidOperationException("This reset effect is already being delivered.");
        var previous = _deliveringService;
        try { _deliveringService = work; service.ResetEntry(); }
        catch
        {
            _deliveringService = previous;
            if (IsCurrent(work.Receipt) && TryPeek(work.Receipt, out var pending) && ReferenceEquals(pending, work))
                Report(work, GoldConsumerOutcome.Rejected, "Resource reset failed; construction may be partial.");
            throw;
        }
        finally { _deliveringService = previous; }
        Report(work, GoldConsumerOutcome.Consumed, "Native resource entry reset completed.");
    }

    public void DeliverController117(WorldAdmissionWork work, WorldController117Services services)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(services);
        if (!TryPeek(work.Receipt, out var head) || !ReferenceEquals(work, head) || work.Effect is not WorldAdmissionEffect.DispatchController117)
            throw new InvalidOperationException("Local controller dispatch requires the current pending 0x117 effect.");
        if (ReferenceEquals(_deliveringService, work)) throw new InvalidOperationException("This local event is already being dispatched.");
        var previous = _deliveringService;
        try
        {
            _deliveringService = work;
            WorldController117.Dispatch(services);
            ValidateCurrent(work.Receipt);
            var snapshot = WorldController117.Capture(services);
            ValidateCurrent(work.Receipt);
            work.Receipt.ControllerAfter117 = snapshot;
        }
        catch
        {
            _deliveringService = previous;
            if (IsCurrent(work.Receipt) && TryPeek(work.Receipt, out var pending) && ReferenceEquals(pending, work))
                Report(work, GoldConsumerOutcome.Rejected, "Local controller dispatch failed; service effects may be partial.");
            throw;
        }
        finally { _deliveringService = previous; }
        Report(work, GoldConsumerOutcome.Consumed, "Local controller 0x117 returned.");
    }
}
