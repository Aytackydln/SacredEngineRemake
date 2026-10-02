# Player particle controls

The player cimGui window exposes the original 210-entry FX catalogue and thirteen
native event variants. Decoded entries appear first; unmapped entries are
disabled and retain their diagnostic in the tooltip. There are 15 decoded
script presets and thirteen playable native bursts. Generic subtype 2 is the confirmed
level-up effect; critical-hit attribution remains unconfirmed.

The two initially collapsed sections separate native lifetime behavior.
**One-time (Play)** lists the decoded event bursts, which use authored seed
particles or actual posed actor vertices and motion until they expire. Mesh bursts
are marked `(model)` and require self targeting. **Other (Enable / Disable)** controls
continuous emission and includes a timed Play preview (default 0.5 seconds).
Native zero-interval count emission uses BurstCount; timed emission uses
EmissionInterval. Disabling emission lets particles drain according to decoded
size/fade motion. Stop all previews clears interactive FX without affecting
script placements or equipment effects. Changing particle quality or disabling
world particles also clears interactive FX.

Self targeting samples the player's position and terrain elevation when an effect
starts. Mesh bursts sample its current posed geometry once. Effects keep that
spawn origin as the actor moves, matching the decoded native events. The separate
**Follow player position (preview)** checkbox defaults off and explicitly moves
the entire effect with the player, including existing particles. Point
targeting uses explicit tile coordinates, samples the terrain there, and stays
fixed. An additional height offset uses native particle units. Each FX can have
independent self and point toggles; changing targeting selects which toggle is
controlled. Already enabled effects retain their original target/height/follow mode; disable
and enable again to change these values.

Model-dependent effects are listed from the current equipment's authored effect
surfaces, grouped by texture and rendering mode. These toggles suppress matching
draws on the player model only. They remain bound to the model's real anchors and
are unavailable for point targeting. The disabled selections survive character
and equipment reloads during the scene, without modifying cached asset meshes or
inventing attachments for unrelated models.

Console cheats exercise the same update path:

```text
set player-panel on
set particle-panel toggles
set particle-panel play
set particle-list all
set particle-target self
set particle-follow off
set particle-target 2263,3136
set particle-play FIRE_S
set particle-enable DWARFMAGIC_BLU
set particle-disable DWARFMAGIC_BLU
set particle-model off
set particle-model on
set particle-stop all
screenshot particle-controls
```

The WorldParticleEffectPlayer lives in Sacred.World and can also be driven by a
terminal client; it uses the same emitter, CPU simulation, blend, atlas and color
definitions as script placements. No shader-specific preview approximation or
hardcoded item/texture IDs were introduced. No additional bin/pak byte meanings
were recovered by this UI work.

Verification: `_scratch/ParticlePlaybackChecks` exercises all decoded presets at
every quality and both CPU modes, including expiry, target anchoring, independent
toggles and render invalidation. `_scratch/ParticlePanelLiveChecks` drives live
console commands and captures frames exclusively through the screenshot cheat.

## Decoded event variants

The One-time section contains PuzzleSolved subtype 0, PuzzleSolved_v subtypes
0 through 5, Generic subtypes 0 (Actor scatter), 2 (Level-up), 5 (Actor sparks),
and 9 (Actor impact smoke), and Changeling subtypes 0/1.
Mesh-dependent events use the current posed player mesh; point targeting is
available for the fixed radial bursts only. The Other section lists
continuous definitions, including the newly decoded Magic Prison preset and its
halo; each continuous row also supports a timed Play preview. Console playback
accepts native event selectors, e.g. `set particle-play PUZZLE_SOLVED_V:2`,
`set particle-play GENERIC:9`, and the friendly alias `set particle-play Level-up`.
See [native evidence and validation](expanded-native-effects.md).
