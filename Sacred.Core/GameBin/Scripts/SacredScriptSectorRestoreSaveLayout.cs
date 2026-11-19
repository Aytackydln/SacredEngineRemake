namespace Sacred.Core.GameBin.Scripts;

/// <summary>Interpreter save sub-block, not a world-bank header. Gold
/// 466F7D writes its marker, then count and native +364 records. 469CFB
/// checks the marker and 469D49 computes count * 280 before importing rows.</summary>
public static class SacredScriptSectorRestoreSaveLayout
{
    public const uint Marker = 0xABCDABCD;
    public const int CountOffset = 4;
    public const int RecordsOffset = 8;
    public const int RecordSize = 280;
    public const int InterpreterVectorOffset = 0x364;
}
