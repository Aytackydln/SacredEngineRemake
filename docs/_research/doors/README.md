# Door investigation, 2026-09-24

Start here for follow-up prompts. Geometry, facing and playback now live in Sacred.World; Sacred.Engine consumes them. The terminal renderer produces the comparisons without driving a window.

## Evidence and status

Input directory: `C:\Users\Aytac\Pictures\Screenshots\Sacred\Doors`.

- [evidence.json](evidence.json): script positions, separate tile anchors, item descriptors, model bounds/origins, activation/deactivation tracks and endpoint bounds for 15 nearby placements / 11 distinct models.
- [verification.json](verification.json): actual-asset playback checks. All 11 models move, preserve source meshes, hold endpoints, and restore the same open state in a fresh instance. Maximum bind-to-first-key drift is 0.686 model units; maximum open/close joining drift is 0.419; maximum first-open-key to last-close-key drift is 1.408. These small differences are present in authored clip keys; do not replace them with guessed angles.
- [alignment.json](alignment.json): screenshot-to-terminal affine registration, inlier counts and residuals. Registration uses terrain/static sprites, not rendered door models. Median terrain residuals are 0.39–0.64 terminal pixels; these are **not door error measurements**.
- [comparisons/](comparisons/): registered same-scale crops with the original, terminal output,
  and the terminal model footprint projected in magenta onto the original. The footprint view
  exposes wall and column occlusion errors independently of lighting and texture filtering.
  Bellevue also includes both terminal states because the supplied reference has the blacksmith
  open and other doors closed.

**Exact pixel equivalence is not established.** Closed-door dimensions/facing and the main open motions now agree substantially better with the references. Remaining visible differences include the narrow exposed edge of the open Waldburg gate, some dungeon leaf/wall intersections, lighting, filtering and sprite occlusion boundaries. The software mask reproduces tile ordering, not all Direct3D per-vertex depth and transparency behavior. Do not describe terrain registration residuals as proof of pixel-perfect door placement. The latest Engine changes compile but were not captured again after the user requested terminal comparisons.

## Screenshot catalogue

| Reference | Objects identified | Motion |
|---|---|---|
| `1715 3397.png`, `1713 3396 open.png` | 5748 `Door_Waldburg01`, also nearby 5650 `Door_Hof01` | Waldburg slides along an animated dummy; its open body disappears behind the tower |
| `3050 2725.png` | 5740 `Door_Gate01` | Hinged iron gate |
| `3378 2522.png` | 5699 `Door_DarkCity01`, 5656 `Door_Hof03` | Tower and house hinges |
| `3425 2583.png` | 5672 `Door_Pub01`, 5653 `Door_Hof02`, 5650 `Door_Hof01`, 5673 `Door_Schmied01`; nearby 5733 and 5655 | House hinges; blacksmith has two sliding leaves and stationary frame meshes |
| `4555 990.png`, `4557 989 open.png` | 5753/5755 `Door_Dwarf01`, 5760/5762 `Door_Dwarf02` | Paired hinged leaves; foreground walls hide much of one open leaf |

IDs are research labels, not implementation conditions. All screenshot doors have activation and deactivation clips. None requires a per-item 90-degree fallback. All have `SwitchType == 0`; that byte does not explain their different motions.

## Placement and projection

Compiled creation commands can contain both integer tile coordinates and a precise world-coordinate operand. Keep both. Divide the world operand by the native `53.66563034057617` units/tile for the visual position. Do not round it and do not derive model positions by inverting static-sprite screen coordinates. Integer tile coordinates remain useful for sector ownership and painter ordering.

Examples:

- Waldburg: tile anchor `(1711,3403)`, visual position `(1714.4307,3403.7056)`.
- Blacksmith: tile anchor `(3423,2595)`, visual position `(3423.7556,2593.0935)`.
- Dwarf02 5760: tile anchor `(4578,987)`, visual position `(4578.7593,988.88245)`.

The mesh extractor centers geometry. Restore `SourceOriginOffset` **before** model scale, facing and camera compensation. Otherwise off-center hinges and door bottoms move.

Targeted Demo disassembly established:

- `cEngine::initMatrices` at `0x57B0F0`: camera basis from eye/target.
- Startup initializer at `0x6ED400`: eye `(0,1200,600)`, target zero. Ground foreshortening is `1/sqrt(5)` and vertical projection is `2/sqrt(5)`.
- `cWorldView::initProjectionMtx` at `0x596500`: orthographic bounds +/-267 horizontally and +/-200 vertically for the 1024x768 reference view.
- `cObject3D::setRelFacing(float)` at `0x42F940` and vector overload at `0x42F790`: facing is transformed through the world/view basis, not used directly as a mesh Z angle.
- `cWorldPosition` transformation routines are indexed in `docs/research/demo-symbols`.

`WorldModelPose` implements scale `768/400 = 1.92`, facing `atan2(sin(-a)*sqrt(5), cos(-a))`, and post-rotation compensation `( (1024/534)/1.92, sqrt(2/5), sqrt(8/5) )` for the remake's 45-degree model camera. These are global native projection values, not fits per door. The original uniform scale 2 and uncompensated camera distorted both height and facing.

