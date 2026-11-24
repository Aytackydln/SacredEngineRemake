using System.Text;

namespace Sacred.GameServer.Web;

internal static partial class Pages
{
    /// <summary>
    /// <c>GET /auction</c>: the auction house rules (durations, fee, cut, limits, allow_special, expiry interval) of the auction_house mod, edited while the
    /// server runs: reads the mod's <c>rules</c> endpoint and writes its storage key <c>config</c> through <c>POST /api/mod-store</c>.
    /// </summary>
    public static string AuctionPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(6 * 1024);
        sb.Append("<div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "auction", admin.Mods().Count);
        sb.Append("</header>").Append(Assets.Text(EmbeddedResource_Assets.auction_html)).Append("</div>");
        return Assets.Page("Auction - " + E(o.Name) + " - Sacred GameServer", sb.ToString(), ["app.css", "auction.css"], ["auction.js"]);
    }
}
