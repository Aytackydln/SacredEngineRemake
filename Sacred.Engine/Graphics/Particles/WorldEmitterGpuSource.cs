using System;
using System.Linq;
using System.Runtime.InteropServices;
using Sacred.Particles;
using Sacred.World.Particles;

namespace Sacred.Engine.Graphics.Particles;

/// <summary>Validated in-memory emitter ABI; never an archive or disk format.</summary>
internal sealed class WorldEmitterGpuSource
{
    public const int HeaderBytes = 272, ParameterBytes = 144, ParticleBytes = 56;
    public const int ParameterStartBytes = 48;
    public WorldEmitterGpuSource(WorldGpuEmitter emitter)
    {
        Emitter = emitter;
        if (emitter.Definition.AdditionalDraws.Count > 0 || emitter.Definition.AdditionalHalos.Count > 0 ||
            emitter.Definition.RetireAfterDuration)
            throw new NotSupportedException("Layered native effects require their CPU draw/lifetime controller.");
        if (emitter.Definition.LineEmission is not null)
            throw new NotSupportedException("World GPU emission requires attachment inputs for a line effect.");
        if (emitter.Definition.OrbitEmission is not null || emitter.Definition.Teleport is not null)
            throw new NotSupportedException("This effect requires its custom native playback rather than the standard GPU birth controller.");
        if (emitter.Definition.BurstOnFirstUpdate || emitter.Definition.SkipFirstMovement)
            throw new NotSupportedException("First-update bursts require their native CPU birth controller.");
        Capacity = emitter.Definition.Capacity; SetCount = emitter.Parameters.Length;
        if (Capacity <= 0 || SetCount <= 0) throw new ArgumentException("Empty GPU emitter.");
        if (checked((long)Capacity * SetCount) > 8192)
            throw new NotSupportedException("GPU emitter exceeds the validated 8192-entry controller work limit.");
        if (emitter.Definition.EmissionMode == 2 && SetCount < 2)
            throw new ArgumentException("80/20 emission requires two parameter sets.");
        SetStride = checked(16 + Capacity * (8 + ParticleBytes));
        StateBytes = checked(HeaderBytes + SetStride * SetCount + (emitter.Definition.Halo is null ? 0 : ParticleBytes));
        var cycleOffset = checked(ParameterStartBytes + ParameterBytes * SetCount);
        InitialOffset = checked(cycleOffset + (emitter.Definition.EmissionCycle is null ? 0 : 48));
        Bytes = new byte[checked(InitialOffset + StateBytes)];
        var words = MemoryMarshal.Cast<byte, int>(Bytes.AsSpan());
        words[0] = Capacity; words[1] = SetCount; words[2] = emitter.Definition.EmissionMode;
        words[3] = emitter.Definition.EmitBeforeMovement ? 1 : 0;
        words[4] = (int)emitter.Definition.GroundCollision;
        words[5] = emitter.Definition.UsesWind ? 1 : 0;
        words[6] = BitConverter.SingleToInt32Bits(emitter.Projection.DefaultWind);
        var weight = emitter.Parameters.Sum(p => p.Emission.EmissionInterval > 0 ? 1 / p.Emission.EmissionInterval : 0);
        words[7] = BitConverter.SingleToInt32Bits(weight);
        words[8] = (int)emitter.Definition.EmissionClock;
        words[9] = BitConverter.SingleToInt32Bits(emitter.Definition.EmissionDurationSeconds ?? 0);
        words[10] = emitter.Definition.EmissionDurationSeconds.HasValue ? 1 : 0;
        for (var set = 0; set < SetCount; set++)
        {
            var p = emitter.Parameters[set]; var offset = ParameterStartBytes + set * ParameterBytes;
            var emission = p.Emission; var motion = p.Motion;
            MemoryMarshal.Write(Bytes.AsSpan(offset), in emission);
            MemoryMarshal.Write(Bytes.AsSpan(offset + 100), in motion);
            words[(offset + 132) / 4] = p.Index;
            words[(offset + 136) / 4] = emitter.Definition.SingleBirthInitialization ? 1 : 0;
            words[(offset + 140) / 4] = emitter.Definition.EmissionCycle is null ? 0 : cycleOffset;
            var e = p.Emission;
            // All arithmetic inputs must be finite before they reach GPU loops/addresses.
            foreach (var value in MemoryMarshal.Cast<byte, float>(Bytes.AsSpan(offset, 80)))
                if (!float.IsFinite(value)) throw new ArgumentException("Non-finite emission source.");
            foreach (var value in MemoryMarshal.Cast<byte, float>(Bytes.AsSpan(offset + 100, 32)))
                if (!float.IsFinite(value)) throw new ArgumentException("Non-finite motion source.");
            if (!float.IsFinite(e.EmissionInterval) || !float.IsFinite(weight)) throw new ArgumentException("Non-finite interval/weight.");
        }
        if (emitter.Definition.EmissionCycle is { } cycle)
        {
            float[] values = [cycle.InitialDelayMinimum, cycle.InitialDelayWidth, cycle.RepeatDelayMinimum,
                cycle.RepeatDelayWidth, cycle.RandomScale, cycle.BurstThreshold, cycle.QuietInterval,
                cycle.QuietVerticalVelocity, cycle.QuietFadeRate, cycle.BurstInterval, cycle.BurstVerticalVelocity,
                cycle.BurstFadeRate];
            if (values.Any(v => !float.IsFinite(v))) throw new ArgumentException("Non-finite cycle source.");
            MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(Bytes.AsSpan(cycleOffset));
        }
        var state = Bytes.AsSpan(InitialOffset);
        var initial = emitter.Initial;
        if (initial is not null && initial.Particles.Length != SetCount)
            throw new ArgumentException("Snapshot parameter set count mismatch.");
        if (initial is null && emitter.Definition.InitialParticles.Any(p => !emitter.Parameters.Any(s => s.Index == p.ParameterSetId)))
            throw new ArgumentException("Authored particle references an absent parameter set.");
        if (initial?.GpuState is { } raw)
        {
            if (raw.Length != StateBytes) throw new ArgumentException("GPU snapshot/source size mismatch.");
            Decode(raw); raw.AsSpan().CopyTo(state); return;
        }
        var rng = initial?.RandomState ?? new SeededParticleRandom(unchecked(emitter.Placement.ScriptOffset * 397 ^
            (int)emitter.Placement.Creation.TypeId)).Capture();
        new SeededParticleRandom(0).Restore(rng);
        rng.AsSpan().CopyTo(MemoryMarshal.Cast<byte, int>(state));
        Write(state, 232, BitConverter.SingleToInt32Bits(initial?.Elapsed ?? 0));
        Write(state, 236, initial?.NextDrawOrder ?? emitter.Definition.InitialParticles.Count);
        Write(state, 244, BitConverter.SingleToInt32Bits(initial?.Age ?? 0));
        Write(state, 248, BitConverter.SingleToInt32Bits(initial?.NativeReleaseTime ?? 0));
        Write(state, 252, initial?.NativeEmissionInitialized == true ? 1 : 0);
        if (emitter.Definition.EmissionCycle is { } controller)
        {
            var playback = new ParticleEmissionCyclePlayback(controller, unchecked((uint)(emitter.Placement.ScriptOffset * 397 ^
                (int)emitter.Placement.Creation.TypeId)));
            if (initial is not null) playback.Restore(initial.CycleCountdown, initial.CycleRandomState, initial.CycleBurst);
            Write(state, 256, BitConverter.SingleToInt32Bits(playback.Countdown));
            Write(state, 260, playback.Burst ? 1 : 0); Write(state, 264, unchecked((int)playback.RandomState));
        }
        var total = 0;
        for (var set = 0; set < SetCount; set++)
        {
            var particles = initial?.Particles[set] ?? emitter.Definition.InitialParticles
                .Select((p, order) => (p, order)).Where(v => v.p.ParameterSetId == emitter.Parameters[set].Index)
                .Select(v => new ParticleSimulationState { Position = v.p.Position.Value, Velocity = v.p.Velocity.Value,
                    Gravity = v.p.Mass, Size = v.p.Size, Fade = v.p.Energy, Rotation = v.p.Phi,
                    AngularVelocity = v.p.Moment, AtlasCell = v.p.Frame, DrawOrder = v.order, Color = v.p.Color }).ToArray();
            if (particles.Length > Capacity) throw new ArgumentException("Initial capacity exceeded.");
            var start = HeaderBytes + set * SetStride;
            Write(state, start, particles.Length); Write(state, start + 4, Capacity - particles.Length);
            for (var i = 0; i < Capacity; i++)
            {
                if (i < particles.Length)
                {
                    Write(state, start + 16 + i * 4, i);
                    MemoryMarshal.Write(state[(ParticleOffset(set) + i * ParticleBytes)..], in particles[i]);
                }
                else Write(state, start + 16 + Capacity * 4 + (Capacity - 1 - i) * 4, i);
            }
            total = checked(total + particles.Length);
        }
        if (total > Capacity) throw new ArgumentException("Shared initial capacity exceeded.");
        Write(state, 240, total);
        if (emitter.Definition.Halo is { } halo)
        {
            var particle = new ParticleSimulationState { Size = halo.HalfSize, Fade = 255, DrawOrder = -1 };
            MemoryMarshal.Write(state[(HeaderBytes + SetStride * SetCount)..], in particle);
        }
    }
    private static void Write(Span<byte> bytes, int offset, int value) => MemoryMarshal.Write(bytes[offset..], in value);
    public WorldGpuEmitter Emitter { get; }
    public byte[] Bytes { get; }
    public int Capacity { get; }
    public int SetCount { get; }
    public int SetStride { get; }
    public int StateBytes { get; }
    public int InitialOffset { get; }
    public int ParticleOffset(int set) => checked(HeaderBytes + set * SetStride + 16 + Capacity * 8);
    public WorldParticleEmissionSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != StateBytes) throw new ArgumentException("Emitter snapshot size mismatch.");
        var rng = MemoryMarshal.Cast<byte, int>(bytes[..232]).ToArray();
        new SeededParticleRandom(0).Restore(rng);
        var particles = new ParticleSimulationState[SetCount][];
        var total = 0;
        for (var set = 0; set < SetCount; set++)
        {
            var start = HeaderBytes + set * SetStride; var count = MemoryMarshal.Read<int>(bytes[start..]);
            if (count < 0 || count > Capacity) throw new InvalidOperationException("Invalid GPU live count.");
            var free = MemoryMarshal.Read<int>(bytes[(start + 4)..]);
            if (free < 0 || free > Capacity || free + count != Capacity) throw new InvalidOperationException("Invalid GPU free count.");
            var occupied = new bool[Capacity];
            for (var i = 0; i < free; i++)
            {
                var slot = MemoryMarshal.Read<int>(bytes[(start + 16 + Capacity * 4 + i * 4)..]);
                if (slot < 0 || slot >= Capacity || occupied[slot]) throw new InvalidOperationException("Invalid GPU free ordering.");
                occupied[slot] = true;
            }
            particles[set] = new ParticleSimulationState[count];
            for (var i = 0; i < count; i++)
            {
                var slot = MemoryMarshal.Read<int>(bytes[(start + 16 + i * 4)..]);
                if (slot < 0 || slot >= Capacity || occupied[slot]) throw new InvalidOperationException("Invalid GPU live slot.");
                occupied[slot] = true;
                particles[set][i] = MemoryMarshal.Read<ParticleSimulationState>(bytes[(ParticleOffset(set) + slot * ParticleBytes)..]);
            }
            total += count;
        }
        if (total > Capacity || total != MemoryMarshal.Read<int>(bytes[240..])) throw new InvalidOperationException("Invalid GPU shared count.");
        return new(rng, BitConverter.Int32BitsToSingle(MemoryMarshal.Read<int>(bytes[232..])),
            MemoryMarshal.Read<int>(bytes[236..]), particles) { GpuState = bytes.ToArray(),
                Age = MemoryMarshal.Read<float>(bytes[244..]), NativeReleaseTime = MemoryMarshal.Read<float>(bytes[248..]),
                NativeEmissionInitialized = MemoryMarshal.Read<int>(bytes[252..]) != 0, MovementReady = true,
                CycleCountdown = MemoryMarshal.Read<float>(bytes[256..]), CycleBurst = MemoryMarshal.Read<int>(bytes[260..]) != 0,
                CycleRandomState = MemoryMarshal.Read<uint>(bytes[264..]) };
    }
}
