namespace Sacred.GameplayHost.Scripting;

/// <summary>Native clock, cleanup, object-type and diagnostic boundaries.</summary>
public sealed record WorldObjectReferenceMaintenanceCallbacks<TObject>(
    Func<uint> Clock, Func<TObject, uint> ReadObjectType, Action<uint> Cleanup,
    Action<uint, uint> ReportOccupied, Action<uint> ReportReturned) where TObject : class;
