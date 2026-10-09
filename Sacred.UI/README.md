# Sacred.UI

Shared presentation for Sacred's original interface, usable by the Engine,
AssetViewer, item generator and combat simulator described in
[GameSystems.md](../GameSystems.md). This is a library, not a new application.

The bottom-center HUD is implemented in `Sacred.UI.Hud`: original panel artwork,
inventory/options/map/quest-book/collect buttons, and the rotating day/night dial.
The Engine hosts its DX12 drawing and pointer input. Only the map action is wired;
the remaining buttons show their native up/down states. HUD clicks capture the
pointer before world movement, with release-inside activation and drag cancellation.

## Existing code moved here

| Namespace | Type | Existing behavior |
| --- | --- | --- |
| `Sacred.UI.CombatArts` | `CharacterCombatArtIconSelectors` | Demo/Gold enabled combat-art atlas name and cell selection |
| `Sacred.UI.CombatArts` | `CharacterCombatArtIcon` | Atlas name and cell origin |
| `Sacred.UI.Character` | `CharacterStatLabels` | Resource-backed attribute, skill and art labels; diagnostic fallback and number formatting |
| `Sacred.UI.Inventory` | `SacredEquipmentDescriptionText` | Plain-text presentation of existing equipment-description sections |

These helpers retain their behavior and names. AssetViewer references this
assembly. The selectors reproduce existing native art-code dispatch; their
integers are combat-art codes, not archive texture/item IDs. A later executable
table reader can replace this combat-art transcription, including all enabled,
recharging and empty states. The HUD already reads its atlas crops from the
executable; the combat-art dispatch still uses the existing selectors.

The Avalonia panes, bitmap lifetime/cropping, sound player, DX12 model viewport and
debug controls remain with their hosts. Character stats, acquisitions, equipment
rules and effects remain in Sacred.Inventory. Game file layouts remain in
Sacred.Core and archive decoding remains in Sacred.Assets. Sacred.UI currently
depends on Core, Assets and Inventory, with no GUI framework dependency.

## Bottom HUD integration

`BottomHudAssets.LoadAsync` reads the selected game's native `sUITextureDef` array
from Sacred.exe (or pureHD.exe when Sacred.exe is absent) and resolves the original
atlas names through the host's Texture.pak loader. No research exports are runtime
inputs. `Sacred.Assets.Executable.SacredUiTextureTable` locates the complete table by
content; its 84-byte layout and anchor enum are in `Sacred.Core.UI`, outside the
generated executable-layout directory.

`BottomHudLayout` preserves the native 1024x768 proportions and anchors the center
panel to the output's bottom edge. `DayNightDial` reproduces calendar rotation and
horizontal scaling. The Engine binds its existing lighting clock as midnight=0,
noon=0.5, with continuous dusk/dawn; cave ambient overrides leave the clock intact.
Drawing uses original textures at output resolution, independently of world render
resolution, and shares the existing SDR/HDR UI composition.

Console diagnostics: `set hud status`, `set hud map`, `set lighting day`,
`set lighting night`, `set lighting cycle`, and `screenshot <label>`.

## Escape menu integration

`EscapeMenuState` owns modal selection, pointer press/release, keyboard traversal
and quit confirmation. `EscapeMenuLayout` preserves the native 264x200 rectangle
and 36-pixel row pitch, centered at output resolution. `EscapeMenuAssets` detects
Demo's four entries versus Gold/pureHD's five using the native Export command,
reads that build's frame definitions, and resolves `GUI_main_12.TGA` from its
archives. `UiDialogFrame` retains reflected crops and tiles the original border.

The Engine opens it with Escape or `HudButton.Options`, captures modal input,
cancels movement/interactions and pauses gameplay/lighting updates. Escape and
Continue resume; Quit opens a localized Yes/No confirmation and confirmed Quit
closes the host application. Options, Save and Export remain disabled until their
screens and domain operations are implemented. Debug windows do not receive
input through the menu. Rendering uses the installed Carolingia font and the
existing SDR/HDR output composition.

Console controls: `set hud options`, `set menu open`, `set menu escape`,
`set menu up`, `set menu down`, `set menu activate`, `set menu continue`,
`set menu quit`, `set menu cancelquit`, `set menu status` and `screenshot <label>`.
See [native evidence and verification](../docs/research/ui-components/escape-menu.md).

## World map UI integration

`WorldMapUiAssets` reads the installation's native map-control definitions and
loads the original taskbar/frame atlases. `WorldMapUiLayout` preserves the Demo
and Gold control positions, and output scaling independent of map zoom.
`WorldMapUiViewport` anchors the frame to all viewport edges, tiles the original
edge art to fill widescreen layouts, and keeps the corners and bottom-center
controls proportional. The map aperture has narrow padding inside the ornament.
The shared `UiDialogFrame` accepts the reflowed map inset as well as the
Escape menu inset. `WorldMapUiState` provides press/release cancellation and a
Close event; waypoint activation is disabled.

Engine draws the map mask/frame and bottom strip, consumes their pointer input
before panning/targeting, and returns to gameplay when Close is activated.
Map settings remain available through console cheats; the diagnostic panel is
hidden by default and can be shown with `set map-panel on`. `set map-ui close`
uses the same action as the Close button, and `set map-ui status` reports readiness.
See [native layout and validation](../docs/research/ui-components/world-map-ui.md).

## Research and future work

- [Verified binary findings and evidence](../docs/research/ui-components/README.md)
- [Component inventory and behavior](../docs/research/ui-components/components.md)
- [Implementation and migration plan](../docs/research/ui-components/implementation-plan.md)
- [OpenHeilig and SacredBild findings](../docs/research/ui-components/external-projects.md)

Future components should consume the selected game's archives/resources and
version-qualified executable definitions. Preserve original names, authored
rectangles, native input ordering and existing byte meanings. Keep rendering and
platform integration behind host adapters; do not add a custom UI asset format.
