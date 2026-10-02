# Sacred world image renderer

This headless program reads the original Sacred game files. It has three modes:
coordinate/zoom debug views, single-sector minimap generation, and all-sector minimap
generation. Images default to **TGA**; pass `--format bmp` for BMP.

## GPU rendering

The default is `--renderer software`: measured sector exports complete faster end
to end with CPU rendering. `--renderer auto` currently makes the same selection.
Use `--renderer gpu` on Windows to explicitly select Direct3D 12 rendering;
initialization and rendering errors are reported. Both paths run offscreen without
a window, focus changes or simulated input. Software remains available on other platforms.

```powershell
dotnet run -c Release --project Sacred.World.Renderer.Terminal -- --sector-x 52 --sector-y 38 --renderer gpu --resolution x4 --output '.\generated-minimap-gpu'
dotnet run -c Release --project Sacred.World.Renderer.Terminal -- --all-sectors --renderer gpu --output '.\generated-minimap-gpu' --texture-cache-mb 256
```

Terrain composition, liquids, sprites, models, lighting and particles use the
same GPU passes as the game renderer. Exports wait for asset preparation and GPU
uploads before reading back the finished frame. Supersampling applies to the
whole frame; GPU mode applies an alpha-weighted area filter on the GPU before
reading back only the final output dimensions. TGA/BMP encoding uses the CPU.
FSR is an upscaler; supersampled minimaps use area reduction instead. The DX12 device
and decoded texture cache persist across a batch. Derived asset and GPU caches
are released every eight sectors to bound memory and descriptor usage.
Offscreen preparation uploads up to 64 textures per kind per submission; the
interactive engine retains its one-texture-per-frame upload limit. Animated water
and material shaders sample `--particle-seconds`, so their phase does not depend
on loading speed or wall-clock time.

GPU asset preparation can cost more than software rendering. In the local Release
comparison, a 1024-square town export took 714 ms cold / 256 ms prepared on software
and 1591 ms cold / 18 ms prepared on GPU. Nine adjacent sectors at that resolution,
including the eight-sector cache release, took 2.42 seconds on software and 5.03
seconds on GPU. These timings are specific to the tested machine and scene.
GPU lighting, shadows and transparency can
produce different pixels from the software backend. Use separate output directories
when changing backend or quality, especially with `--skip-existing`.

TGA exports use a bottom-left origin and bottom-up pixel rows. Sacred Gold's
loose-TGA loader reverses rows unconditionally instead of honoring the origin flag;
this storage order gives the correct orientation in both Sacred and standard image
editors. Regenerate older top-down exports without `--skip-existing` to correct
their in-game inversion.

Run these examples from the repository root. `--game <directory>` also accepts the
installation path; without one, the tool uses the existing default Sacred Gold
Steam installation on `E:` if present.

## Coordinate and zoom views

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run -c Release --project Sacred.World.Renderer.Terminal -- `
  'E:\SteamLibrary\steamapps\common\Sacred Gold' `
  --output '.\world-debug-images' `
  --world-x 3360 --world-y 2464 --width 1280 --height 720 --zoom 0.75
```

This writes `map.tga` (the authored Ancaria map), `minimap.tga` (the existing
minimap lattice), `world-day.tga` (terrain, liquid/floor layers and static sprites),
and `world-models.tga` (the daytime view with GRN models and simulated particles,
using the selected backend). `world-day` remains a software diagnostic view.
Map and world views retain their position markers. Omitted coordinates use the
world archive's start-sector center.

Use `--open-doors` to sample the final pose of each door's authored activation
sequence. Use `--indoor-level <n>` with coordinates inside an authored indoor grid
to select that floor's state. Exterior output omits indoor models.

## Single-sector minimap generation

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run -c Release --project Sacred.World.Renderer.Terminal -- `
  'E:\SteamLibrary\steamapps\common\Sacred Gold' `
  --sector-x 52 --sector-y 38 --output '.\generated-minimap'
