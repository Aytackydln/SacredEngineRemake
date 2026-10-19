using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Granny.Diagnostics;

namespace Sacred.Engine.Animation;

/// <summary>One writer per character; all workers finish before effects or rendering read poses.</summary>
internal sealed class CharacterAnimationBatch
{
    public static int WorkerLimit { get; set; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
    private readonly List<(CharacterAnimationState State, float Speed)> _items = [];
    private readonly HashSet<CharacterAnimationState> _owners = [];
    private readonly ParallelOptions _options = new()
    {
        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 8)
    };
    private readonly Action<int> _update;
    private float _deltaSeconds;

    public CharacterAnimationBatch() => _update = Update;

    public void Add(CharacterAnimationState state, float speed)
    {
        if (_owners.Add(state)) _items.Add((state, speed));
    }

    public void Run(float deltaSeconds)
    {
        using var measurement = AnimationPerformance.Measure(AnimationCpuStage.CharacterBatchWall);
        _options.MaxDegreeOfParallelism = Math.Clamp(WorkerLimit, 1, Environment.ProcessorCount);
        _deltaSeconds = deltaSeconds;
        try
        {
            if (_items.Count >= 8 && _options.MaxDegreeOfParallelism > 1)
                Parallel.For(0, _items.Count, _options, _update);
            else
                for (var i = 0; i < _items.Count; i++) Update(i);
            foreach (var item in _items) item.State.UpdateEquipmentEffects(deltaSeconds);
        }
        finally { _items.Clear(); _owners.Clear(); }
    }

    private void Update(int index)
    {
        var item = _items[index];
        item.State.UpdatePoseOnly(_deltaSeconds, item.Speed);
    }
}
