using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Engine.Assets;

namespace Sacred.Engine.Scene;

internal sealed class ResourceLoadSequence
{
    private readonly IReadOnlyList<ResourceLoadStep> _steps;
    private Task? _currentTask;
    private int _stepIndex;

    public ResourceLoadSequence(IReadOnlyList<ResourceLoadStep> steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        if (steps.Count == 0)
            throw new ArgumentException("A loading sequence must contain at least one step.", nameof(steps));
    }

    public bool IsComplete => _stepIndex == _steps.Count;
    public double Progress => _stepIndex / (double)_steps.Count;
    public string CurrentItem => IsComplete ? "Ready" : _steps[_stepIndex].DisplayName;

    /// <summary>Advances no more than one completed step so every progress label can be presented.</summary>
    public bool Update()
    {
        if (IsComplete)
            return false;

        _currentTask ??= Task.Run(_steps[_stepIndex].Load);
        if (!_currentTask.IsCompleted)
            return false;

        _currentTask.GetAwaiter().GetResult();
        _currentTask = null;
        _stepIndex++;
        return true;
    }

    public void WaitForActiveStep() => _currentTask?.GetAwaiter().GetResult();
}
