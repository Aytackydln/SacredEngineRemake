namespace Sacred.Network.Admission;

/// <summary>One verified FIRSTCONTACT comparison; a null path denotes an unresolved session value.</summary>
public sealed record GoldDataFingerprintField(int PayloadOffset, uint MismatchMask, string? RelativePath);
