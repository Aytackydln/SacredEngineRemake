using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Native script operand codes; these state operands have no payload.</summary>
public enum SacredScriptObjectStateOperand : byte
{
    Lock = 0x06,
    Unlock = 0x07,
    Open = 0x0F,
    Close = 0x10,
    TriggerOpen = 0x26,
    TriggerClose = 0x27,
    TriggerLock = 0x2C,
    TriggerUnlock = 0x2D
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptTriggerPatchPositionLayout
{
    public const int SerializedSize = 13;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly int TileX;
    [FieldOffset(5)] public readonly int TileY;
    [FieldOffset(9)] public readonly int SurfaceLevel;
}

/// <summary>CreateTrigger operand 0x28 followed by one byte selecting the trigger type.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptTriggerTypeLayout
{
    public const int SerializedSize = 2;
    public const byte OperandKind = 0x28;
    [FieldOffset(0)] public readonly byte Kind;
    [FieldOffset(1)] public readonly byte TriggerType;
}

public static class SacredScriptDoorOpcodes
{
    public const ushort SetObjectState = 2;
    public const ushort CreateTrigger = 48;
    public const ushort SetTriggerState = 49;
    public const ushort TriggerPatch = 50;
}
