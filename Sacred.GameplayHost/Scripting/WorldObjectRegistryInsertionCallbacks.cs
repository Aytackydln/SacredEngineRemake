namespace Sacred.GameplayHost.Scripting;

/// <summary>Family-specific construction, destructor and activation boundaries.
/// Prepare assigns the post-construction allocation byte, before activation.</summary>
public sealed record WorldObjectRegistryInsertionCallbacks<TObject>(
    Func<uint, TObject?> Construct, Func<TObject, uint> ReadFlags,
    Action<TObject> Destroy, Action<TObject, byte> Prepare, Action<TObject> Activate)
    where TObject : class;
