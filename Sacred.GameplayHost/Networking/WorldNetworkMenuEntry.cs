namespace Sacred.GameplayHost.Networking;

/// <summary>Original mode-2 network-window command segment 0x70B894..0x70BD3F.
/// Caller has already validated local family 5 / command 0x36 / mode 2. Credential
/// configuration owns the reached registry/string/OS path; Initialize owns the
/// compared controller initializer with actual connection inputs. No OS bindings
/// or defaults are installed. Caller still owns later message forwarding.</summary>
public static class WorldNetworkMenuEntry
{
    public static void Begin(WorldNetworkMenuServices services, uint requestedSource, Action loadConfiguration,
        Action<WorldControlMemory, byte> initialize)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(loadConfiguration);
        ArgumentNullException.ThrowIfNull(initialize);
        var menu = services.Menu;
        menu.WriteWord(0x154, requestedSource);
        if (requestedSource is not (3 or 4 or 6)) return;
        loadConfiguration();
        var source = menu.ReadWord(0x154);
        var initializationMode = (byte)(source is 4 or 6 ? 1 : 0);
        var controller = services.ResolveController();
        initialize(controller, initializationMode);
        if (menu.ReadWord(0x154) == 3)
        {
            var current = services.ResolveController();
            current.WriteByte(0x3819, 0);
            current.WriteByte(0x381a, 0);
        }
    }
}
