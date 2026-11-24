using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Sacred.GameServer.Web;

/// <summary>
/// The static files of the web UI (<c>Assets/*.css</c>, <c>*.js</c>, <c>*.html</c>, <c>*.txt</c>) are embedded resources, read through
/// the accessors that Raffinert.EmbeddedResourceAccessGenerator generates (<see cref="EmbeddedResource_Assets"/>): no resource names as
/// strings, no reflection, trim and AOT safe, one deployable file, nothing fetched from outside. Style sheets and scripts are served to the
/// browser as files under <c>/assets/</c> (<see cref="TryGetFile"/>); the HTML files are templates the server fills in.
/// </summary>
internal static class Assets
{
    private const string Prefix = "Sacred.GameServer.Web.Assets.";

    /// <summary>A served file: its content type, bytes and a validator for conditional requests.</summary>
    public sealed record File(string ContentType, byte[] Content, string ETag);

    private static readonly ConcurrentDictionary<EmbeddedResource_Assets, string> Texts = new();

    // File name -> resource, for the /assets/ route. Only style sheets and scripts are public; templates and data stay inside.
    private static readonly Dictionary<string, EmbeddedResource_Assets> Served = Enum.GetValues<EmbeddedResource_Assets>()
        .Select(r => (Name: r.GetResourceName()[Prefix.Length..], Resource: r))
        .Where(f => f.Name.EndsWith(".css", StringComparison.Ordinal) || f.Name.EndsWith(".js", StringComparison.Ordinal))
        .ToDictionary(f => f.Name, f => f.Resource, StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, File> Files = new(StringComparer.Ordinal);

    /// <summary>The text of an asset, without the trailing newline of the file.</summary>
    public static string Text(EmbeddedResource_Assets resource) =>
        Texts.GetOrAdd(resource, static r => r.ReadAllText().TrimEnd('\n', '\r'));

    /// <summary>A style sheet or script by file name (<c>app.css</c>); false for anything else (no folders, no templates).</summary>
    public static bool TryGetFile(string name, out File file)
    {
        file = null!;
        if (!Served.TryGetValue(name, out var resource)) return false;
        file = Files.GetOrAdd(name, _ => Create(name, Text(resource)));
        return true;
    }

    /// <summary>A generated file (for example the scoped form-control rules): same headers and validator as an embedded one.</summary>
    public static File Create(string name, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var type = name.EndsWith(".css", StringComparison.Ordinal) ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8";
        return new File(type, bytes, "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16] + "\"");
    }

    /// <summary>Fills <c>{{name}}</c> placeholders of a template. Values go in as given: callers pass HTML-encoded text or markup they built.</summary>
    public static string Fill(string template, params (string Name, string Value)[] values)
    {
        // One pass over the template: a value is never searched for placeholders itself (a player name may contain "{{body}}").
        var sb = new System.Text.StringBuilder(template.Length + 256);
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf("{{", i, StringComparison.Ordinal);
            var close = open < 0 ? -1 : template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) { sb.Append(template, i, template.Length - i); break; }
            sb.Append(template, i, open - i);
            var name = template.AsSpan(open + 2, close - open - 2);
            var found = false;
            foreach (var (n, v) in values)
                if (name.SequenceEqual(n)) { sb.Append(v); found = true; break; }
            if (!found) sb.Append(template, open, close + 2 - open);
            i = close + 2;
        }
        return sb.ToString();
    }

    /// <summary>
    /// The page layout (<c>page.html</c>): head with the theme script, title and style sheets, the body and the scripts. Style sheets and
    /// scripts are file names under <c>/assets/</c> (a query string is allowed); the links are relative, so the UI also works behind a path base.
    /// </summary>
    public static string Page(string title, string body, string[] styles, string[] scripts, string viewport = "width=device-width, initial-scale=1") =>
        Fill(Text(EmbeddedResource_Assets.page_html), ("viewport", viewport), ("title", title), ("body", body),
            ("styles", string.Concat(styles.Select(s => Fill(Text(EmbeddedResource_Assets.style_link_html), ("name", s))))),
            ("scripts", string.Concat(scripts.Select(s => Fill(Text(EmbeddedResource_Assets.script_link_html), ("name", s))))));
}
