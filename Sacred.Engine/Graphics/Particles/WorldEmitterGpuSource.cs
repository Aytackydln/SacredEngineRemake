using System;
using System.Linq;
using System.Runtime.InteropServices;
using Sacred.Particles;
using Sacred.World.Particles;

namespace Sacred.Engine.Graphics.Particles;

/// <summary>Validated in-memory emitter ABI; never an archive or disk format.</summary>
internal sealed class WorldEmitterGpuSource
{
    public const int HeaderBytes = 256, ParameterBytes = 144, ParticleBytes = 52;
    public WorldEmitterGpuSource(WorldGpuEmitter emitter)
    {
        Emitter = emitter;
        Capacity = emitter.Definition.Capacity; SetCount = emitter.Parameters.Length;
        if (Capacity <= 0 || SetCount <= 0) throw new ArgumentException("Empty GPU emitter.");
        if (checked((long)Capacity * SetCount) > 8192)
            throw new NotSupportedException("GPU emitter exceeds the validated 8192-entry controller work limit.");
        if (emitter.Definition.EmissionMode == 2 && SetCount < 2)
            throw new ArgumentException("80/20 emission requires two parameter sets.");
        SetStride = checked(16 + Capacity * (8 + ParticleBytes));
        StateBytes = checked(HeaderBytes + SetStride * SetCount + (emitter.Definition.Halo is null ? 0 : ParticleBytes));
        InitialOffset = checked(32 + ParameterBytes * SetCount);
        Bytes = new byte[checked(InitialOffset + StateBytes)];
        var words = MemoryMarshal.Cast<byte, int>(Bytes.AsSpan());
        words[0] = Capacity; words[1] = SetCount; words[2] = emitter.Definition.EmissionMode;
        words[3] = emitter.Definition.EmitBeforeMovement ? 1 : 0;
        words[4] = (int)emitter.Definition.GroundCollision;
        words[5] = emitter.Definition.UsesWind ? 1 : 0;
        words[6] = BitConverter.SingleToInt32Bits(emitter.Projection.DefaultWind);
        var weight = emitter.Parameters.Sum(p => p.Emission.EmissionInterval > 0 ? 1 / p.Emission.EmissionInterval : 0);
        words[7] = BitConverter.SingleToInt32Bits(weight);
        for (var set = 0; set < SetCount; set++)
        {
            var p = emitter.Parameters[set]; var offset = 32 + set * ParameterBytes;
            var emission = p.Emission; var motion = p.Motion;
            MemoryMarshal.Write(Bytes.AsSpan(offset), in emission);
            MemoryMarshal.Write(Bytes.AsSpan(offset + 100), in motion);
            words[(offset + 132) / 4] = p.Index;
            var e = p.Emission;
            // All arithmetic inputs must be finite before they reach GPU loops/addresses.
            foreach (var value in MemoryMarshal.Cast<byte, float>(Bytes.AsSpan(offset, 80)))
                if (!float.IsFinite(value)) throw new ArgumentException("Non-finite emission source.");
            foreach (var value in MemoryMarshal.Cast<byte, float>(Bytes.AsSpan(offset + 100, 32)))
                if (!float.IsFinite(value)) throw new ArgumentException("Non-finite motion source.");
            if (!float.IsFinite(e.EmissionInterval) || !float.IsFinite(weight)) throw new ArgumentException("Non-finite interval/weight.");
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
        var total = 0;
        for (var set = 0; set < SetCount; set++)
        {
            var particles = initial?.Particles[set] ?? emitter.Definition.InitialParticles
                .Select((p, order) => (p, order)).Where(v => v.p.ParameterSetId == emitter.Parameters[set].Index)
                .Select(v => new ParticleSimulationState { Position = v.p.Position.Value, Velocity = v.p.Velocity.Value,
                    Gravity = v.p.Mass, Size = v.p.Size, Fade = v.p.Energy, Rotation = v.p.Phi,
                    AngularVelocity = v.p.Moment, AtlasCell = v.p.Frame, DrawOrder = v.order }).ToArray();
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
            MemoryMarshal.Read<int>(bytes[236..]), particles) { GpuState = bytes.ToArray() };
    }
}
