using System;
using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace Sacred.Inventory.Effects;

/// <summary>Model-space implementation of the recovered generators and constrained point chains.
/// The pose moves the emitter; living particles and trailing points retain their previous positions.</summary>
internal sealed class NativeModelEffectSimulation
{
    private readonly SacredModelEffectDefinition _definition;
    private readonly Vector3 _start, _end, _direction;
    private readonly string? _bone;
    private readonly int _firstVertex;
    private readonly EquipmentEffectSurface[] _surfaces;
    private readonly Vector3[] _positions, _velocities;
    private readonly float[] _ages;
    private readonly float[] _sizes, _rotations, _angularVelocities, _gravity;
    private readonly Random _random = new(1401);
    private float _accumulator;
    private float _pendingChainSeconds;
    private bool _initialized;
    private int _nextParticle;

    public NativeModelEffectSimulation(SacredModelEffectDefinition definition, Vector3 start, Vector3 end,
        string? bone, Vector3 direction, int firstVertex, EquipmentEffectSurface[] surfaces)
    {
        _definition = definition;
        _start = start; _end = end; _direction = direction; _bone = bone;
        _firstVertex = firstVertex; _surfaces = surfaces;
        _positions = new Vector3[surfaces.Length];
        _velocities = new Vector3[surfaces.Length];
        _ages = new float[surfaces.Length];
        _sizes = new float[surfaces.Length];
        _rotations = new float[surfaces.Length];
        _angularVelocities = new float[surfaces.Length];
        _gravity = new float[surfaces.Length];
        Array.Fill(_ages, float.PositiveInfinity);
    }

    public void Update(Mesh mesh, GrnAnimatedMesh? pose, float deltaSeconds)
    {
        var start = Transform(pose, _start);
        var end = Transform(pose, _end);
        var direction = _direction;
        if (_bone is not null && pose is not null)
            pose.TryTransformRigidDirection(_bone, direction, out direction);
        direction = direction.LengthSquared() > 0.00001f ? Vector3.Normalize(direction) : Vector3.UnitX;
        if (!_initialized)
        {
            for (var i = 0; i < _positions.Length; i++)
                // Gold constructors 787120/7877C0: whip begins on -Y at the
                // simulation origin; streak begins collapsed. Demo streak differs.
                _positions[i] = _definition.Kind == SacredModelEffectKind.Whip
                    ? new Vector3(0, -i * _definition.LinkLength, 0) : Vector3.Zero;
            _initialized = true;
        }
        var dt = float.IsFinite(deltaSeconds) ? Math.Max(deltaSeconds, 0) : 0;
        if (_definition.PointCount > 0) UpdateChainWhenReady(start, direction, dt);
        else UpdateParticles(start, end, dt);
        for (var i = 0; i < _positions.Length; i++)
        {
            var chain = _definition.PointCount > 0;
            var fade = chain ? 1f : float.IsFinite(_ages[i]) && _sizes[i] > 0
                ? Math.Clamp(1f + _ages[i] * _definition.Motion.FadeChangeRate / 255f, 0, 1) : 0;
            // Native chain draw uses the glow texture at every point, rather than a scrolling strip.
            var color = chain ? new Vector4(1, 1, 1, Unpack(_definition.CornerColors[0]).W)
                : ParticleColor(i);
            color.W *= fade;
            _surfaces[i].Color = color;
            var halfSize = chain ? _definition.HalfSize : _sizes[i];
            // Keep the streak solver's endpoint-exclusive state intact, but draw
            // point zero at this frame's animated attachment. This removes the
            // visible one-substep lag without changing retained-point physics.
            var drawPosition = chain && i == 0 ? start : _positions[i];
            WriteQuad(mesh, i, drawPosition, halfSize, fade > 0);
        }
        mesh.NotifyVerticesChanged();
    }

    public void Reset()
    {
        _initialized = false;
        _accumulator = 0;
        _pendingChainSeconds = 0;
        _nextParticle = 0;
        Array.Clear(_velocities);
        Array.Clear(_sizes);
        Array.Clear(_rotations);
        Array.Clear(_angularVelocities);
        Array.Clear(_gravity);
        Array.Fill(_ages, float.PositiveInfinity);
    }

