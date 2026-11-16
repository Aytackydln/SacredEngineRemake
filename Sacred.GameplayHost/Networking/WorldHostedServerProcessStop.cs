namespace Sacred.GameplayHost.Networking;

/// <summary>Compared hosted-process stop loop in local controller event 0x117.
/// Window +0x24 and process handle +0x20 are separate. Storage and OS callbacks
/// retain their native ordering and rereads; this never adopts a running process.</summary>
public static class WorldHostedServerProcessStop
{
    public static void Stop(WorldControlMemory controller, WorldHostedServerProcessServices services)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(services);
        var window = controller.ReadWord(0x24);
        if (window != 0) services.PostClose(window);
        else
        {
            window = services.FindWindow("SACREDGAMESERVERCLASS");
            if (window != 0) services.PostClose(window);
            else services.Terminate(controller.ReadWord(0x20));
        }
        var status = services.ReadExitStatus(controller.ReadWord(0x20));
        while (status.Success && status.Code == 0x103)
        {
            services.Sleep(10);
            status = services.ReadExitStatus(controller.ReadWord(0x20));
        }
        controller.WriteByte(0x1c, 0);
        controller.WriteWord(0x24, 0);
    }
}
