namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared field/diagnostic adapters and the explicit storage word
/// before the logical deque. Its value must have caller-established provenance.</summary>
public sealed record WorldObjectReferenceCleanupCallbacks<TObject>(
    Func<TObject, uint> ReadReference, Action<TObject, uint> WriteReference,
    Func<TObject, uint> ReadFlags, Func<uint>? ReadPredecessor,
    Action<WorldObjectReferenceCleanupDiagnostic> Report) where TObject : class
{
    /// <summary>Explicit original storage outside logical bounds. Native
    /// negative-index swap writes can make later reads depend on these bytes.</summary>
    public Func<int, uint>? ReadOutsideLogical { get; init; }
    public Action<int, uint>? WriteOutsideLogical { get; init; }
}

public enum WorldObjectReferenceCleanupDiagnosticKind { ZeroReference, Repair, LiveReference, Duplicates, DuplicateIndex }

/// <summary>Native diagnostic arguments, including UInt32 bits of negative indices.</summary>
public readonly record struct WorldObjectReferenceCleanupDiagnostic(
    WorldObjectReferenceCleanupDiagnosticKind Kind, uint First, uint Second = 0);
