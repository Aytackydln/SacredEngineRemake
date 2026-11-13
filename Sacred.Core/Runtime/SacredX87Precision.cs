namespace Sacred.Core.Runtime;

/// <summary>x87 precision-control field, independent of rounding direction and
/// exception masks. Original controlled replays retain their explicit mode.</summary>
public enum SacredX87Precision : byte
{
    Single = 24,
    Double = 53,
    Extended = 64
}
