using System.Globalization;

namespace Sacred.Core.Pax.Hero;

internal static class SacredHeroPaxFilePath
{
    public static string ForSelection(string gameDirectory, int selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        if (selection is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(selection));
        return Path.Combine(gameDirectory, "Save", "Hero" + (selection - 1).ToString("D2", CultureInfo.InvariantCulture) + ".pax");
    }
}
