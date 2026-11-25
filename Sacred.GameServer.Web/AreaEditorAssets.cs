namespace Sacred.GameServer.Web;

/// <summary>The area editor page's own style and script (one inline script, no libraries; works offline). The theme variables come from
/// the admin UI's style (<c>Assets/app.css</c>), so light and dark follow it.</summary>
public static class AreaEditorAssets
{
    public static string Script => Assets.Text(EmbeddedResource_Assets.areas_js);
}
