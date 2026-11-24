namespace Sacred.GameServer.Web;

/// <summary>The Events page's script (<c>Assets/events.js</c>) and style (<c>events.css</c>), no libraries. It talks to the world_events mod: reads
/// <c>/mods/world_events/events</c> and writes the mod's storage through <c>POST /api/mod-store</c> (keys <c>events</c>, <c>events_off</c>
/// and the command key <c>control</c>); the mod picks changes up within a couple of seconds, no reload needed.</summary>
public static class EventsPageAssets
{
    public static string Css => Assets.Text(EmbeddedResource_Assets.events_css);

    public static string Script => Assets.Text(EmbeddedResource_Assets.events_js);
}