Painter ordering must use the authored tile anchor separately from the precise mesh pivot. Applying the character's depth bias to world props put panels behind their own building fronts. Engine world props now omit that bias. Static sprite masking also matters: a raw model overlay made the Waldburg gate look too tall and made the dungeon's otherwise hidden second leaves appear to be duplicates.

The native `cWorldView0::showWorld` loop submits two interleaved `drawLine`
sequences in 48-pixel steps. Its static `cViewObject` lists preserve the WLDX
map-Y traversal index; map X and Static.pak chain order resolve equal rows. The
pavilion/bridge overlap at 3230,2547 distinguishes this from an X+Y sort: the
pavilion column is on Y 2546 and must cover the bridge rail on Y 2545, although
the bridge has the larger X+Y sum. Terminal registration against the original
screenshot has a 0.40-pixel median terrain residual and reproduces that overlap.

Script-created 3D world models remain tied to their authored isometric tile
anchor (X+Y). Their local model depth occupies a bounded interval around that
slot. Opaque static sprites paint in the map-Y order above with an always-pass
depth state, but write their X+Y slot for later model occlusion. This preserves
both ordering domains without a type-specific correction.

World models occupy one authored painter slot from their integer tile anchor. The
fractional world operand remains the visual pivot; using it for depth puts the
Waldburg gate in front of both wall layers. DX12 therefore bounds world-model
geometry inside that single slot with a monotonic rational mapping. A hard clamp
made many vertices share exactly the same depth and let triangle submission order
turn barrels and parts of chests inside out. The bounded mapping preserves their
face order. Characters and attachments retain their original projected
per-vertex depth. This places the gate between the two wall tiles without an
item-specific rule.

## Animation and byte mappings

| Data | Offset / identifier | Meaning |
|---|---|---|
| Items model descriptor | `0x57`, float32 | `Angle3D`, facing in projected-world degrees |
| Model metadata / Models.tmp record | `0x70 + 4*0xAA = 0x318`, uint32 | Activation motion-table index |
| Model metadata / Models.tmp record | `0x70 + 4*0xAB = 0x31C`, uint32 | Deactivation motion-table index |
| GRN form-mesh descriptor | `0xCA5E0C03`, payload +0 uint32 | One-based source mesh index, owning its child form-bone bindings |
| GRN form-bone descriptor | `0xCA5E0C0A`, payload +0 uint32 | Skeleton bone slot; one explicit binding also attaches an unweighted mesh |

Activation/deactivation aliases are mapped in both Core model metadata layout structs. The form-bone prefix is mapped in `Granny1FormBoneLayout` and used by the readers. Remaining form-bone bytes are not assigned speculative meanings here.

The old loader guessed `SourceMeshIndex + 1`. That fails when dummy bones and stationary frame meshes intervene. Blacksmith form meshes bind to slots 2,3,4,5, not 1,2,3,4. Bind by the form record.

Rigid **form geometry** uses `inverse(full bind world) * full animated world`. Do not decompose away scale. Hof doors contain a reflection (negative scale with a 180-degree quaternion); decomposition can choose a different reflection axis as the hinge rotates, flipping the panel. Explicitly attached character equipment retains its separate rigid attachment path.

`DoorMotionPlayback` uses named bone hierarchy tracks for both translation and rotation. The old "last moving translation track" fallback is removed. `GrnAnimatedMesh.ApplyClamped` samples the exact endpoint without looping. Frame-rate-independent updates hold that endpoint. Engine retains requested state when sectors stream out and applies it as soon as the model loads again; mutable animation instances do not modify cached source meshes.

## Reproduce

From the repository root:

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run --project docs/_research/doors/tools
& docs/_research/doors/tools/Render-Comparisons.ps1
py docs/_research/doors/tools/compare.py
& 'C:\Users\Aytac\.dotnet\dotnet.exe' build Sacred.Engine --no-restore
```

The evidence program accepts game-directory and evidence-output path as its first two arguments, with optional `--bones` afterward. The comparison script needs NumPy and OpenCV, uses the supplied screenshot folder, and writes retained PNG crops. Full BMP frames/logs are under `_scratch/doors-<location>-<state>`.

For one location:

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run --project Sacred.World.Renderer.Terminal -- `
  --world-x 3425 --world-y 2583 --width 1600 --height 900 --zoom 0.5 `
  --open-doors --output _scratch/doors-bellevue-open
```

## Follow-up work

Script-created 3D doors and containers use the compiled placement Z operand as
their indoor surface level. Zero normally maps to level one, but Items.pak category
`Door` at Z zero is an unscoped entrance portal, as demonstrated by Bellevue's
doors whose anchors lie inside level-one grids. Floor-scoped tile anchors are
tested against authored cells and omitted outside or on a nonmatching floor.
Static.pak-backed models continue to use
the alternate-surface flag and visibility state at `0x2B`. Terminal checks at
`2528,2084` selected the building at origin `2521,2080`: exterior, level 1 and
level 2 rendered 49, 51 and 49 model placements respectively, while the two
interior models appeared only on level 1.

Use the aligned crops to resolve residual occlusion/edge differences before claiming exact matching. Investigate native clipping and depth rules globally; do not add corrections keyed on item/texture IDs. The open Waldburg reference may also represent a nonfinal animation instant; confirm before changing its authored endpoint. Preserve the distinction between visual coordinates and tile ordering. References do not establish initial saved-game switch states; `--open-doors` intentionally supplies a comparison state, not a save-game interpreter.
