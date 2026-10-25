# Sacred Asset Viewer

Run `Sacred.ItemViewer.Avalonia` on Windows. The launcher automatically loads the
last successful game installation, using the existing ItemViewer settings so
favorites and saved equipment filters remain available. Choose another installation
with **Browse** or the location field and **Load game**. `--game-directory <path>`
overrides the remembered location for startup.

The launcher opens five independent windows:

- **Models**: the `Items.pak` table excluding Effect-category rows, searchable by row, resource ID, model
  or category, with 3D previews, authored model effects and descriptor field offsets/raw bytes.
- **Effects - Items.pak**: Effect-category rows, with native type names, recipe status,
  event variants and descriptor offsets/raw bytes. FX identifiers use the native type ID
  associated with the Items.pak row; several different recipes share the same descriptor
  name. The viewer plays the 16 decoded script particle effects and 74 decoded event samples from
  the embedded Gold catalog through the shared world particle simulator. Actor-seeded
  events display a randomly selected playable Creature.pak character; **Random character**
  picks another random template. Continuous effects fill before camera fitting. Finite
  bursts finish their native lifetime and replay for inspection. **Restart** and **Pause/Play**
  control preview playback. Unmapped recipes show their diagnostic instead of requesting
  an FX name from the GRN archive. Other native FX families remain research work.
  **Deflection / FX_PARADE** now renders its ten untextured, five-vertex native
  line strips. The viewer previously counted forty proxy segments but skipped
  every draw because the effect has no texture. It now binds the selected actor's
  decoded neck/root attachment and preserves native vertex RGB/alpha, outward
  motion and finite lifetime. Camera fitting covers the complete trajectory.
  `fx 773`, `fx-time 0`, `fx-time 0.1` and `fx-time 0.22` inspect birth and travel;
  `fx-time 0.3` shows expiry. `fx-hit` retriggers while paused, and `fx-actor`
  chooses another playable model. Both event variants 0 and 10 are supported.
  [Birth](../docs/game-server/evidence/deflection-viewer-birth.png),
  [travel](../docs/game-server/evidence/deflection-viewer-after.png) and
  [pixel checks across nine actors](../docs/game-server/evidence/deflection-viewer-checks.json)
  were captured through AssetViewer console cheats, without desktop input.
  Both **Stargate** recipes use the native 20x34 mesh, animated vertex colours,
  five-second minimap transitions and two rotating whirls. Generic event subtype 6
  scales its actor burst using the selected character's Items.pak blocking radius.
  **Implemented** filters the 107 rows with previews and combines with search.
  Grayscale RGBA FX textures preserve their authored RGB brightness; the shared
  classifier correction restores radial ground patterns and reveals expanding rings.
  See [corrected captures](../docs/research/particle-definitions/expanding-burst-fx.md#grayscale-texture-correction).
  **Ring of Poison and Kamikaze** add two event previews: a complete 150-slot
  particle burst, fifty expanding ring strips and two rotating ground flares.
  Saved bytes, three resources, packed colours and native timer boundaries are
  preserved. Kamikaze uses an explicit standalone event radius rate of 150.
  Current coverage is **140 recipes across 107 FX rows**.
  See [research and captures](../docs/research/particle-definitions/expanding-burst-fx.md).
  **Bubble Trap Explosion and Terror Sprinter** add three event previews across
  two constructors, including both Terror Sprinter modes. All fifty expanding
  ring strips, twenty independently textured runes and rotating ground glow
  preserve the native saved state, phase boundaries and blend factors.
  That batch brought coverage to **138 recipes across 105 FX rows**.
  See [research and captures](../docs/research/particle-definitions/ring-surface-fx.md).
  **War Cry Sign** adds complete swept fins, ground glow and repeated highlights.
  See [research and captures](../docs/research/particle-definitions/war-cry-sign-fx.md).
  **Transdim, Pong Ball and Zhadum** add complete event previews: fifteen
  randomized ground passes, Pong hit flares / persistent glow, and triggered Zhadum
  hold/fade. `fx-hit` triggers Pong Ball or Zhadum; `fx-step <seconds>` advances while
  paused. That batch brought coverage to **134 recipes across 102 FX rows**. BFG's ground glow
  also has corrected surface tint. See [research and captures](../docs/research/particle-definitions/pulse-flare-fx.md).
  **Panzer and Stahl** add two complete event previews, each with 71 rotating
  ground planes and native growth, hold, fade, changing RGB and height/alpha falloff.
  The standalone duration is explicitly ten seconds. That batch brought coverage to **131 recipes**.
  See [research and captures](../docs/research/particle-definitions/column-flare-fx.md).
  **BFG and Trap02 Pill/Ghost** add three complete event previews: BFG's
  three animated billboard layers and ground glow, both asymmetric trap size pulses
  and the native Pill removal flag. `fx-hit` removes BFG/Pill immediately, including
  while paused. That batch brought coverage to **129 recipes**. See [research and captures](../docs/research/particle-definitions/sprite-flare-fx.md).
  **Live Undead, Call of Death and Trap01** add three complete event previews:
  four revival layers, fractional death-layer carry, native toggle colours and all
  sixteen trap atlas cells. `fx-hit` toggles Trap01; `fx-cell <0..15>` selects its
  sampled cell. That batch brought coverage to **126 recipes**. See [research and captures](../docs/research/particle-definitions/controlled-ground-flares.md).
  **Fire Circle and Healing** add all four native flag samples: Fire02/Fire09,
  red/pink and blue Healing. Native Fire Circle cutoff/draining and all 400 radial
  Healing slots, including zero-time reseeding, are preserved. That batch brought coverage to
  **123 recipes**. See [research and captures](../docs/research/particle-definitions/fire-circle-and-healing-fx.md).
  **Superfluid and Confusion** add all eight Superfluid strike states and
  Confusion's two rotating planes and two complete helical ribbons. Native phase
  reset/alpha carry and exponential ground-height reset remain deterministic when
  seeking/restarting. That batch brought coverage to **119 recipes**. Owner/audio/light integration,
  world placement and Terminal surfaces remain pending. See [research and captures](../docs/research/particle-definitions/superfluid-and-confusion-fx.md).
  **Shielded, Holy Shield and Magic Shield** add both Shielded colors and
  complete circular-shield previews: Holy's rotating brightness trail/ground symbol
  and Magic's thirty markers, growth/hold/shrink and two independent height pulses.
  `fx-hit` triggers native ring-shield impact; `fx-step` advances paused decay.
  Coverage was **117 recipes** at that checkpoint. Owner/audio/light integration, world placement and
  Terminal surfaces remain pending. See [research and captures](../docs/research/particle-definitions/shield-fx.md).
  **Crusader Strength and Speed Up** render all eight strike texture states,
  the native vertical alpha gradient and both glow passes. Native strict retirement,
  seeded seek/restart and saved timers are decoded. Coverage was **113 recipes** at that checkpoint;
  world placement, light/finish notifications and Terminal surfaces remain pending.
  See [research and captures](../docs/research/particle-definitions/strike-symbol-fx.md).
  **Mana Regeneration, HP Regeneration and MagLite** render their native
  layered ground flares. Mana/HP have rotating symbols and five glow planes;
  MagLite has 21 circular slices. Timers retire all layers; `fx-extend <timer units>`
  extends or revives Mana without resetting rotation.
  Actor following, audio, MagLite owner/light integration and world surfaces remain
  pending. See [research and captures](../docs/research/particle-definitions/regeneration-and-light-fx.md).
  Both **Anducar Spikes** event rows render their randomized authored models,
  native delay/grow/hold/shrink and high-quality additive glow. Distance controls
  timing; `fx-distance 0` supplies coincident endpoints. Camera fitting includes
  full growth, and seek/restart retain the same random geometry. Coverage is
  **108 recipes** at that checkpoint. World shadows/lighting parity and mesh scene integration remain
  pending. See [research and captures](../docs/research/particle-definitions/anducar-fx.md).
  Both **Pong Batter** variants render their scrolling box and twelve glow-sprite
  edges. `fx-hit` triggers native impact; `fx-step` advances recovery while paused.
  Coverage was **106 recipes** at that checkpoint; minigame control and world mesh integration remain
  pending. See [research and captures](../docs/research/particle-definitions/pong-batter-fx.md).
  All four **Underworld Glyph** members render two scrolling model texture
  passes, twenty curved ribbons and a glow. Restart and seek retain deterministic
  randomized wave parameters. Coverage was **104 recipes** at that checkpoint; world mesh integration
  remains pending. See [research and captures](../docs/research/particle-definitions/glyph-fx.md).
  All five **Forge Element** members render their model, two ground flares and
  scrolling cylindrical strip. **Release** or `fx-release` starts native
  compression/fade; `fx-step <seconds>` advances paused playback without resetting.
  Coverage was **100 recipes** at that checkpoint. World mesh integration and Air's subsequent object
  dispatch remain pending. See [Forge research and captures](../docs/research/particle-definitions/forge-element-fx.md).
  All seven **Element Stone** members render their authored GRN model, three
  rotating beams, flicker strip and two flares. Coverage was 95 recipes at that checkpoint.
  These direct-mesh previews use shared native geometry; world scene placement
  and Terminal mesh rendering remain pending. See
  [research and captures](../docs/research/particle-definitions/element-stone-fx.md).
  Cloud, both Fire Skin owner modes, Ground Poison, terminal Freeze Impact,
  three Gladiator cast branches, Soulcatcher, Burning Light and Songs from Hell
  added eight families and eleven recipes, bringing coverage to 88 recipes at that checkpoint.
  Native delayed starts, random bursts, glow envelopes and ground lensflares are
  implemented. See [research and captures](../docs/research/particle-definitions/aura-and-cast-fx.md).
  Bleed, all three Battle Fog palettes, ordinary Gladiator hit and both High Speed
  selectors are implemented. High Speed preserves native ground geometry,
  birth-size growth and retirement. See [research and captures](../docs/research/particle-definitions/sustained-and-burst-fx.md).
  Seraphim hit includes both native combat-art branches. Blood Puddle uses the
  native ground-plane draw, timed release and fading palette. These additions
  brought coverage to 70 recipes at that checkpoint; see
  [hit and ground FX](../docs/research/particle-definitions/hit-and-ground-fx.md).
  Family membership follows native constructors, and **FamilyCoverage** counts all
  members (Smoke is 13/13). Windstrike and Burning Bone now
  use native finite emission clocks and per-particle colour. Burning Bone displays
  a random playable actor; individual bone attachment remains pending.
  Nature Healing, Time Control and Small Fire now have complete native visual
  paths, including both healing passes, 20 flares and all three Small Fire subtypes.
  Each new family covers 1/1 member rows. See
  [layered FX decoding](../docs/research/particle-definitions/layered-fx.md).
  Teleport IN/OUT share a newly decoded manual orbit/ejection controller and cover
  both family members (2/2). Reanimate covers its sole member with rotating emission,
  saved point-release interpolation and the native 0.125 draw scale.
  See [Teleport and Reanimate](../docs/research/particle-definitions/teleport-and-reanimate-fx.md).
  The previous eight constructors provide fourteen event recipes, including Arac Attack,
  Laser/Dwarf impacts, Elve SM Hit, Dragon Ground Fire's three textures, Wall of Fire Line,
  Bat Signal's three actor palettes and Shaddar Magic's timed projectile. Native burst order,
  the fire quad pivot and release clocks are implemented.
  Geyser now runs its randomized quiet/eruption cycle. Torch Smoke adds native fire
  particles and a fixed glow on a random torch's `weapon_fx01`. Changeling Cast emits
  for one second and fades its lensflare through 1.5 seconds.
  Magic Fire and Magic Gift now use native line emission with explicit preview
  `weapon_fx01..02` endpoints on random related weapons.
  Fireball adds all five event palettes and its native pulsing flare at a weapon's
  `weapon_gl01`. **Random weapon** rerolls these equipment previews.
  See [equipment FX attachments and verified Fireball textures](../docs/research/particle-definitions/weapon-fx-attachments.md).
  Magic Fire/Gift expose native unsigned **Strength** through a numeric control and
  `fx-strength <value>` cheat, with size/growth coefficients decoded from Sacred.exe.
  **Distance** supplies an editable endpoint separation. Time Control defaults to
  the native combat caller's red actor bursts along a 200-unit world path.
  The separate named particle layer's flare cone is retained in the catalogue. Magic Fire/Gift keep
  authored weapon helpers by default and support a length override. Actor width
  continues to use the random character's Items.pak blocking radius.
  **Default inputs** restores distance and strength defaults. See
  [FX input defaults](../docs/research/particle-definitions/preview-inputs.md).
  See [line and Fireball FX](../docs/research/particle-definitions/line-and-fireball-fx.md),
  [cyclic and flare FX](../docs/research/particle-definitions/cyclic-and-flare-fx.md),
  [previous constructor decoding](../docs/research/particle-definitions/standard-fx-families.md)
  and [implementation progress](../docs/research/particle-definitions/asset-viewer-progress.md).
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

In Models, Effects and Characters, drag with the left mouse button to rotate horizontally.
The rotation slider stays in sync; Reset camera restores the starting orientation.
Models and Effects show a thin white ground grid that fades away from the preview.
Their perspective camera makes distant grid cells converge and shrink.
Grid cells and lines have fixed world dimensions, so they grow naturally when zooming in.
Scroll to zoom, or use `zoom <wheel steps>`
in the console (positive zooms in; negative zooms out).

Console cheats are available without desktop input simulation:

```text
open models|effects|equipment|textures|characters
model <Items.pak row>
fx <Items.pak row>
fx-implemented true|false
fx-search <name, ID or native family; empty clears>
fx-variant script|<native event subtype>
fx-strength <unsigned value>
fx-distance <native units, 0..2000>
fx-defaults
fx-restart
fx-release
fx-step <seconds; advances paused playback without resetting>
fx-actor
fx-play true|false
fx-time <seconds, 0..60; seeks and pauses>
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
zoom <wheel steps; Models or Effects>
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
