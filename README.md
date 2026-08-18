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

### Sacred.Assets
Load game files like Pak, Bin asynchronously while limiting to 1 load per file.

### Sacred.Inventory
In-game item and related logic, including graphics and inventory management.

### Sacred.Granny
3D model and related data loader independant of libraries.

`IGrnAssetLoader` supports both the managed Granny 1 parser and the original game's
32-bit `Granny.dll`. The native implementation runs in the bundled x86
`Sacred.Granny.Native.Worker` process so the x64 ItemViewer and DX12 engine remain
64-bit. ItemViewer exposes the choice in its filter toolbar. The engine reads and
writes `GRANNY_BACKEND : ManagedParser|GrannyDll` in `SacredEngineRemake.cfg`; in
terminal mode, `set granny managed|native` changes the saved choice for the next
launch.

The DLL backend currently supplies geometry, indices, UVs, and render surfaces for
standalone models. Character composition and editable animation tracks deliberately
fall back to the managed implementation until the Granny 1 skeleton/animation API is
mapped.

### Sacred.World
Logic about game world. Compositing, paths, world scripts etc.

### Sacred.World.Renderer.Terminal
Terminal project that outputs images of game world, map and minimap.

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