```

Sector arguments are **grid coordinates**, each sector containing 64×64 world
tiles. This writes `MINIMAP052038.tga`, freshly rendered from the world data.
The original packed `MINIMAPxxxYYY.TGA` record supplies the output width and height.
Framing uses the minimap lattice's right-edge sector-center anchor: the view center
is `((sectorX + 0.25) * 64, (sectorY + 0.75) * 64)`, with a horizontal span of
3072 native isometric pixels. Neighboring sectors supply scenery crossing crop
boundaries. Generation includes terrain, liquids, static sprites, models and
particles, and removes the debug position marker. Rectangular originals retain
their aspect ratio when the rendered frame is reduced to the output dimensions.

### Resolution and smoothing

`--resolution x1|x2|x4` multiplies both packed texture dimensions while preserving
the world crop. For an original 256×256 texture:

| Resolution | Output | Internal render with default smoothing |
| --- | --- | --- |
| `x1` (default) | 256×256 | 512×512 |
| `x2` | 512×512 | 1024×1024 |
| `x4` | 1024×1024 | 2048×2048 |

```powershell
dotnet run -c Release --project Sacred.World.Renderer.Terminal -- --sector-x 52 --sector-y 38 --resolution x2 --output '.\generated-minimap-x2'
dotnet run -c Release --project Sacred.World.Renderer.Terminal -- --all-sectors --resolution x4 --output '.\generated-minimap-x4'
```

Sector images default to `--supersampling x2`: terrain, sprites, models and particles
are rendered at twice the requested output dimensions, then downscaled with an
area filter. Each output pixel averages the source pixels it covers, with alpha
weighted colors to avoid fringes at transparent edges. `--supersampling x4` improves
smoothing further; `--supersampling x1` disables supersampling for faster generation.
Doubling either scale roughly quadruples the internal pixel count, so higher
settings use more time and memory. These two options apply to sector modes.

Software integer reductions use a specialized area filter; large images distribute
filter rows across up to eight CPU workers. Pixel averaging, alpha handling and
rounding match the general filter. Software sector exports reuse their final image buffer,
and model/particle passes share a per-frame static-sprite occlusion mask. Empty
overlays avoid unnecessary work, and off-screen model triangles are discarded
before rasterization. Coordinate mode retains a separate terrain frame for
`world-day`, so that image is preserved.

Unpacked texture overrides remain available for rendering scenery; they do not
change the output dimensions taken from the packed original minimap. Sector modes
derive their camera from that original, so coordinate, viewport, zoom and indoor
options cannot be combined with them. A nonexistent sector or missing original
texture causes single-sector mode to fail.

## All sectors

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run -c Release --project Sacred.World.Renderer.Terminal -- `
  'E:\SteamLibrary\steamapps\common\Sacred Gold' `
  --all-sectors --output '.\generated-minimap' --texture-cache-mb 256
```

This enumerates `sectors.keyx` in grid order and generates sectors with an original
packed minimap record, including cave sectors. Missing original records are counted
in the final summary and skipped. Individual rendering failures are logged while the batch continues;
the final exit code is nonzero if any sector failed.

Use `--skip-existing` to resume a batch. It skips any existing destination filename,
so use a directory containing completed exports at the same resolution and smoothing
settings. Use separate directories when changing these options. Each new image is written to a
temporary file and renamed after completion. The console reports batch progress
at most every ten seconds and a final summary. Add `--verbose` for shared-library
loading messages, per-sector exports, missing texture names, model/particle
diagnostics and decoded-texture cache hits/misses. Errors remain visible by default;
`--verbose` also includes full exception details for a failed run.
The terminal session's LRU texture cache defaults to 256 MiB;
sector, composed-sprite and model caches are released every eight rendered sectors.
The texture budget does not include meshes, composed sprites or image buffers.
Pass `--texture-cache-mb 0` to disable decoded-image retention.
`TexturePakArchive` performs uncached reads. The cache is implemented in this
terminal project and supplied only to its renderers; the normal engine does not
receive this additional RAM cache. Cached decoded data is released when the
export session is disposed.

`--particle-seconds <n>` controls simulated warm-up (default 2, range 0–60).
Emitters advance at 60 fixed simulation steps per second without a real-time wait;
zero disables warm-up. The seeded emitter simulation makes repeated exports
reproducible. Particle atlases, animation cells, rotation, tint, fade and blending
use the shared world particle definitions.

Software lighting, model transparency and particle/model occlusion differ from
the engine's GPU rendering; static sprite occlusion is applied to the software
particle overlay. Both backends open no window.

Use `--help` for all arguments. Exit codes: 0 success, 1 loading/rendering failure,
2 invalid arguments.
