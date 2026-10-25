namespace Sacred.GameplayHost.World;

/// <summary>Gold engine event 6: one shared 32-slot pool for resource and numeric readouts.</summary>
public sealed class GameplayWorldTextPool
{
    private readonly GameplayWorldText?[] _slots = new GameplayWorldText[32];
    public IEnumerable<GameplayWorldText> Entries => _slots.OfType<GameplayWorldText>();
    public long Dropped { get; private set; }
    public bool Add(GameplayWorldText entry)
    {
        var slot = Array.FindIndex(_slots, existing => existing is null);
        if (slot < 0 && entry.TextId == 0x42F)
            slot = Array.FindIndex(_slots, existing => existing?.Resource == 0);
        if (slot < 0) { Dropped++; return false; }
        _slots[slot] = entry; return true;
    }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        for (var index = 0; index < _slots.Length; index++)
        {
            _slots[index]?.Advance(seconds);
            if (_slots[index]?.Expired == true) _slots[index] = null;
        }
    }
    public void Clear() { Array.Clear(_slots); Dropped = 0; }
}
