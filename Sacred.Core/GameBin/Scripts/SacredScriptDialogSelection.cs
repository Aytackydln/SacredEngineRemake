namespace Sacred.Core.GameBin.Scripts;

/// <summary>Last explicit CreateNPC dialog name (0x09) or UInt16 number (0x0A).
/// Both resolve to registration slots, never directly to functions.</summary>
public sealed record SacredScriptDialogSelection(string? Name, ushort? Number);
