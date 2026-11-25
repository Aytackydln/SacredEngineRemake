using System.Text;

namespace Sacred.GameServer.Web;

/// <summary>
/// <c>GET /sectors</c>: the live sector ownership map. The same canvas Ancaria map and sector grid as the area editor (<c>/api/areas/map</c>,
/// needs --game-dir), with every sector a player owns filled in that player's colour, occupied sectors outlined, the players as dots and
/// the owner and the players inside a sector on hover or tap. The data arrives as Server-Sent Events from <c>/api/sectors/stream</c>
/// (polling <c>/api/sectors</c> when the browser has no EventSource). One inline script, no libraries, works offline and on a phone.
/// </summary>
internal static partial class Pages
{
    /// <summary><c>GET /sectors</c>: live sector ownership map for admins (style and script are <c>Assets/sectors.css</c> and <c>sectors.js</c>).</summary>
    public static string SectorsPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(10 * 1024);
        sb.Append("<div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "sectors", admin.Mods().Count);
        sb.Append("</header>").Append(Assets.Text(EmbeddedResource_Assets.sectors_html)).Append("</div>");
        return Assets.Page("Sectors - " + E(o.Name) + " - Sacred GameServer", sb.ToString(), ["app.css", "sectors.css"], ["sectors.js"]);
    }
}
