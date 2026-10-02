# Particle simulation execution

World particles default to `CpuSimd`. Settings exposes CPU (SIMD) and CPU
(scalar), persisted as `PARTICLE_SIMULATION` in the existing remake config.
Console commands are `set particle-simulation CpuSimd` and
`set particle-simulation CpuScalar`. Switching preserves live state and random
emission sequences. Hardware without SIMD uses the scalar update automatically.
The terminal world's CPU and DX12 renderers both consume the shared simulator.

Each emitter stores a fixed-capacity structure-of-arrays batch per parameter
set. `Vector<float>` lanes advance different particles. Spawns and incomplete
vectors use the scalar integrator. Removal compacts survivors in one stable
pass. Inverse-interval emission weights are computed once per emitter.
No allocations occur during batch integration or compaction.

The current renderer needs live positions on the CPU for isometric projection,
viewport culling, painter sorting and interleaving particles with world objects.
A compute-only GPU simulation would need GPU-to-CPU readback before these
operations. An effective GPU backend needs GPU projection, culling and depth
ordering together with GPU-resident state; it is not implemented here. The
existing elemental-weapon vertex shader continues its GPU animation.

## Native ground movement

Verified Gold code hash:
`ee60108ce8147721717df1632c47b89c2b445a0255922219fa14a5980feadf37`.

`stdMovementWithGround` at `0x7645C0` reads emitter Z at base-system `+0x10`
and `heightAboveGround` at `+0xA4`. These are mapped in
`SacredParticleGroundLayout`. The Demo declaration also names
`heightAboveGround`. These are memory offsets; the enclosing base-system save
record is still unknown.

After integrating position and gravity acceleration, `0x7646AE..0x7646DA`
tests `particle.Z <= emitter.Z - heightAboveGround`. Movement mode 2 clears
energy. Other modes multiply vertical velocity by the float at `0x89094C`,
which is exactly -1. Position is not clamped, and there is no damping or
descending-velocity guard. Fade/size/mass/additional rotation then advance.
This routine does not apply inward acceleration.

`ParticleGroundCollision` represents None, Bounce and Die. Collision belongs
to the native effect definition, independently of the scalar/SIMD setting.
The decoded smoke and dwarf-magic update paths call `stdMovement` at
`0x7640C0`, not the ground routine. They retain None. Unsupported native
families remain unsupported; no item or texture exceptions enable collision.

Both integrators preserve separate angular-velocity and additional-rotation
updates. The native inward-force normalization threshold at `0x890D70` is
length `1e-7`, replacing the earlier length-squared `1e-6` approximation.

## Verification

`native_simulation_reference.py` executes the original routines using Unicorn
and a version-checked decoded memory image. The checked-in reference contains
48 steps across ordinary motion, bounce and death, including below/equal/above
ground and a zero-time update.

```powershell
py docs/research/particle-definitions/native_simulation_reference.py `
  --memory-image _scratch/particle-simulation-memory.bin `
  --output docs/research/particle-definitions/reports/particle-simulation-reference.json
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run -c Release `
  --project docs/research/particle-definitions/ParticleSimulationChecks -- `
  docs/research/particle-definitions/reports/particle-simulation-reference.json --benchmark
```

Checks compare scalar and SIMD against native motion, exercise vector tails,
empty batches, zero-length inward force, stable removal/metadata and all 14
decoded definitions across all quality levels for 150 frames, switching a live
emitter to scalar halfway through. On the development machine, 1,209,300
checks passed with eight SIMD lanes; 1,202,404 passed with hardware intrinsics
disabled and four fallback lanes. The counts depend on vector width.

Release microbenchmark medians on this machine (five samples after warmup):

| Particles per batch | SIMD ns/particle/update | Scalar ns/particle/update |
| --- | ---: | ---: |
| 100 | 2.35 | 8.62 |
| 200 | 1.28 | 8.77 |
| 8192 | 1.28 | 10.92 |

These timings measure integration and compaction with constant motion, not
complete emission, projection, sorting or rendering. They establish the SIMD
kernel benefit, not an end-to-end frame-rate gain. The benchmark is opt-in.
