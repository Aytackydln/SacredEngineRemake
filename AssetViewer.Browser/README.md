# Sacred Asset Viewer for WebAssembly

The browser host reuses AssetViewer's Avalonia controls, catalogues, texture
resolution, managed Granny decoding, skeletal animation, equipment effects and
world particle simulation. All model and effect previews render through WebGPU.
The Windows `AssetViewer` project continues to use DX12.

## Run

Use the .NET 10 SDK with the WebAssembly workload:

```powershell
dotnet workload install wasm-tools
dotnet run --project AssetViewer.Browser -- --urls http://localhost:5280
```

Open that address in a browser with WebGPU enabled. Choose **Open Sacred
installation…** and select the game folder containing `pak`, `bin` and `scripts`.
Models, Characters, Equipment, Effects, Textures and Drops use the original
viewer controls. Character animations, equipment attachments, effect variants,
strength/distance inputs, pause, restart and native effect controllers remain
available. The browser uses the managed Granny backend; the desktop retains its
native backend choices and inventory audio.

## File access

`Files/BrowserInstallation.cs` adapts a user-selected installation to the shared
C# readers. `BrowserFileStream` is a seekable, asynchronous stream backed by
`File.slice().arrayBuffer()`. It keeps at most 1 KiB of read-ahead per
archive for ordinary reads. Larger reads transfer the selected payload directly into the caller's
managed buffer, without a second managed payload array. Offsets are passed as
exact JavaScript numbers and are not narrowed to signed 32-bit integers.

