using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold script quest record, native vector at 0xAABF18 (stride 0x124).
/// Opcode 21 searches Number before activation. Unmapped fields retain their offsets.
/// Vectoren.bin stores these after its counted function table; initial bytes are copied by Gold 0x470054.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptQuestLayout
{
    public const int SerializedSize = 0x124;
    public const int NameOffset = 4;
    public const int NameLength = 256;
    [FieldOffset(NameOffset), BinaryString("Name", NameLength, "ISO-8859-1")] private readonly byte _name;
    public const int ActivationFunctionOffset = 0x10C;
    /// <summary>Invoked by quest entry 0x46CB94, after flag 4 and journal append.</summary>
    public const int EnterFunctionOffset = 0x110;
    public const int SetupFunctionOffset = 0x114;
    /// <summary>Additional function-index words converted to name hashes by
    /// normal activation 0x46DC9C..0x46DD41, alongside the three named callbacks.</summary>
    [FieldOffset(0x118)] public readonly int CallbackFunction118;
    [FieldOffset(0x11C)] public readonly int CallbackFunction11C;
    /// <summary>Invoked by activation and by TriggerQuest (Gold 0x48F1EB).
    /// TriggerQuest resolves QIS_Trigger%d when zero, then branches on the
    /// function dispatch result before quest entry.</summary>
    [FieldOffset(ActivationFunctionOffset)] public readonly int ActivationFunction;
    /// <summary>Gold 0x46CB94 dispatches this callback after entry state and journal writes.</summary>
    [FieldOffset(EnterFunctionOffset)] public readonly int EnterFunction;
    /// <summary>Invoked before ActivationFunction and cleared after the invocation returns.</summary>
    [FieldOffset(SetupFunctionOffset)] public readonly int SetupFunction;
    public const int FlagsOffset = 0x120;
    /// <summary>Gold 0x46DB54 suppresses repeated activation when this bit is set.</summary>
    public const uint ActiveFlag = 1;
    /// <summary>QuestDone tests bit 2 at Gold 0x47789E / 0x47797E.</summary>
    public const uint SolvedFlag = 2;
    /// <summary>Gold 0x46C59F sets this before the OnEnter callback. It does not mark the quest solved.</summary>
    public const uint TriggeredFlag = 4;
    /// <summary>Set around activation callbacks, then cleared by Gold 0x46DBD5.</summary>
    public const uint ActivatingFlag = 8;
    [FieldOffset(0)] public readonly int Number;
    [FieldOffset(FlagsOffset)] public readonly uint Flags;
}
