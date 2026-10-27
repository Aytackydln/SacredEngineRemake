namespace Sacred.Core.Loot;

public enum SacredCreatureLootCandidateDisposition { Advance, RetrySelection, Fallback }
public readonly record struct SacredCreatureLootCandidateResult(SacredCreatureLootCandidateDisposition Disposition, uint Resource);

/// <summary>Per 0x559BF0 invocation, retained across outer budget passes.
/// Independent of the six selection attempts allowed within each pass.</summary>
public sealed class SacredCreatureLootCandidateState
{
    public int RejectedSharedRecords { get; set; }
}

/// <summary>Gold 0x55A7F4..0x55A8E7: modifier -1 always retries; absent shared
/// values or their flag 0x40 retry only for the first five such rejections.</summary>
public static class SacredCreatureLootCandidateValidation
{
    public static SacredCreatureLootCandidateResult Validate(uint resource, bool hasCategory, int maximumResource,
        uint fallbackResource, SacredCreatureLootCandidateState state,
        Func<uint, int?> modifierType, Func<uint, uint?> sharedFlags)
    {
        if (!hasCategory || resource == 0 || unchecked((int)resource) > maximumResource)
            return new(SacredCreatureLootCandidateDisposition.Fallback, fallbackResource);
        if (modifierType(resource) == -1) return new(SacredCreatureLootCandidateDisposition.RetrySelection, resource);
        var flags = sharedFlags(resource);
        if (!flags.HasValue || (flags.Value & 0x40) != 0)
        {
            var previous = state.RejectedSharedRecords++;
            if (previous < 5) return new(SacredCreatureLootCandidateDisposition.RetrySelection, resource);
        }
        return new(SacredCreatureLootCandidateDisposition.Advance, resource);
    }
}
