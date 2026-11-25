using System.Text;

namespace Sacred.GameServer.Web;

/// <summary>
/// Themed form controls for the admin pages that edit things (Events, Areas): checkboxes and radios with a drawn box, a select with its
/// own arrow, a range slider, a visible focus ring and large touch targets. The rules are written once and scoped to the page's own
/// wrapper class (<see cref="Css"/> prefixes every selector), so nothing leaks into the shared stylesheet and the other pages. Colours
/// are the theme variables, so light and dark follow the UI.
/// </summary>
public static class FormControls
{
    // Rules with their declarations, one per line of Assets/form-controls.css; every selector is prefixed with each scope. The lines
    // after the "@coarse" marker apply on touch screens (bigger boxes and rows). Comment lines start with "/*".
    private static readonly (string[] Base, string[] Coarse) Rules = Load();

    private static (string[], string[]) Load()
    {
        var lines = Assets.Text(EmbeddedResource_Assets.form_controls_css).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var split = lines.FindIndex(l => l.Contains("@coarse", StringComparison.Ordinal));
        static string[] Rules(IEnumerable<string> part) => part.Where(l => l.Length > 0 && !l.StartsWith("/*", StringComparison.Ordinal)).ToArray();
        return (Rules(lines.Take(split)), Rules(lines.Skip(split + 1)));
    }

    private static readonly Dictionary<string, Assets.File> ScopedFiles = new(StringComparer.Ordinal);

    /// <summary>The rules scoped to a known page wrapper (<c>ae</c> = <c>.ae</c>, <c>ev</c> = <c>.ev</c>) as a served file; null for any other scope.</summary>
    internal static Assets.File? File(string scope)
    {
        if (scope is not ("ae" or "ev")) return null;
        lock (ScopedFiles) return ScopedFiles.TryGetValue(scope, out var file) ? file : ScopedFiles[scope] = Assets.Create("form-controls.css", Css("." + scope));
    }

    /// <summary>The rules for the given scopes (CSS selectors of a page's wrapper elements, such as <c>.ev</c> or <c>.ae</c>).</summary>
    public static string Css(params string[] scopes)
    {
        var sb = new StringBuilder(4096);
        foreach (var rule in Rules.Base) Append(sb, rule, scopes);
        sb.Append("@media (pointer:coarse){");
        foreach (var rule in Rules.Coarse) Append(sb, rule, scopes);
        sb.Append('}');
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string rule, string[] scopes)
    {
        var brace = rule.IndexOf('{', StringComparison.Ordinal);
        var selectors = rule[..brace].Split(',');
        var first = true;
        foreach (var scope in scopes)
            foreach (var selector in selectors)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(scope).Append(' ').Append(selector);
            }
        sb.Append(rule.AsSpan(brace)).Append('\n');
    }
}
