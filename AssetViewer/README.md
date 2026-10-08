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
  Selecting a playable character automatically chooses its equipment class filter
  from its resolved creature template; both Vampiress forms use Vampiress. Other
  NPCs use unrestricted mode. You can override the filter until selecting another
  character. Select a slot, search for an
  equippable item, and equip or remove it. The animation selector lists the model's
  authored clips from `Models.tmp`, plus default animation and bind pose. Clips
  loop and can be paused or resumed. Attached wearable and hand meshes and their
  effects follow the animated skeleton. The character list collapses after selection;
  **Change character** reopens it. Each selected character keeps its own loadout and
  allocations.
  Actor framing uses the base model alone. Equipping, replacing or removing items keeps
  the actor's center, scale, camera rotation and zoom; attachment meshes and effects do
  not expand the frame.
  Changing the class filter preserves equipped instances. NPC mode
  allows all classes. The PoC offers a common set of slots for model inspection;
  rings attach to the finger selected by their slot, and amulets follow their authored
  wearable skeleton. Ring slots 1/3 use the left hand and 2/4 use the right hand;
  empty slots preserve this mapping. See [native accessory placement](../docs/research/accessory-placement.md).
  **Stats**, **Skills**, **Bonuses** and **Allocate** show Creature.pak base values,
  level growth, learned ranks, allocations and equipment contributions. Equipping
  creates an independent item instance with authored values. Resolved native item
  records can be imported by Sacred.Inventory; random drop generation comes later.
  Selecting an equipped slot or a candidate item shows its 3D model beside its localized
  description beneath the equipment list. It uses the game's fixed inventory placement:
  authored preview rotation, scale, offset and occupied cell dimensions from `Weapon.pak`,
  with the inventory projection and model textures. Generated equipment effects are hidden
  in this item preview. Item rotation and zoom controls are
  disabled. Equipped descriptions use the concrete instance's values. Picking comes later.
  **Allocate** displays all six attributes together, with base at the current level,
  editable added points, equipment bonus and total. Every acquired skill and combat art
  has an editable base rank, equipment bonus, total and remove button. Use the selection
  boxes to add available skills or rune-backed combat arts at rank 1. Equipment cannot
  acquire a missing skill or art. The combat-art pool follows the selected hero template,
  independently of the equipment class filter. **Stats** separates attack and protection.
  Combat arts show their native colored icons in **Skills**, **Allocate** and the add-art
  selector. Colors come from the game's UI atlases: ordinary special moves are green,
  spells blue, and vampire-form arts red; knight-form arts retain green icons.
  New viewer actors receive the native single-player starting arts at base level 1,
  including both Vampiress transformations. Allocations persist when changing characters.
  Hand candidates and equip validation share the native Gold slot rules: shields,
  bows, crossbows and muskets use the left hand; ordinary melee weapons use the right.
  Equipment roles come from the resolved `Items.pak` family. A shield with a zero
  `Weapon.pak` subtype displays as Shield and uses the shield attachment placement.
  Combat values currently show direct contributions before native skill curves,
  percentage effects and opponent-dependent calculations. See the
  [research and continuation plan](../docs/research/inventory-character-stats.md).

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
inventory
inventory-slot <index>
inventory-item <equipment ID from filtered candidates>
item-description
screenshot-item <path.png>
inventory-clear
stats
allocation
stats-view <stats|skills|bonuses|allocate>
level <level>
attribute <Strength|Endurance|Dexterity|PhysicalRegeneration|MentalRegeneration|Charisma> <allocated points>
skill <Creature.pak skill ID> <learned rank>
combat-art <available combat-art code> <learned rank; 0 removes>
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
`screenshot-item` captures the selected item's newly rendered inventory frame in Characters.
`rotate` continues to rotate the character.
The original project/assembly name and settings location remain compatible with
existing Rider run configurations.

`gc` forces a full collection for testing native callback and window lifetimes.
`animations` lists the selected character's clips and motion slots.
`animation-time` pauses the character and seeks to a looping clip time for captures.
