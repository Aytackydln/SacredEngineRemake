using System.Runtime.InteropServices;

namespace SacredRemake;

internal static partial class WindowsApplicationIdentity
{
    private const string AppUserModelId =
        "SacredEngineRemake.SacredEngineRemake";

    public static void Initialize()
    {
        _ = SetCurrentProcessExplicitAppUserModelId(AppUserModelId);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16,
        EntryPoint = "SetCurrentProcessExplicitAppUserModelID")]
    private static partial int SetCurrentProcessExplicitAppUserModelId(string appId);
}