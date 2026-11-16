namespace Sacred.GameplayHost.Networking;

/// <summary>Original 0x70B58F conversion, complete 0x70B310 selection and
/// 0x70C410/440/470 page configuration. These consume current menu/multiplayer
/// inputs; they do not establish a network session, admission or actor readiness.</summary>
public static class WorldNetworkMenuMode
{
    public static bool ConvertAfterChildUpdate(WorldNetworkMenuServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var menu = services.Menu;
        var source = menu.ReadWord(0x154);
        if (source is not (3 or 4 or 6) || services.ReadMultiplayer() == 0) return false;
        menu.WriteWord(0x154, source == 3 ? 1u : source == 4 ? 2u : 5u);
        services.ResolveController().WriteByte(0x381b, 0);
        // Resolver construction is synchronous; read current mode afterward.
        var current = menu.ReadWord(0x154);
        if (current == 1) Select(services, 5);
        else if (current == 2) Select(services, 1);
        else if (current == 5)
        {
            services.ResolveController().WriteByte(0x381b, 1);
            Select(services, 1);
        }
        return true;
    }

    public static void Select(WorldNetworkMenuServices services, uint selection)
    {
        ArgumentNullException.ThrowIfNull(services);
        var menu = services.Menu;
        menu.WriteWord(0x158, selection);
        FlagsFor(1, 0x15c, false); FlagsFor(2, 0x160, false); FlagsFor(3, 0x164, false);
        FlagsFor(4, 0x168, false); FlagsFor(4, 0x168, false); FlagsFor(7, 0x16c, false);
        var current = menu.ReadWord(0x158);
        services.SetVisibility(menu.ReadWord(0x174), (current & 0xffffff00u) | (current == 6 ? 1u : 0));
        FlagsFor(1, 0x15c, true); FlagsFor(2, 0x160, true); FlagsFor(3, 0x164, true);
        FlagsFor(4, 0x168, true); FlagsFor(4, 0x168, true); FlagsFor(7, 0x16c, true);
        if (menu.ReadWord(0x158) != 5) services.SetBits(menu.ReadWord(0x170), 5, false);
        else
        {
            var source = menu.ReadWord(0x154);
            if (source is 1 or 2 or 5) ConfigurePage(services, menu.ReadWord(0x170), source);
            services.SetBits(menu.ReadWord(0x170), 5, true);
        }

        void FlagsFor(uint mode, uint offset, bool set)
        {
            if (menu.ReadWord(0x158) == mode) services.SetBits(menu.ReadWord(offset), 1, set);
        }
    }

    public static void ConfigurePage(WorldNetworkMenuServices services, uint pageAddress, uint source)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (source is not (1 or 2 or 5)) throw new NotSupportedException("No compared page configuration for this source.");
        var page = services.ReadPage(pageAddress);
        page.WriteByte(0x3cc, (byte)(source == 1 ? 1 : 0));
        page.WriteByte(0x3cd, (byte)(source == 5 ? 1 : 0));
        services.SetBits(page.ReadWord(0x3b0), 5, source == 2);
        services.UpdateList(pageAddress, page.ReadWord(0x390));
    }

    /// <summary>Complete 0x70B790 caller. Source reset occurs after page-selection
    /// callbacks when the captured visibility argument's low byte is zero.</summary>
    public static void SetVisibility(WorldNetworkMenuServices services, uint argument, Action<uint> baseVisibility)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseVisibility);
        baseVisibility(argument);
        if ((argument & 0xff) != 0) return;
        Select(services, 0);
        services.Menu.WriteWord(0x154, 0);
    }
}
