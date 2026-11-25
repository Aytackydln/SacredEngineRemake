using System.Text;

namespace Sacred.GameServer.Web;

internal static partial class Pages
{
    /// <summary>
    /// <c>GET /bounties</c>: the bounty board's configuration (slots, lifetime, announcements and the pool of bounties: creature words, target range,
    /// minimum level, lifetime, gold and item reward) edited while the server runs: reads the mod's admin-only <c>rules</c> endpoint and writes its storage
    /// key <c>config</c> through <c>POST /api/mod-store</c>. Pool fields the page has no column for (<c>types</c>, <c>sectors</c>, <c>region</c>) are kept.
    /// </summary>
    public static string BountyPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "bounties", admin.Mods().Count);
        sb.Append("</header>").Append(Assets.Text(EmbeddedResource_Assets.bounty_html)).Append("</div>");
        return Assets.Page("Bounties - " + E(o.Name) + " - Sacred GameServer", sb.ToString(), ["app.css", "bounty.css"], ["bounty.js"]);
    }
}
