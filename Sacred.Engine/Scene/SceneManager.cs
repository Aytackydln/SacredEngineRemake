using System;
using System.Collections.Generic;

namespace Sacred.Engine.Scene;

internal sealed class SceneManager : IDisposable
{
    private readonly Dictionary<GameSceneId, SceneRegistration> _registrations = new();
    private IGameScene? _activeScene;
    private GameSceneId? _requestedScene;
    private bool _disposed;

    public event Action? SceneChanged;

    public IGameScene ActiveScene =>
        _activeScene ?? throw new InvalidOperationException("No scene is active.");

    public GameSceneId ActiveSceneId => ActiveScene.Id;

    public void Register(GameSceneId id, Func<IGameScene> factory, bool preserveInMemory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(factory);
        if (_registrations.ContainsKey(id))
            throw new InvalidOperationException($"Scene '{id}' is already registered.");

        _registrations.Add(id, new SceneRegistration(factory, preserveInMemory));
    }

    public void RegisterInstance(IGameScene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (_registrations.ContainsKey(scene.Id))
            throw new InvalidOperationException($"Scene '{scene.Id}' is already registered.");

        _registrations.Add(scene.Id, new SceneRegistration(() => scene, true) { Instance = scene });
    }

    public void Start(GameSceneId id)
    {
        if (_activeScene is not null)
            throw new InvalidOperationException("The scene manager has already started.");

        Activate(id);
    }

    public void RequestSwitch(GameSceneId id) => _requestedScene = id;

    public void Update(float deltaSeconds)
    {
        ActiveScene.Update(deltaSeconds);
        if (_requestedScene is not { } requested)
            return;

        _requestedScene = null;
        if (requested != ActiveScene.Id)
            Activate(requested);
    }

    private void Activate(GameSceneId id)
    {
        if (!_registrations.TryGetValue(id, out var registration))
            throw new InvalidOperationException($"Scene '{id}' has not been registered.");

        var previous = _activeScene;
        previous?.OnDeactivated();
        _activeScene = registration.GetOrCreate();
        _activeScene.OnActivated();
        Console.WriteLine($"Scene switch: {previous?.Id.ToString() ?? "None"} -> {id}");
        SceneChanged?.Invoke();

        if (previous is null || previous == _activeScene)
            return;

        var previousRegistration = _registrations[previous.Id];
        if (!previousRegistration.PreserveInMemory)
        {
            previous.Dispose();
            previousRegistration.Instance = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var disposed = new HashSet<IGameScene>(ReferenceEqualityComparer.Instance);
        if (_activeScene is not null && disposed.Add(_activeScene))
            _activeScene.Dispose();
        foreach (var registration in _registrations.Values)
        {
            if (registration.Instance is { } instance && disposed.Add(instance))
                instance.Dispose();
        }

        _registrations.Clear();
        _activeScene = null;
    }

    private sealed class SceneRegistration(Func<IGameScene> factory, bool preserveInMemory)
    {
        public bool PreserveInMemory { get; } = preserveInMemory;
        public IGameScene? Instance { get; set; }

        public IGameScene GetOrCreate()
        {
            var scene = Instance ?? factory();
            if (PreserveInMemory)
                Instance = scene;
            return scene;
        }
    }
}