    public void Rebase(Matrix4x4 change)
    {
        if (!_initialized) return;
        // A chain's head belongs to the moving equipment, while its retained
        // points stay behind and are pulled forward by constraint relaxation.
        // Rebasing the head as well makes it alternate between the old world
        // position and the emitter; skipping every point makes a rigid shape.
        var firstRetainedPoint = _definition.PointCount > 0 ? 1 : 0;
        for (var i = firstRetainedPoint; i < _positions.Length; i++)
        {
            _positions[i] = Vector3.Transform(_positions[i], change);
            _velocities[i] = Vector3.TransformNormal(_velocities[i], change);
        }
    }

    private void UpdateChainWhenReady(Vector3 start, Vector3 direction, float dt)
    {
        if (_positions.Length == 0) return;
        if (dt <= 0) return;
        if (_definition.StepsPerSecond <= 0)
        {
            UpdateChain(start, direction, dt);
            return;
        }

        // Gold truncates the 200 Hz relaxation count on every invocation. Modern
        // render loops can invoke us above 200 FPS, so keep the visible head on
        // the animated attachment while elapsed time accumulates for relaxation.
        _pendingChainSeconds += dt;
        var steps = (int)(Math.Min(_pendingChainSeconds, .5f) * _definition.StepsPerSecond);
        if (steps < 2) return;

        var elapsed = _pendingChainSeconds;
        _pendingChainSeconds = 0;
        UpdateChain(start, direction, elapsed);
    }

    private void UpdateChain(Vector3 start, Vector3 direction, float dt)
    {
        // Demo 6AEC00 and Gold 787AC0 truncate EACH call, without carrying
        // fractional steps. Only streak clamps time to .5 seconds.
        var subdivided = _definition.StepsPerSecond > 0;
        var steps = subdivided ? (int)(Math.Min(dt, .5f) * _definition.StepsPerSecond) : 1;
        var oldHead = _positions[0];
        // Gold 787B18..787B56 treats a zero XY head as an unplaced system and
        // uses the current emitter as interpolation origin (absent in the demo).
        if (subdivided && oldHead.X == 0 && oldHead.Y == 0) oldHead = start;
        for (var iteration = 0; iteration < steps; iteration++)
        {
            // Native interpolation is endpoint-exclusive; a one-step streak call
            // retains its old head. Whip immediately adopts the current emitter.
            _positions[0] = subdivided ? oldHead + (start - oldHead) * ((float)iteration / steps) : start;
            var weight = _definition.DirectionWeight;
            for (var i = 1; i < _positions.Length; i++)
            {
                var delta = _positions[i] - _positions[i - 1] + _velocities[i] + direction * weight;
                if (delta.LengthSquared() < 0.000001f) delta = direction;
                var next = _positions[i - 1] + Vector3.Normalize(delta) * _definition.LinkLength;
                _velocities[i] = (next - _positions[i]) * _definition.Inertia;
                _positions[i] = next;
                weight -= _definition.DirectionWeightStep;
            }
        }
    }

    private void UpdateParticles(Vector3 start, Vector3 end, float dt)
    {
        var emission = _definition.Emission;
        var emissionSize = emission.Size + _definition.Intensity * _definition.EmissionSizeIntensityScale;
        for (var i = 0; i < _positions.Length; i++)
        {
            if (float.IsFinite(_ages[i]) && _sizes[i] > 0 &&
                255 + _ages[i] * _definition.Motion.FadeChangeRate > 0)
                AdvanceParticle(i, start, dt);
        }
        if (_positions.Length == 0) return;
        _accumulator += dt;
        while (emission.EmissionInterval > 0 && _accumulator >= emission.EmissionInterval)
        {
            _accumulator -= emission.EmissionInterval;
            var i = _nextParticle++ % _positions.Length;
            _ages[i] = 0;
            _sizes[i] = emissionSize + RandomSigned() * emission.SizeRandomWidth;
            _rotations[i] = emission.Rotation + RandomSigned() * emission.RotationRandomWidth;
            _angularVelocities[i] = emission.AngularVelocity + RandomSigned() * emission.AngularVelocityRandomWidth;
            _gravity[i] = emission.Gravity + RandomSigned() * emission.GravityRandomWidth;
            _velocities[i] = new Vector3(
                emission.Velocity.X + RandomSigned() * emission.VelocityRandomWidth.X,
                emission.Velocity.Y + RandomSigned() * emission.VelocityRandomWidth.Y,
                emission.Velocity.Z + RandomSigned() * emission.VelocityRandomWidth.Z);
            _positions[i] = Vector3.Lerp(start, end, _random.NextSingle()) + new Vector3(
                emission.PositionOffset.X + RandomSigned() * emission.PositionRandomWidth.X,
                emission.PositionOffset.Y + RandomSigned() * emission.PositionRandomWidth.Y,
                emission.PositionOffset.Z + RandomSigned() * emission.PositionRandomWidth.Z);
            AdvanceParticle(i, start, _accumulator, false);
        }
    }

