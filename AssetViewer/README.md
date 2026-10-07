# Sacred Asset Viewer

Run `Sacred.ItemViewer.Avalonia` on Windows. The launcher automatically loads the
last successful game installation, using the existing ItemViewer settings so
favorites and saved equipment filters remain available. Choose another installation
with **Browse** or the location field and **Load game**. `--game-directory <path>`
overrides the remembered location for startup.

The launcher opens four independent windows:

- **Models**: the complete `Items.pak` table, searchable by row, resource ID, model
  or category, with 3D previews, authored model effects and descriptor field offsets/raw bytes.
- **Equipment**: the existing `Weapon.pak` equipment browser with item information,
  filters, favorites, preview experiments, sounds and effects.
- **Textures**: every entry from `texture*.pak`, with archive and row identity,
  storage format, dimensions and offsets. Preview RGBA, RGB or alpha, zoom or fit,
  and change the background. The preview decodes the selected packed entry itself.
- **Characters**: every `Items.pak` creature row, including NPCs and player models.
  Choose an inventory layout/class restriction, select a slot, search for an
  equippable item, and equip or remove it. The animation selector lists the model's
  authored clips from `Models.tmp`, plus default animation and bind pose. Clips
  loop and can be paused or resumed. Attached wearable and hand meshes and their
  effects follow the animated skeleton. Class layouts use `Sacred.Inventory`;
  NPC mode allows all classes. Selecting a character or changing the layout clears
  its loadout. Jewelry can occupy slots without adding a model.

Changing the game location closes open viewers after the new installation loads
successfully. A failed load leaves the previous installation available. Browsers
use pages of 100 rows; equipment retains its existing paging and filters.

In Models and Characters, drag with the left mouse button to rotate horizontally.
The rotation slider stays in sync; Reset camera restores the starting orientation.

Console cheats are available without desktop input simulation:

```text
open models|equipment|textures|characters
model <Items.pak row>
item <equipment ID>
texture <archive.pak> <entry ID>
character <Items.pak row>
class <SacredCharacterClass name|npc>
animations
animation <bind|default|motion slot|GRN name>
animation-play <true|false>
animation-time <seconds>
rotate <horizontal degrees>
equip <inventory slot index> <equipment ID>
unequip <inventory slot index>
screenshot <path.png>
screenshot-ui <path.png>
location <game installation path>
close <view name>
gc
quit
```

`screenshot` captures a newly rendered 3D frame, or the selected texture/channel
at native resolution. `screenshot-ui` captures the Avalonia controls of the current
window (the native 3D viewport is excluded). Both work without focusing the window.
The original project/assembly name and settings location remain compatible with
existing Rider run configurations.

`gc` forces a full collection for testing native callback and window lifetimes.
`animations` lists the selected character's clips and motion slots.
`animation-time` pauses the character and seeks to a looping clip time for captures.
