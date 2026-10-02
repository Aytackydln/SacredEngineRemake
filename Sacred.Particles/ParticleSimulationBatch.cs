using System.Numerics;

namespace Sacred.Particles;

/// <summary>Fixed-capacity structure-of-arrays storage for one native parameter set.
/// Vector lanes contain different particles, rather than XYZ of a single particle.</summary>
public sealed class ParticleSimulationBatch
{
    internal readonly float[] X, Y, Z, Vx, Vy, Vz, Gravity, Size, Fade, Rotation, AngularVelocity;
    private readonly int[] _atlasCells, _drawOrders;
    private bool _hasDeadBirths;

    public SacredParticleParameterSet Parameters { get; }
    public int Count { get; private set; }
    public int Capacity => X.Length;

    public ParticleSimulationBatch(SacredParticleParameterSet parameters, int capacity)
    {
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
        if (Count == Capacity) throw new InvalidOperationException("Particle batch capacity exceeded.");
        var index = Count++;
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
        // Native movement skips particles already dead at the start of a frame.
        if (_hasDeadBirths) Compact();
        ParticleCpuSimulation.Update(this, deltaSeconds, gravityDirection, mode, collision, groundHeight);
        Compact();
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
        Count = write;
        _hasDeadBirths = false;
    }
}
