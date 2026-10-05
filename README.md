# Sacred 1 Engine Remake Project

## Point of the project

- Give insight into future projects into remaking the Sacred 1 engine or other tools,
even if this project is not finished
- Provide various tools to see game data, such as item stats, item affixes, and item sets
- Bring attention for current owner of the Sacred IP to consider remaking/remastering Sacred 1
- Waste AI processing power?

## Solution Structure

### Sacred.Core
Contains file structure of the game and classes that compose them for usability.

The generated [game-file format coverage report](docs/game-file-formats.md) lists mapped
files, multi-section structures, known fields, and unresolved byte ranges.

### Sacred.Core.Analyzer
Roslyn-based source analyzer and Markdown report generator for the `StructLayout` types
in `Sacred.Core`. See [its usage guide](Sacred.Core.Analyzer/README.md).

### Sacred.Assets
Load game files like Pak, Bin asynchronously while limiting to 1 load per file.

### Sacred.Inventory
In-game item and related logic, including graphics and inventory management.

### Sacred.Granny
3D model and related data loader independant of libraries.

`IGrnAssetLoader` supports both the managed Granny 1 parser and the in-process
64-bit `granny_x64.dll` backend. The native backend runs in the x64 ItemViewer and
DX12 engine process and uses pointer-width interop. ItemViewer exposes the choice
in its filter toolbar. The engine reads and writes
`GRANNY_BACKEND : ManagedParser|GrannyDll` in `SacredEngineRemake.cfg`; in terminal
mode, `set granny managed|native` changes the saved choice for the next launch.

The DLL backend currently supplies geometry, indices, UVs, and render surfaces for
standalone models. Character composition and editable animation tracks deliberately
fall back to the managed implementation until the Granny 1 skeleton/animation API is
mapped.

### Sacred.World
Logic about game world. Compositing, paths, world scripts etc.

### Sacred.World.Renderer.Terminal
Terminal project that outputs images of game world, map and minimap.

Generate one minimap texture or all sectors from the original game world files:

```powershell
.\Sacred.World.Renderer.Terminal.exe --game 'D:\SteamLibrary\steamapps\common\Sacred Gold' --sector-x 52 --sector-y 38 --resolution x2 --output '.\generated-minimap-x2'
.\Sacred.World.Renderer.Terminal.exe --game 'D:\SteamLibrary\steamapps\common\Sacred Gold' --all-sectors --renderer gpu --supersampling x8 --output '.\generated-minimap' --texture-cache-mb 256
```

| Argument | Default | Description |
| --- | --- | --- |
| `--game <directory>` | Sacred Gold Steam installation on `E:` if present | Game installation folder; also accepted as the first positional argument. |
| `--campaign <name or path>` | `NetScriptCamp` | Script subdirectory under `bin`, or an absolute script directory. |
| `--output <directory>` | `./world-debug-images` | Folder for exported images. |
| `--format <tga, bmp>` | `tga` | Image format. |
| `--renderer <software, gpu, auto>` | `software` | Rendering backend. GPU requires Windows; `auto` currently selects software. |
| `--sector-x <integer>` | — | Sector grid X coordinate; requires `--sector-y`. |
| `--sector-y <integer>` | — | Sector grid Y coordinate; requires `--sector-x`. |
| `--all-sectors` | Off | Export all sectors with an original minimap texture; cannot be combined with a single sector. |
| `--resolution <x1, x2, x4, x8, x16>` | `x1` | Scale sector image dimensions. Currently, `x16` uses the same scale as `x8`. |
| `--supersampling <x1, x2, x4, x8, x16>` | `x2` | Smooth sector image edges; `x1` disables smoothing. |
| `--skip-existing` | Off | Skip existing output files; requires `--all-sectors`. |
| `--world-x <number>` | Start-sector center | View center X coordinate; cannot be used with sector exports. |
| `--world-y <number>` | Start-sector center | View center Y coordinate; cannot be used with sector exports. |
| `--width <pixels>` | `1280` | View image width; cannot be used with sector exports. |
| `--height <pixels>` | `720` | View image height; cannot be used with sector exports. |
| `--zoom <number>` | `0.75` | View camera zoom; must be positive and cannot be used with sector exports. |
| `--open-doors` | Off | Render doors in their open position. |
| `--indoor-level <1–255>` | — | Select the indoor floor at the view center; cannot be used with sector exports. |
| `--particle-seconds <0–60>` | `2` | Particle simulation warm-up in seconds. |
| `--texture-cache-mb <integer>` | `256` | Texture cache size in MiB; `0` disables caching. |
| `--verbose` | Off | Show detailed loading and rendering diagnostics. |
| `--help` | — | Show command-line help. |

### Sacred.Shaders
DX12 shaders for all graphics projects about the game.

### Sacred.Engine
Research on displaying game world and characters using modern graphics pipeline (DX12 with proton support)

### SacredItemSimulator
Research project to simulate item generation and affix generation in Sacred 1.
Currently the work is about "Item Behaviors" like inventory space, stackability, and item sets.
Intended to be a simulation of the loot system of Sacred 1.

### SacredItemSimulator.Avalonia
Avalonia UI project to visualize the item simulation of SacredItemSimulator.