Models.pak and every Texture*.pak stay as original browser File objects. They
are never copied into MEMFS, uploaded, converted to another archive format or
read as whole files. Existing C# archive indexers read descriptor tables and
small entry headers; models and textures decode only when requested. Indexing
uses the optional `IBatchedReadStream` scatter-read interface, with at most 256
ranges and 64 KiB of output per batch. For native browser Files, a compact Blob
composes only those [header slices](https://w3c.github.io/FileAPI/#slice-method-algo) into one asynchronous read, without fetching
payload gaps. Header order, names and formats are still decoded in shared C#.
Range-backed providers use up to 16 concurrent reads, coalescing gaps of at most
4 KiB into reads of at most 64 KiB. Ordinary desktop streams keep their original
sequential read path. The browser logs model and texture indexing times;
`(await import('./files.js')).metrics` reports I/O counts and bytes.

Small catalogues, BIN files and executable metadata are buffered as whole files
(up to 64 MiB per metadata file), rather than streamed through their parsers. Original
metadata needed by path-based UI/drop helpers is mounted in a temporary Wasm
directory and removed when the installation is replaced. No game files are
modified. Browser settings currently live for the page session.

## Texture retention and frame pacing

The browser session retains up to 128 MiB of decoded textures in
`ViewerTextureCache`. Aliases share loads, simultaneous requests share a decode,
and only four distinct payload loads run at once. Canceling a preview releases its
wait; a pending load is canceled when nobody still needs it. Failed loads can be
retried. The cache evicts the least recently used decoded pixels and is cleared
when the installation changes. Entry IDs and archive-qualified selections retain
their original lookup precedence. Desktop texture loading bypasses this cache so
unpacked texture edits remain visible.

WebGPU has a separate 128 MiB device texture cache shared by viewports and
equipment slots. Material aliases and animation variants reuse the same pixel
identity. Switching models or reopening a viewport releases bindings without
destroying cached uploads. Only idle textures can be evicted; visible textures
remain pinned even if a preview exceeds that budget. Frame uploads reuse scratch
buffers, and static model vertices are uploaded once.

There is no application FPS limit or 16 ms render timer. Preview frames use
[`requestAnimationFrame`](https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animation-frames)
to follow the browser's display refresh scheduling. Hidden-tab throttling remains
under browser control. Detaching a viewport cancels its pending callback.

`await sacred.Command('texture-cache')` reports decoded cache bytes, hits,
misses and entries. `(await import('./webgpu.js')).diagnostics` reports texture
uploads, GPU cache usage, active bindings and frame-loop counts. Texture completion
logs separate loading/decoding time from bitmap creation time.

Character animation samples clips and evaluates regular/rigid bone palettes in
the existing C# Granny code. WebGPU deforms the vertex buffer using a compute
shader generated from the desktop's `SacredSkinning.hlsl`; bind vertices,
influences and projection use the desktop's linked `GpuSkinSourceData` packing.
Only bone palettes change per pose. Effect simulation and attachment queries
continue to use the C# pose, and radial material queries evaluate only the
required surface vertices. An empty item-description preview redraws only when
its contents or size change.

`await sacred.Command('animation-perf on')` enables the shared animation counters;
`await sacred.Command('animation-perf')` captures/resets them. Use
`animation-perf off` to disable measurement. Pause a character with
`animation-time <seconds>`, then run `skinning-validate` to compare every GPU
vertex with the shared C# skinning reference. This readback is a diagnostic,
rather than part of normal rendering.

## Rendering and shared sources

- `Startup/` owns browser startup, navigation, installation selection and console
  commands. Browser pages host the existing desktop window content.
- `AssetViewer.Shared/AssetViewer.Shared.projitems` links the original UI and
  preview sources; there is no second implementation of asset decoding,
  animation, FX simulation or effect-family dispatch.
- `Rendering/` implements the shared viewport and offscreen equipment-rendering
  interfaces using a WebGPU canvas hosted by Avalonia's NativeControlHost.
- `wwwroot/webgpu.js` owns GPU devices, buffers, textures, pipelines, draw
  submission, readback and resource destruction. It supports opaque, straight
  alpha, premultiplied and additive passes, line strips, billboards, animated
  textures, secondary glow, overlays and ground grids. Unsupported WebGPU is an
  explicit error; previews do not fall back to WebGL or software drawing.
- Shared material ordering, flags, alpha classification and camera conventions
  are used by both DX12 and WebGPU. The browser does not reference Sacred.Shaders
  or Vortice; it links the platform-independent shader constant definitions.

`AssetViewer.WebGpu.Generator` is a Roslyn incremental source generator. Normal
builds compile the desktop's original HLSL into all 16 WGSL stages and emit C#
sources into the browser assembly. Startup registers those sources with WebGPU;
there are no checked-in WGSL files, shader fetches, or regeneration scripts.
HLSL inputs are declared as `AdditionalFiles`, so edits rebuild the affected
programs. Shared include edits rebuild every dependent program, and line endings
are normalized before comparison. Compiler failures are build errors (`WGSL001`).

NuGet restores the pinned `SlangDxcBundle.Toolchain` 2026.14.1 compiler for the
build host, independently of the browser target. Windows x64, Linux x64/ARM64,
and macOS x64/ARM64 toolchains are included by that package; no separate Vulkan
SDK or Slang installation is needed. The compiler is a private build dependency
and is not shipped to the browser.

The generator adapts the DX12 descriptor-heap glow lookup to a fixed WebGPU
binding and permits the original post-discard texture sampling. Preview textures
have only mip zero. Edit the shared HLSL rather than generated C# output.
The skinning adapter reads the desktop's packed vertices through byte-addressed
storage, preserving their 48-byte stride despite WGSL's different vec3 alignment.
Slang's emitted HLSL `precise` keyword is removed because WGSL has no equivalent;
the vertex-reference and image checks cover the resulting numerical tolerance.

## Publish

```powershell
dotnet publish AssetViewer.Browser -c Release
```

Serve `AssetViewer.Browser/bin/Release/net10.0-browser/publish/wwwroot` as static
files over HTTPS or localhost, with `.wasm` served as `application/wasm`. No
application server, cross-origin isolation or Wasm threads are required. Avalonia's
DataGrid package currently emits an upstream IL2104 trimming warning; the
published viewer is covered by the browser checks below.

## Console cheats and screenshots

Use the browser developer console; commands do not focus windows or synthesize
keyboard/pointer input:

```javascript
await sacred.Command('model 3073');
await sacred.Command('character 1');
await sacred.Command('animation 28');
await sacred.Command('animation-time 0.1');
await sacred.Command('equip 1 3073');
await sacred.Command('fx 773');
await sacred.Command('fx-time 0.1');
await sacred.Command('screenshot sacred-preview.png');
```

Other commands include `view`, `item`, `texture <archive> <entry>`, `class`,
`unequip`, `effects`, `animation-play`, `fx-variant`, `fx-play`, `fx-restart`,
`fx-strength`, `fx-distance`, `fx-hit`, `fx-release`, `fx-step`, `fx-cell`,
`drop`, `drop-replay` and `drop-clear`. `fx-cases` lists every currently supported
preview variant; decoded controller bindings with no existing draw path retain
their shared viewer diagnostic. `capture-size <width> <height>` changes the
screenshot backing size for renderer comparisons.

The screenshot cheat renders a fresh GPU frame and downloads PNG without focus.
Texture screenshots download the decoded texture. GPU readback for equipment
slots is asynchronous and preserves the original transparent item placement.

## Validation

```powershell
dotnet test AssetViewer.Tests
dotnet test AssetViewer.WebGpu.Generator.Tests
dotnet publish AssetViewer.Browser -c Release
npm ci --prefix AssetViewer.Browser.Tests
npm test --prefix AssetViewer.Browser.Tests
# File adapter and indexing checks without the full FX catalogue:
npm run test:indexing --prefix AssetViewer.Browser.Tests
npm run test:performance --prefix AssetViewer.Browser.Tests
npm run test:characters --prefix AssetViewer.Browser.Tests
```

Set `SACRED_GAME_DIRECTORY` for a different installation and
`SACRED_CHROME_PATH` for another Chromium executable. The browser checks use the
published output by default; `SACRED_BROWSER_WWWROOT` can select another published
directory. Logs, screenshots and `report.json` are written to
`AssetViewer.Browser.Tests/TestResults`.
If Chrome is unavailable, set `SACRED_CHROME_PATH` or install Playwright Chromium
with `npm exec --prefix AssetViewer.Browser.Tests -- playwright install chromium`.

Portable C# tests cover sparse multi-gigabyte reads, exact offsets, cancellation,
bounded buffering, material passes and camera fitting. Installed-data tests
compare decoded models/animations and texture formats/expansion ownership through
async-only streams against desktop loading. Browser checks exercise actual
WebGPU, every supported FX preview, animation seek/pause/resume, equipment-slot
readback, textures, drops, cleanup and rejection without WebGPU. They also test
real File/Blob range and composite reads. Indexing checks use native Blob slices
containing original descriptors/headers at their original sparse offsets, keeping
fixture memory bounded without copying large archives. Set
`SACRED_RANGE_INDEX_FIXTURES=1` to measure the HTTP provider and smoke-test model,
animation, texture and effect loading after indexing. These timing reports
identify their fixture type; native header fixtures do not measure physical
disk throughput. Large preview fixtures use HTTP range-backed
File-shaped objects through the production adapter to avoid automating a picker
or copying entire archives into the test browser.

Source-generator tests compile the real HLSL through the restored Slang compiler,
validate every emitted stage and its generated C#, check incremental dependency
invalidation and line-ending normalization, and verify build errors for invalid
HLSL, missing shared inputs, and missing compilers.

Performance checks verify that revisiting a decoded texture performs no archive
read and that model/viewport switches perform no repeated texture upload. They
include a 1024×1024 atlas, cache eviction, shared-load cancellation, pinned GPU
resources, and 144 Hz frame callbacks without a fixed throttle. Timings are saved
in `TestResults/performance.json`; frame scheduling tests do not claim that the
physical display or the WebAssembly renderer sustains 144 FPS.

Character checks separate playback, paused and equipped animation costs, assert
that normal frames do no CPU vertex skinning/body upload, and compare complete
GPU vertex buffers with the C# reference at multiple times. Synthetic GPU cases
cover rigid and weighted transforms, more than four influences, invalid weights
and indices, empty influences, projection axes and normal fallbacks. Detailed
measurements are saved in `TestResults/characters.json`.

Optional DX12 comparisons use `SACRED_DESKTOP_BASELINES`, containing
`desktop-model.png` (686×514, `model 3073`) and `desktop-character.png` (525×805,
`character 1`, `animation 28`, `animation-time 0.1`), captured with the desktop
screenshot cheat. The reference installation run matched the model exactly and
the character within one channel value in one pixel.
