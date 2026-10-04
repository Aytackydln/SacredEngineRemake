using System.Numerics;
using Sacred.Particles.Diagnostics;

namespace Sacred.Particles;

/// <summary>Fixed-capacity structure-of-arrays storage for one native parameter set.
/// Vector lanes contain different particles, rather than XYZ of a single particle.</summary>
public sealed class ParticleSimulationBatch
{
    internal readonly float[] X, Y, Z, Vx, Vy, Vz, Gravity, Size, Fade, Rotation, AngularVelocity;
    private readonly int[] _atlasCells, _drawOrders;
    private bool _hasDeadBirths;

    public SacredParticleParameterSet Parameters { get; }
    private int _count;
    private ParticleGroundCollision? _gpuCollision;
    private IParticleGpuBackend? _gpuBackend;
    private bool _gpuOnly;
    public int Count => Gpu?.Count ?? _count;
    public ParticleGpuBatch? Gpu { get; private set; }
    public IParticleGpuBackend? GpuBackend
    {
        get => _gpuBackend;
        set
        {
            if (!ReferenceEquals(value, _gpuBackend))
            {
                if (_gpuOnly) DiscardGpu();
                else RestoreCpu();
            }
            _gpuBackend = value;
        }
    }
    public int Capacity => X.Length;

    public ParticleSimulationBatch(SacredParticleParameterSet parameters, int capacity)
    {
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.SimulationStorage);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Parameters = parameters;
        X = new float[capacity]; Y = new float[capacity]; Z = new float[capacity];
        Vx = new float[capacity]; Vy = new float[capacity]; Vz = new float[capacity];
        Gravity = new float[capacity]; Size = new float[capacity]; Fade = new float[capacity];
        Rotation = new float[capacity]; AngularVelocity = new float[capacity];
        _atlasCells = new int[capacity]; _drawOrders = new int[capacity];
    }

    public ParticleSimulationState this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            if (Gpu is not null) throw new InvalidOperationException("GPU particle state requires an explicit CPU snapshot.");
            return new ParticleSimulationState
            {
                Position = new(X[index], Y[index], Z[index]),
                Velocity = new(Vx[index], Vy[index], Vz[index]),
                Gravity = Gravity[index], Size = Size[index], Fade = Fade[index],
                Rotation = Rotation[index], AngularVelocity = AngularVelocity[index],
                AtlasCell = _atlasCells[index], DrawOrder = _drawOrders[index]
            };
        }
    }

    public void Add(ParticleSimulationState particle)
    {
        if (Gpu is not null) { Gpu.Add(particle); return; }
        if (Count == Capacity) throw new InvalidOperationException("Particle batch capacity exceeded.");
        var index = _count++;
        X[index] = particle.Position.X; Y[index] = particle.Position.Y; Z[index] = particle.Position.Z;
        Vx[index] = particle.Velocity.X; Vy[index] = particle.Velocity.Y; Vz[index] = particle.Velocity.Z;
        Gravity[index] = particle.Gravity; Size[index] = particle.Size; Fade[index] = particle.Fade;
        Rotation[index] = particle.Rotation; AngularVelocity[index] = particle.AngularVelocity;
        _atlasCells[index] = particle.AtlasCell; _drawOrders[index] = particle.DrawOrder;
        _hasDeadBirths |= particle.Fade <= 0 || particle.Size <= 0;
    }

    public void Update(float deltaSeconds, Vector3 gravityDirection,
        ParticleSimulationMode mode = ParticleSimulationMode.CpuSimd,
        ParticleGroundCollision collision = ParticleGroundCollision.None, float groundHeight = 0)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(collision)) throw new ArgumentOutOfRangeException(nameof(collision));
        _gpuOnly = mode == ParticleSimulationMode.GpuOnly;
        if (_gpuOnly && GpuBackend is not { IsAvailable: true }) return;
        if (mode == ParticleSimulationMode.Auto)
            mode = GpuBackend is { IsAvailable: true } ? ParticleSimulationMode.Gpu : ParticleSimulationMode.CpuSimd;
        if (mode is ParticleSimulationMode.Gpu or ParticleSimulationMode.GpuOnly && GpuBackend is { IsAvailable: true })
        {
            // Native emitters keep this fixed. Explicit tool changes need fresh vertical lifetime state.
            if (Gpu is not null && _gpuCollision != collision)
            {
                if (_gpuOnly) DiscardGpu();
                else RestoreCpu();
            }
            if (Gpu is null)
            {
                var initial = new ParticleSimulationState[_count];
                for (var i = 0; i < initial.Length; i++) initial[i] = this[i];
                Gpu = new(Parameters, Capacity);
                _gpuCollision = collision;
                foreach (var particle in initial) Gpu.Add(particle);
                _count = 0;
            }
            Gpu.Update(deltaSeconds, gravityDirection, collision, groundHeight);
            return;
        }
        RestoreCpu();
        if (mode == ParticleSimulationMode.Gpu) mode = ParticleSimulationMode.CpuSimd;
        // Native movement skips particles already dead at the start of a frame.
        if (_hasDeadBirths) Compact();
        ParticleCpuSimulation.Update(this, deltaSeconds, gravityDirection, mode, collision, groundHeight);
        Compact();
    }

    public void RestoreCpu()
    {
        if (Gpu is null) return;
        var snapshot = (GpuBackend ?? throw new InvalidOperationException("GPU particle snapshot backend unavailable.")).Snapshot(Gpu);
        Gpu = null;
        _gpuCollision = null;
        _count = 0;
        foreach (var particle in snapshot) Add(particle);
    }

    // A strict GPU backend replacement cannot read particles back through the CPU fallback.
    private void DiscardGpu()
    {
        if (Gpu is null) return;
        Gpu = null;
        _gpuCollision = null;
        _count = 0;
    }

    // Stable, linear compaction preserves birth ordering and avoids repeated List.RemoveAt shifts.
    private void Compact()
    {
        var write = 0;
        for (var read = 0; read < Count; read++)
        {
            if (Fade[read] <= 0 || Size[read] <= 0) continue;
            if (write != read)
            {
                X[write] = X[read]; Y[write] = Y[read]; Z[write] = Z[read];
                Vx[write] = Vx[read]; Vy[write] = Vy[read]; Vz[write] = Vz[read];
                Gravity[write] = Gravity[read]; Size[write] = Size[read]; Fade[write] = Fade[read];
                Rotation[write] = Rotation[read]; AngularVelocity[write] = AngularVelocity[read];
                _atlasCells[write] = _atlasCells[read]; _drawOrders[write] = _drawOrders[read];
            }
            write++;
        }
        _count = write;
        _hasDeadBirths = false;
    }
}
