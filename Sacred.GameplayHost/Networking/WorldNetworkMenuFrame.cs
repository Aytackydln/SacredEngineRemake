namespace Sacred.GameplayHost.Networking;

/// <summary>Complete native 0x70B500 frame/update caller around mode conversion.
/// Established storage and actual presentation/network services remain external.</summary>
public static class WorldNetworkMenuFrame
{
    public static void Update(WorldNetworkMenuFrameServices services, uint argument)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.BaseUpdate(argument);
        var menu = services.Mode.Menu;
        var selection = menu.ReadWord(0x158);
        if (selection is >= 1 and <= 7)
        {
            var offset = selection switch { 1 => 0x15cu, 2 => 0x160u, 3 => 0x164u, 4 => 0x168u, 5 => 0x170u, 6 => 0x174u, _ => 0x16cu };
            services.UpdateChild(menu.ReadWord(offset), argument, selection == 5);
        }
        if (!WorldNetworkMenuMode.ConvertAfterChildUpdate(services.Mode)) return;
        services.Ui(new(0x12, "UI_MAINMENU", 5, 0) { NativeWord0C = 0 });
        services.Mode.SetBits(services.MenuAddress, 5, true);
    }
}
