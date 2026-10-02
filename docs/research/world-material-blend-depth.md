# World material blend and depth states

Verified 2026-10-02 using the Sacred Gold texture loader, Tiles.pak definitions,
SDR/HDR DX12 pipeline creation, offscreen world rendering, and live screenshot
cheats. Existing [native depth research](world-object-depth.md) supplies the
2004 game's sprite depth rules; no new archive byte interpretation was needed.

## Texture evidence

The active game texture source (including its unpacked TGA overrides) decoded
25,511 distinct archive texture names:

| Authored coverage | Textures |
|---|---:|
| Alpha 255 throughout | 8,250 |
| Binary alpha 0/255 | 6,224 |
| Contains alpha 1..254 | 11,037 |

Scenery is not universally opaque. CHEST01, CHEST02, CHEST_03, COFFIN_01,
COFFIN_02, and COFFIN_05 are fully covered. CHEST_01 has 15,392 zero-alpha,
2,574 fractional-alpha, and 47,570 fully covered texels. COFFIN_04 has 15,404,
2,867, and 47,265 respectively. Such fractional textures retain blending and
model depth writes for self-occlusion.

All 90,132 distinct referenced terrain cells from 5,008 sheets were checked
against the source diamond in SacredTerrainTileCommon.hlsl. A conservative
test includes every texel whose rounding square intersects the source polygon,
independently of the destination elevation. Of these cells, 69,899 have binary
coverage and 20,233 require blending. Transparent atlas padding is not itself
evidence that the visible tile is translucent. ISO01 and ISO02, for example,
contain 20,536 transparent and 45,000 opaque texels per sheet.

## Pipeline choices

| Pass | Blend | Depth |
|---|---|---|
| Binary-coverage static sprites, lit or unlit | Disabled; discard holes and output alpha 1 | Disabled for ordinary painter sprites |
| Binary-coverage depth-flagged sprites | Disabled; discard holes and output alpha 1 | LessEqual, writes enabled |
| Fractional or player-fading ordinary sprites | Premultiplied alpha | Disabled |
| Fractional depth-flagged sprites | Premultiplied alpha | LessEqual, writes enabled |
| Post-model transparent sprites/particles | Existing alpha composition | LessEqual, writes disabled |
| Opaque/binary static model materials | Disabled; existing alpha cutoff, output alpha 1 | LessEqual, writes enabled |
| Fractional model materials | Straight alpha, SRC_ALPHA / INV_SRC_ALPHA | LessEqual, writes enabled |
| Animated model materials | Straight alpha, SRC_ALPHA / INV_SRC_ALPHA | LessEqual, writes enabled |
| Effect overlays | Existing premultiplied composition | Existing material-specific depth state |
| Binary terrain cell composition | Disabled; discard zero-alpha texels | Disabled |
| Fractional terrain cell composition | Existing straight/premultiplied alpha | Disabled |
| Composed sector quads | Existing alpha composition for transparent outer borders | Disabled |
| Water, halos, shadows, illumination | Existing specialized composition | Existing pass-specific state |
| Upscale, area downsample, player occlusion coverage | Disabled | Disabled |

Vortice 3.8.3's `BlendDescription.Opaque` enables ONE/ZERO blending.
`Dx12BlendStates.Opaque` explicitly clears BlendEnable instead. Stencil remains
disabled. Straight-RGB model, animated-model, and inventory-colour shaders use
`Dx12BlendStates.StraightAlpha`; Vortice's AlphaBlend preset uses ONE as the
source factor and would over-contribute RGB at fractional coverage. Sprite
pixel shaders no longer export SV_Depth: their vertex shader
already emits the same interpolated depth with W=1, allowing rasterizer depth
testing without a redundant pixel depth write. Alpha discard is retained;
forced early depth writes would incorrectly fill sprite holes.

Binary sprite/model materials use cutout boundaries rather than blending the
fractional alpha created by bilinear filtering. Authored fractional alpha,
player fading, and particle coverage retain the existing blended shaders.

Terrain batching preserves consecutive submission order and offsets the root
SRV into the instance buffer for each run; a DrawInstanced start-instance value
alone does not offset these SV_InstanceID-indexed structured-buffer reads.

## Validation

The full Debug solution build completed without warnings or errors. SDR and
HDR world PSOs compiled and were created on the local GPU. Three offscreen
world scenes passed deterministic repeat rendering and fence-protected cache
release checks. Against the existing renderer baseline, their channel mean
differences were 0.017284, 0.001231, and 0.009163 on the 0..255 scale, with no
alpha-channel differences; these comparisons are not pixel-identical claims.
The D3D12 debug layer was unavailable on this Windows installation.

Live screenshot cheats captured the stone bridge, pavilion/wooden bridge,
Waldburg gate, lava bank, particle scene, and an HDR pavilion frame. The SDR
bridge, pavilion, and lava captures were visually inspected. No input emulation
or window focusing was used. Detailed audit CSVs and verification logs are in
`_scratch/BlendAudit`; they are investigation artifacts, not runtime inputs.
