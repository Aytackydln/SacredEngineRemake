using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Gold 0x7E1257..0x7E18D6 outer admission continuation. Decisions read
/// current fields after synchronous service return, rather than an eager plan.
/// The receipt owner supplies a lifetime check around every service boundary.</summary>
internal static class WorldAdmissionLive
{
    internal static WorldAdmissionLiveResult Execute(GoldAdmissionEvent admission,
        WorldAdmissionLiveServices services, Action validateCurrent)
    {
        var control = services.Controller;
        control.WriteByte(3, 0);
        Call(services.ResetResource);
        if (Query() && control.ReadByte(9) != 0 && (admission.Flags & 1) == 0)
        {
            Reject();
            // Unlike late rejection, this branch clears identities AFTER the
            // local event returns, using the then-current connection instance.
            control.WriteWord(0x14, 0);
            Connection().WriteWord(0x48, 0);
            return Result(true, false);
        }

        Call(() => services.AcceptedRoster(admission));
        if (Query())
        {
            if (control.ReadByte(9) != 0 && (admission.Flags & 1) == 0)
            {
                Reject();
                return Result(false, true);
            }
            Ui(new(0x36, "UI_WND_NETWORK", 1, 4));
            if (control.ReadByte(0x381b) == 0)
                Ui(new(0x39, "DEFAULT_BUSYDLG", 2, 2, "UI_BUSY_WAIT4SERVER", Parameter: 12));
            Call(services.FinalizeConnection);
        }
        else Ui(new(0x36, "UI_WND_NETWORK", 1, 3));

        if (control.ReadByte(1) != 0)
            Ui(new(0x3c, "UI_NET_BTN_JOIN", 1, 0, "UI_NET_STARTGAME"));
        if ((admission.Flags & 2) != 0)
        {
            Chat("UI_NET_HOST_GAMESTART");
            Chat("UI_PLEASE_WAIT");
        }
        else if ((admission.Flags & 4) != 0)
        {
            control.WriteByte(2, 1);
            Chat("UI_NET_HOST_GAMESTARTED");
        }
        return Result(false, false);

        void Call(Action service)
        {
            validateCurrent();
            service();
            validateCurrent();
        }
        bool Query()
        {
            validateCurrent();
            var value = services.QueryConnection();
            validateCurrent();
            return value;
        }
        void Ui(GoldLobbyUiRequest request) => Call(() => services.Ui(request));
        void Chat(string key) => Call(() => services.ChatResource(key));
        void Reject()
        {
            Ui(new(0x39, "DEFAULT_BUSYDLG", 2, 4));
            Call(services.DispatchController117);
        }
        WorldControlMemory Connection() => services.ReadConnection() ??
            throw new NotSupportedException("Native admission requires current connection storage.");
        WorldAdmissionLiveResult Result(bool prefix, bool late) => new(prefix, late,
            control.ReadByte(3), control.ReadByte(1), new(control.ReadWord(0x14),
                control.ReadWord(0x24), services.ReadConnection()?.ReadWord(0x48)));
    }
}
