using System;

namespace SacredRemake;

internal static class LaunchArguments
{
    internal static bool IsTerminalMode(string argument) =>
        string.Equals(argument, "-terminal", StringComparison.OrdinalIgnoreCase);
}