    private void AdvanceParticle(int i, Vector3 emitter, float dt, bool applyPotential = true)
    {
        var motion = _definition.Motion;
        _positions[i] += _velocities[i] * dt;
        _velocities[i] += (motion.GravityDirection.Value - Vector3.UnitZ) * (_gravity[i] * dt);
        var displacement = _positions[i] - emitter;
        if (applyPotential && motion.InwardAcceleration != 0 && displacement.LengthSquared() > 1e-14f)
            _velocities[i] -= Vector3.Normalize(displacement) * (motion.InwardAcceleration * dt);
        _ages[i] += dt;
        _sizes[i] += (motion.SizeChangeRate + _definition.Intensity * _definition.SizeChangeIntensityScale) * dt;
        _rotations[i] += (_angularVelocities[i] + motion.AdditionalAngularVelocity) * dt;
        _gravity[i] += motion.GravityChangeRate * dt;
    }

    private void WriteQuad(Mesh mesh, int particle, Vector3 position, float halfSize, bool visible)
    {
        var size = visible ? halfSize : 0;
        ReadOnlySpan<Vector2> offsets = [new(-size, -size), new(size, -size), new(size, size), new(-size, size)];
        var rotation = (_definition.ParticleDrawFlags & 4) != 0 ? _rotations[particle] : 0;
        var (sin, cos) = MathF.SinCos(rotation);
        var side = Math.Max(1, _definition.ParticleAtlasSide);
        // stdRender (demo 68D404 / Gold function 761B00): energy selects an atlas cell,
        // with 256 rather than 255 as the denominator.
        var energyLost = float.IsFinite(_ages[particle]) ? -_ages[particle] * _definition.Motion.FadeChangeRate : 0;
        var frame = Math.Clamp((int)(energyLost * side * side / 256f), 0, side * side - 1);
        var cell = new Vector2(frame % side, frame / side);
        ReadOnlySpan<Vector2> uv = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];
        for (var corner = 0; corner < 4; corner++)
        {
            var index = _firstVertex + particle * 4 + corner;
            // Mesh order BL, BR, TR, TL; native triangle strip order BL, TL, BR, TR.
            ReadOnlySpan<int> nativeCorners = [0, 2, 3, 1];
            var marker = _definition.CornerColors.Count == 4
                ? (_definition.CornerColors[nativeCorners[corner]] & 0xFFFFFFu) + 1 : 1u;
            var offset = offsets[corner];
            mesh.Vertices[index] = mesh.Vertices[index] with
            {
                Position = position,
                Normal = new Vector3(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos, marker),
                TexCoord = (uv[corner] + cell) / side
            };
        }
    }

    private Vector4 ParticleColor(int particle)
    {
        if (_definition.ParticleColors.Count == 256 && float.IsFinite(_ages[particle]))
        {
            var energy = Math.Clamp((int)(255 + _ages[particle] * _definition.Motion.FadeChangeRate), 0, 255);
            return Unpack(_definition.ParticleColors[energy]);
        }

        return Unpack(_definition.ParticleColor | 0xFF000000u);
    }

    private Vector3 Transform(GrnAnimatedMesh? pose, Vector3 point) =>
        _bone is not null && pose is not null && pose.TryTransformRigidPoint(_bone, point, out var transformed)
            ? transformed
            : point;
    private float RandomSigned() => _random.NextSingle() * 2 - 1;
    internal static Vector4 Unpack(uint color) => new((color >> 16 & 255) / 255f,
        (color >> 8 & 255) / 255f, (color & 255) / 255f, (color >> 24) / 255f);
}
