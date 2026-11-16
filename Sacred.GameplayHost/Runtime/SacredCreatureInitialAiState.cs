namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold 0x59F580 plus CreateNPC's +0x204 interval write, without an active hero.
/// Mode/state are native +0xFC/+0xFE; interval/elapsed are +0x204/+0x208.
/// The hero-dependent script/movement branch and later AI ticks remain separate.</summary>
public readonly record struct SacredCreatureInitialAiState(ushort Mode, ushort State, float Interval, float Elapsed)
{
    public SacredCreatureInitialAiState ApplyWithoutHero(uint runtimeFlags, uint behavior)
    {
        if ((behavior & 1) == 0) return this;
        var result = this;
        if ((runtimeFlags & 0x40000) == 0 && Mode == 0 && State == 0)
            result = result with { State = 1, Elapsed = Interval };
        return result with { Interval = 2.5f };
    }
}
