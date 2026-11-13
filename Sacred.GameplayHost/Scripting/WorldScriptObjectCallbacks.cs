using Sacred.Network.Gameplay;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Native interpreter +0x358 callback rows in construction order.
/// Repeated references/names append; registration does not invoke a function.</summary>
public sealed class WorldScriptObjectCallbacks
{
    private readonly List<GoldObjectScriptCallback> _records = [];
    public WorldScriptObjectCallbacks() => Records = _records.AsReadOnly();
    public IReadOnlyList<GoldObjectScriptCallback> Records { get; }
    public void Append(GoldObjectScriptCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _records.Add(callback);
    }
    public void RegisterCreature(uint reference, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        Append(GoldObjectScriptCallback.Create(reference, name));
    }
    /// <summary>Gold 0x48394A reuses the command's zero-initialized callback
    /// scratch after its death append. A shorter See: name retains bytes after
    /// the terminator from that prior name; all 68 bytes enter the vector.</summary>
    public void RegisterCreatureSee(uint reference, string name, string? deathCallback)
    {
        var prefixed = SacredScriptObjectCallbackLayout.SeePrefix + name;
        _ = GoldObjectScriptCallback.Create(reference, prefixed);
        var scratch = GoldObjectScriptCallback.Create(reference, deathCallback ?? "").ToPayload();
        Encoding.Latin1.GetBytes(prefixed + '\0', scratch.AsSpan(sizeof(uint)));
        Append(GoldObjectScriptCallback.FromPayload(scratch));
    }
}
