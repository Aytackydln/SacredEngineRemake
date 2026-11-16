using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compared 0x11 dispatcher branch through common exit, composed with
/// roster construction/application. This is an eager projection from supplied
/// snapshots; it does not execute services or preserve their reentry timing.
/// Use receipt-owned live delivery for decisions after service return.</summary>
public static class WorldAdmissionPlan
{
    public static WorldAdmissionPlanResult Prepare(WorldAdmissionControllerState controller,
        GoldPlayerRoster previous, GoldAdmissionEvent admission, WorldAdmissionContinuationInputs inputs,
        Func<string, string> identifier)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(identifier);
        var multiplayer = inputs.Multiplayer ?? throw new NotSupportedException("Admission plan requires an established multiplayer context.");
        var decoded = GoldGameEventDecoder.Decode(admission.Message) as GoldAdmissionEvent
            ?? throw new InvalidDataException("Admission plan requires the original 0x11 response.");
        var prefix = WorldAdmissionControllerPrefix.Apply(controller, decoded, inputs.PrefixConnectionQuery, inputs.ControllerFlag9);
        var ui = new List<GoldLobbyUiRequest>();
        var chat = new List<string>();
        var flag2 = inputs.PreviousFlag2;
        if (prefix.Rejected)
        {
            ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, 4));
            return Result(null, null, false, 1, false);
        }
        var staged = WorldAdmissionRosterBuilder.Build(prefix, decoded, previous.GetEntry(0), inputs.RosterInputs);
        // Native construction has already replaced the controller table before
        // invoking its consumer, so both prior/current views are this table.
        var applied = WorldRosterApplication.Apply(staged.Roster, staged.Roster, decoded.PlayerSlot,
            inputs.ControllerFlag28, multiplayer, identifier);
        if (inputs.LaterConnectionQuery && inputs.ControllerFlag9 && (decoded.Flags & 1) == 0)
        {
            ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, 4));
            return Result(staged, applied, true, 1, false);
        }
        var finalize = inputs.LaterConnectionQuery;
        if (finalize)
        {
            ui.Add(new(0x36, "UI_WND_NETWORK", 1, 4));
            if (!inputs.ControllerFlag381B)
                ui.Add(new(0x39, "DEFAULT_BUSYDLG", 2, 2, "UI_BUSY_WAIT4SERVER", Parameter: 12));
        }
        else ui.Add(new(0x36, "UI_WND_NETWORK", 1, 3));
        if (prefix.State.HostByte1 != 0) ui.Add(new(0x3c, "UI_NET_BTN_JOIN", 1, 0, "UI_NET_STARTGAME"));
        if ((decoded.Flags & 2) != 0)
        {
            chat.Add("UI_NET_HOST_GAMESTART");
            chat.Add("UI_PLEASE_WAIT");
        }
        else if ((decoded.Flags & 4) != 0)
        {
            flag2 = 1;
            chat.Add("UI_NET_HOST_GAMESTARTED");
        }
        return Result(staged, applied, false, 0, finalize);

        WorldAdmissionPlanResult Result(WorldAdmissionRosterStage? roster, WorldRosterApplicationStage? application,
            bool late, int native117, bool finalizer) =>
            new(prefix, roster, application, flag2, late, new(ui.AsReadOnly(), chat.AsReadOnly(), native117, finalizer));
    }
}
