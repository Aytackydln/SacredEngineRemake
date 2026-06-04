import os
import io
import math
import struct
import zlib
import re
import json
import pickle
import hashlib
import tempfile
from pathlib import Path
from collections import defaultdict, OrderedDict, deque
from dataclasses import dataclass
import threading
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor, as_completed

# Hide pygame's support prompt; keep the real OpenGL display backend for the viewer.
os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")

import pygame

try:
    import numpy as np
except ImportError:
    np = None

try:
    from OpenGL.GL import (
        glBegin, glBindTexture, glBlendFunc, glCallList, glClear, glClearColor, glColor4f,
        glDeleteLists, glDeleteTextures, glDisable, glEnable, glEnd, glEndList,
        glGenLists, glGenTextures, glGetIntegerv, glLineWidth, glLoadIdentity, glMatrixMode,
        glNewList, glOrtho, glPopMatrix, glPushMatrix, glScalef, glTranslatef,
        glTexCoord2f, glTexImage2D, glTexParameteri, glVertex2f, glViewport,
        glReadPixels,
        GL_BLEND, GL_COLOR_BUFFER_BIT, GL_CLAMP_TO_EDGE, GL_LINEAR,
        GL_LINES, GL_LINE_LOOP, GL_MODELVIEW, GL_NEAREST, GL_ONE, GL_ONE_MINUS_SRC_ALPHA,
        GL_SRC_ALPHA, GL_TEXTURE_2D,
        GL_TRIANGLE_FAN, GL_TRIANGLES, GL_PROJECTION, GL_QUADS, GL_RGBA, GL_COMPILE,
        GL_TEXTURE_MAG_FILTER, GL_TEXTURE_MIN_FILTER, GL_TEXTURE_WRAP_S,
        GL_TEXTURE_WRAP_T, GL_UNSIGNED_BYTE, GL_MAX_TEXTURE_SIZE,
    )
except Exception as _opengl_import_error:
    raise RuntimeError(
        "This OpenGL viewer requires PyOpenGL. Install it with: pip install PyOpenGL PyOpenGL_accelerate"
    ) from _opengl_import_error

# ============================================================
# CONFIG
# ============================================================

LOD1_ZOOM = 0.17
LOD8_ZOOM = 0.035

GAME_PATH = Path(r"C:\GOG Games\Sacred Gold")
KEYX_PATH = GAME_PATH / "World" / "sectors.keyx"
WLDX_PATH = GAME_PATH / "World" / "sectors.wldx"
TILES_PAK_PATH = GAME_PATH / "Pak" / "tiles.pak"
TEXTURE_PAK_PATH = GAME_PATH / "Pak" / "texture.pak"
MIXED_PAK_PATH = GAME_PATH / "Pak" / "mixed.pak"
STATIC_PAK_PATH = GAME_PATH / "World" / "Static.pak"
FLOOR_PAK_PATH = GAME_PATH / "World" / "Floor.pak"
ITEMS_PAK_PATH = GAME_PATH / "Pak" / "ITEMS.PAK"

# STATIC.PAK 2D overlay reconstructed from runtime renderer / world access:
#   Tile +0x04           = head ID of a STATIC.PAK record chain.
#   Static +0x04         = TypeManager typeId.
#   Static +0x08         = flags; the ordinary sprite pass skips flags & 0x290.
#   Static +0x0C         = owning sector ID (ushort; confirmed against visible records).
#   Static +0x0E/+0x12  = already-projected world-pixel X/Y sprite anchor.
#   Static +0x1F         = next static record ID in the tile chain.
#   ITEMS.PAK type +0x10 = MIXED.PAK base sprite/group ID.
#
# This intentionally implements only the 2D MIXED sprite path. Granny/3D
# creature/weapon/model rendering is not required for the static world map.
DRAW_STATIC_MIXED_OBJECTS = True
STATIC_OBJECTS_LINEAR_FILTER = False  # use nearest filtering: 2D sprites should stay crisp when zoomed
STATIC_OBJECTS_MAX_VISIBLE = 3000
STATIC_OBJECTS_DEBUG_ORIGINS = False
STATIC_NORMAL_RENDER_EXCLUDE_FLAGS = 0x290
STATIC_CHAIN_MAX_DEPTH = 4096

# MIXED, TILE and FLOOR textures keep their source alpha; opaque black pixels remain visible artwork.

# Manual alignment for only the 2D STATIC/MIXED objects toggled with O.
# Coordinates are in the same projected world-pixel space as STATIC +0x0E/+0x12.
# Positive X moves objects right; positive Y moves objects down.
STATIC_OBJECT_SHIFT_X = 47.8
STATIC_OBJECT_SHIFT_Y = -0.3

# Terrain height values reconstructed from World_InterpolateTerrainHeightAtTilePos:
# tile descriptor +0x18..+0x1B are retained for inspection/gameplay reconstruction,
# but the rendered floor remains on the flat isometric grid, as in the game.
READ_TERRAIN_HEIGHT_VALUES = True
TERRAIN_HEIGHT_SCALE = 2.5
FLAT_TILE_HEIGHTS = (0.0, 0.0, 0.0, 0.0)

# Terrain floor vertex shading reconstructed from FloorTile_ApplyAndCacheVertexTint /
# SurfaceConfig_ComputeVertexTintRGB:
#   tile +0x14..+0x17 = four per-corner intensity bytes.
# These values reproduce the dark/light low/high-looking terrain patches without
# deforming floor geometry. Applied to terrain, FLOOR overlays and liquids in close LOD.
DRAW_TERRAIN_VERTEX_TINT = True
TERRAIN_TINT_MINIMUM = 0          # diagnostic clamp; keep at 0 for engine values.
TERRAIN_TINT_ORDER = (0, 1, 2, 3)  # engine corner order: left, top, right, bottom.

# The game does not draw all objects from one list: BuildVisibleRenderQueues
# partitions them into renderer vectors according to asset-definition metadata.
# Confirmed for the problematic indoor/adjacent-object path:
#   asset +0x2E == 0x0C identifies the special ordering class
#   asset flags & 0x00000004  -> rear queue  (+0x96AD0)
#   asset flags & 0x00800000  -> front queue (+0x96B00)
# The viewer resolves the same 0x80-byte asset/type payload through ITEMS.PAK.
# STATIC.PAK does not expose the world-object instance +0x24 gate, so a class-12
# rear-marked STATIC sprite is routed to the rear vector directly.
STATIC_USE_ENGINE_QUEUE_BUCKETS = True
STATIC_SPECIAL_RENDER_CLASS = 0x0C
# In the real indoor render path, building wall/background components appear to
# be submitted before room objects. We do not yet have the instance-side +0x24
# selector in STATIC.PAK, so apply this only while PageUp/I interior mode is active:
# render class-12 sprites on the selected interior layer before normal props.
STATIC_INTERIOR_CLASS12_WALLS_FIRST = True
STATIC_QUEUE_DRAW_ORDER = (
    "q0_rear_or_gfx4_layer1",
    "q1_auxiliary",
    "q2_gfx4_other",
    "q3_ordinary",
    "q4_front_or_gfx800000",
)

# Building/static layer controls.  Static +0x2B is the surface/render layer field
# used by the renderer when routing static sprites.  Start with ground layer 1;
# use PageUp/PageDown in the viewer to reveal/hide upper building floors, or
# Home to show all layers.
STATIC_MAX_SURFACE_LAYER = 1       # None = show all static layers
STATIC_LAYER_TOGGLE_ENABLED = True

# FLOOR.PAK overlay rendering reconstructed from the full-game
# WorldRenderer_DrawFloorBlendOverlays (FUN_0062d3c0):
#   Tile +0x0C        = FLOOR chain head
#   FLOOR +0x04       = tileOrBlendRef
#       low 17 bits   = primary visible/colour FLOOR tile
#       high 15 bits  = secondary blend-mask tile; non-zero selects GPU two-texture blending
#   FLOOR +0x0C       = next FLOOR record ID
#
# Proof for the order: the single-texture path renders the low field directly,
# while the two-texture path binds texture(low_id) first and texture(high_id) second.
# The demo executable used a 16/16 split; the full game requires 17/15.
# FLOOR overlays render only in live LOD1, from the already-resident VRAM tiles.
DRAW_FLOOR_OVERLAYS = True
FLOOR_OVERLAY_MODE = "all"       # "all" or "off"; press F to toggle.
FLOOR_CHAIN_MAX_DEPTH = 128
FLOOR_PRIMARY_TILE_MASK = 0x1FFFF
FLOOR_SECONDARY_TILE_SHIFT = 17
FLOOR_SECONDARY_TILE_MASK = 0x7FFF

# Animated liquid pass reconstructed from WorldRenderer_DrawAnimatedSurfaceEffects.
# Tile +0x1F high nibble selects one of two liquid style channels:
#   0x90 -> SectorSurfaceInfo +0xF7
#   0xA0 -> SectorSurfaceInfo +0xF8
# Either channel may select a water or lava texture style.
#
# World_LoadSectorIndexExtended proves that each KEYX entry embeds a 0x100-byte
# SectorSurfaceInfo block at KEYX +0x1E9 and copies it to the runtime Sector surface-info pointer (Sector +0x17C in the full game):
#   KEYX +0x2E0 == surfaceInfo +0xF7 -> style ID for 0x90 water.
#   KEYX +0x2E1 == surfaceInfo +0xF8 -> style ID for 0xA0 class.
#
# WorldRenderer_DrawAnimatedSurfaceEffects then resolves:
#   style = SurfaceConfig + 0x91E8 + style_id * 0xD8
#   style->frames[animationFrame]       at +0x00
#   style->frameCount                   at +0xC8
#   style->enableSecondaryPass          at +0xCC
#   style->mainAlphaMultiplier          at +0xD0
#
# Main liquid vertex order is tile +0x10, +0x11, +0x13, +0x12 and uses signed bytes:
#   alpha = clamp(s8(corner) * style->mainAlphaMultiplier, 0, 255)
DRAW_LIQUID_SURFACES = True
LIQUID_SURFACE_TYPE_MASK = 0xF0
LIQUID_SURFACE_TYPE_90 = 0x90
LIQUID_SURFACE_TYPE_A0 = 0xA0
LIQUID_SURFACE_TYPE_WATER = LIQUID_SURFACE_TYPE_90  # internal compatibility name
LIQUID_SURFACE_TYPE_LAVA = LIQUID_SURFACE_TYPE_A0   # internal compatibility name

KEYX_SURFACE_INFO_OFFSET = 0x1E9
KEYX_STYLE_90_OFFSET = KEYX_SURFACE_INFO_OFFSET + 0xF7  # 0x2E0
KEYX_STYLE_A0_OFFSET = KEYX_SURFACE_INFO_OFFSET + 0xF8  # 0x2E1

# Full-game animated liquid styles recovered from SurfaceConfig_ctor:
#   definitions base = SurfaceConfig +0x91E8
#   record stride    = 0xD8
#   frames[]         = +0x00, frame_count = +0xC8
#   detail pass      = +0xCC, main alpha multiplier = +0xD0
#   hazard/lava flag = +0xD4
# Keep the existing viewer dictionary style so liquid rendering remains compatible.
FULL_ANIMATED_SURFACE_STYLE_DEFINITIONS = {
    0:  {"texture_kind": "water",    "family": "B", "frame_count": 50, "main_alpha_multiplier": -12,  "detail_enabled": True,  "hazard": False, "label": "B_WATER"},
    1:  {"texture_kind": "water",    "family": "B", "frame_count": 50, "main_alpha_multiplier": -12,  "detail_enabled": True,  "hazard": False, "label": "B_WATER"},
    2:  {"texture_kind": "water",    "family": "C", "frame_count": 50, "main_alpha_multiplier": -12,  "detail_enabled": True,  "hazard": False, "label": "C_WATER"},
    3:  {"texture_kind": "water",    "family": "D", "frame_count": 50, "main_alpha_multiplier": -12,  "detail_enabled": True,  "hazard": False, "label": "D_WATER"},
    4:  {"texture_kind": "lava",     "family": "A", "frame_count": 50, "main_alpha_multiplier": -255, "detail_enabled": False, "hazard": True,  "label": "A_LAVA"},
    5:  {"texture_kind": "lava",     "family": "B", "frame_count": 50, "main_alpha_multiplier": -255, "detail_enabled": False, "hazard": True,  "label": "B_LAVA"},
    6:  {"texture_kind": "lava",     "family": "C", "frame_count": 20, "main_alpha_multiplier": -255, "detail_enabled": False, "hazard": True,  "label": "C_LAVA"},
    7:  {"texture_kind": "schwefel", "family": "A", "frame_count": 20, "main_alpha_multiplier": -255, "detail_enabled": False, "hazard": True,  "label": "A_SCHWEFEL"},
    8:  {"texture_kind": "lava",     "family": "D", "frame_count": 50, "main_alpha_multiplier": -255, "detail_enabled": False, "hazard": True,  "label": "D_LAVA"},
    9:  {"texture_kind": "water",    "family": "E", "frame_count": 50, "main_alpha_multiplier": -255, "detail_enabled": False, "hazard": False, "label": "E_WATER"},
    10: {"texture_kind": "water",    "family": "F", "frame_count": 50, "main_alpha_multiplier": -24,  "detail_enabled": True,  "hazard": False, "label": "F_WATER"},
    11: {"texture_kind": "water",    "family": "G", "frame_count": 50, "main_alpha_multiplier": -12,  "detail_enabled": True,  "hazard": False, "label": "G_WATER"},
    12: {"texture_kind": "lava",     "family": "E", "frame_count": 50, "main_alpha_multiplier": -255, "detail_enabled": False, "hazard": True,  "label": "E_LAVA"},
    13: {"texture_kind": "water",    "family": "B", "frame_count": 50, "main_alpha_multiplier": -12,  "detail_enabled": False, "hazard": False, "label": "B_WATER"},
}
ANIMATED_SURFACE_STYLE_DEFINITIONS = FULL_ANIMATED_SURFACE_STYLE_DEFINITIONS
UNKNOWN_STYLE_MAIN_ALPHA_MULTIPLIER = -12
UNKNOWN_STYLE_DETAIL_ENABLED = True

LIQUID_TEXTURE_FAMILIES = {
    "water": ("A", "B", "C", "D", "E", "F", "G"),
    "lava": ("A", "B", "C", "D", "E"),
    "schwefel": ("A",),
}
LIQUID_TEXTURE_FAMILY_DEFAULT = {"water": "C", "lava": "A"}
LIQUID_STATIC_FRAME_DEFAULT = {"water": 2, "lava": 2}
LIQUID_ALPHA_GLOBAL_SCALE = 1.0
LIQUID_USE_VERTEX_ALPHA = True

# The recovered secondary pass is style-enabled, submitted before the main water
# quad, and uses the renderer-owned CAUST00.TGA..CAUST31.TGA sequence:
#   intensity = clamp(s8(corner) * -8, 0, 255) * surface_tint_alpha / 256
# It writes the scaled intensity to RGB and renders with ONE/ONE additive blending.
# The exact engine time-to-caustic-frame conversion still needs its floating
# constant decoded, so the viewer keeps the caustic frame manually selectable.
DRAW_LIQUID_SECOND_PASS = True
LIQUID_SECOND_PASS_TEXTURE_PREFIX = "CAUST"
LIQUID_SECOND_PASS_FRAME_COUNT = 32
LIQUID_SECOND_PASS_FRAME_DEFAULT = 0
LIQUID_SECOND_PASS_INTENSITY_MULTIPLIER = -8
# FUN_00571e70(GetSurfaceConfig()) >> 24 scales the detail colour. 1.0 is kept
# editable until that surface-tint value is extracted for the active environment.
LIQUID_SECOND_PASS_STRENGTH = 1.0
# The game expands the detail quad by 0.2 / renderer scale, which is 0.2 screen
# pixels after the viewer's projection/zoom transform.
LIQUID_SECOND_PASS_PIXEL_EXPAND = 0.2


# WLDX marks animated surfaces per terrain tile. Draw exactly one terrain diamond.
LIQUID_LINEAR_FILTER = True
# Liquid texture tiles use a 96x48 projected footprint, centred on the 100x50 terrain placement.
LIQUID_PROJECTED_TILE_W = 96.0
LIQUID_PROJECTED_TILE_H = 48.0
LIQUID_PROJECTED_OFFSET_X = 2.0
LIQUID_PROJECTED_OFFSET_Y = 1.0
# Verified mapping from engine liquid values (+0x10,+0x11,+0x13,+0x12)

# FLOOR overlays, animated liquids and STATIC/MIXED sprites are live LOD1 layers only.

# Building view: exterior draws layers <= active level. Interior draws only the
# selected layer and suppresses outside FLOOR overlays above ground.
STATIC_ACTIVE_LAYER_DEFAULT = 1
STATIC_LAYER_VIEW_DEFAULT = "exterior"  # "exterior", "interior", or "all"

# None loads every sector present in SECTORS.KEYX. Set this to a list such as
# [113] or list(range(1, 177)) when testing a selected subset only.
SECTOR_IDS = None

# KEYX sector positioning:
# World_LoadSectorIndexExtended reads absolute position sources at +0x3C/+0x40.
# Only that path is used here; WLDX label and neighbour-link placement code has been removed.
KEYX_ABSOLUTE_RAW_X_OFFSET = 0x3C
KEYX_ABSOLUTE_RAW_Y_OFFSET = 0x40
KEYX_ABSOLUTE_BIAS = 0x19
KEYX_ENGINE_SCALE_OVERRIDE = None  # Set to DAT_0073b91c / DAT_0073cbec for exact emulation.



# Important behavior:
# - LOD1 uses atlas-backed ground geometry compiled only for currently visited sectors.
# - LOD8 is baked once with terrain, FLOOR, liquids and default exterior STATIC sprites,
#   then saved to disk losslessly. LOD4 remains removed.
# - LOD16 is derived from cached LOD8 and saved; frame updates never generate far imagery.
# - Full-resolution tile surfaces and texture.pak bytes are released after GPU upload;
#   they are reconstructed transiently only when a resize recreates the GL context.
OVERVIEW_LOD_FACTOR = 16
OVERVIEW_CHUNK_PX = 2048
SECTOR_LOAD_WORKERS = max(2, min(8, (os.cpu_count() or 2)))
LOD_LAZY_WORKERS = max(2, min(8, (os.cpu_count() or 2)))
LOD8_RAM_CACHE_MAX_SECTORS = 768
LOD8_REQUESTS_PER_FRAME = 32
LOD16_RAM_CACHE_MAX_CHUNKS = 256
LOD16_REQUESTS_PER_FRAME = 8
GROUND_COMPILES_PER_FRAME = 2
GROUND_COMPILED_SECTOR_CACHE_LIMIT = 192
FLOOR_COMPILES_PER_FRAME = 2
FLOOR_COMPILED_SECTOR_CACHE_LIMIT = 192

# True means preload/crop/upload every valid tile definition, even when SECTOR_IDS
# currently displays only a subset of the map.
# LOD1 base ground is packed once from the already-cut tile surfaces into one or
# more resident atlas pages, then drawn through compiled OpenGL display lists.
# This removes Python per-tile work and nearly all texture binds while panning.
GROUND_ATLAS_MAX_PAGE_SIZE = 4096
GROUND_ATLAS_PADDING = 0       # GL_NEAREST is used, so no bleed padding is required.

# Multiprocessing for startup tile decoding and LOD generation. Use all available
# logical CPUs by default; set either to a smaller number if RAM pressure is high.
LOD_BUILD_WORKERS = max(1, (os.cpu_count() or 2))
SHEET_DECODE_WORKERS = LOD_BUILD_WORKERS

# JPEG-backed LOD8/LOD16 images keep a lossless alpha plane for overlapping diamonds.
LOD_ALPHA_COMPRESSION_LEVEL = 6

# Cached between launches. Live LOD1 artwork is still extracted and uploaded to VRAM
# each time; interpreted map data and composited far-view imagery are persisted.
PERSISTENT_CACHE_SCHEMA = "full-v31-streamed-lod1-1"
PERSISTENT_CACHE_ROOT = Path(__file__).resolve().parent / "sacred_map_viewer_cache"
PERSISTENT_LOD8_FACTOR = 8
PERSISTENT_LOD16_FACTOR = 16

# Pygame/viewer settings.
WINDOW_SIZE = (1920, 1080)
# Start focused on one sector so the first frame does not try to load the entire
# world at once. Press Shift+1 in the viewer to fit the whole world.
START_FITTED_TO_WORLD = False
START_SECTOR_ID = 70
START_ZOOM = 0.75

BACKGROUND_COLOR = (20, 20, 25)
SHOW_HOVER_INFO_DEFAULT = True

KEYX_HEADER_SIZE = 0x100
KEYX_ENTRY_SIZE = 0x300
PAK_HEADER_SIZE = 0x100
PAK_DESC_SIZE = 0x0C

SECTOR_W = 64
SECTOR_H = 64
TILE_DESC_SIZE = 0x20

# Tile sprite cutout size includes transparent/overlap border pixels.
# Source texture rectangle used for each atlas diamond. The live engine does not
# draw these as pre-cut alpha diamonds: it samples four UV points from a shared
# 18-entry diamond atlas table and draws a slightly oversized 96.4x48.4 quad.
ISO_TILE_W = 100
ISO_TILE_H = 50
ENGINE_FLOOR_RENDER_HALF_W = 48.2
ENGINE_FLOOR_RENDER_HALF_H = 24.2

# Actual engine tile-to-projected-world spacing.
ISO_STEP_W = 96
ISO_STEP_H = 48

TILES_PER_TEXTURE = 18

# Engine diamond-atlas source origins, recovered from WorldRenderer_Constructor:
# x = column * 104 + (odd row ? 52 : 0), y = row * 25.
TILE_POSITIONS = [
    [0, 0], [104, 0], [52, 25], [156, 25],
    [0, 50], [104, 50], [52, 75], [156, 75],
    [0, 100], [104, 100], [52, 125], [156, 125],
    [0, 150], [104, 150], [52, 175], [156, 175],
    [0, 200], [104, 200],
]

# Four source points inside the 100x50 crop, in the engine's vertex order:
# left, top, bottom, right. 0.002 UV bias == 0.512 source pixels on a 256x256 sheet.
ENGINE_DIAMOND_UV_PIXELS = {
    "left":   (2.512, 24.012),
    "top":    (50.512, 1.012),
    "bottom": (50.000, 48.512),
    "right":  (98.012, 23.500),
}

# LOD1 compiles visible sector display lists only from an idle service; panning never
# performs new LOD1 GL compilation work. LOD8/LOD16 are persistent composited images.
LOD1_IDLE_GROUND_COMPILES_PER_TICK = 1
LOD1_IDLE_FLOOR_COMPILES_PER_TICK = 1
LOD1_IDLE_LIQUID_COMPILES_PER_TICK = 1
LOD1_OBJECT_TEXTURE_UPLOADS_PER_IDLE_TICK = 8
ASYNC_TEXTURE_UPLOADS_PER_FRAME = 8

# ============================================================
# LOW-LEVEL HELPERS
# ============================================================

def u8(b, o):
    return struct.unpack_from("<B", b, o)[0]


def s8(b, o):
    return struct.unpack_from("<b", b, o)[0]


def u16(b, o):
    return struct.unpack_from("<H", b, o)[0]


def s16(b, o):
    return struct.unpack_from("<h", b, o)[0]


def u32(b, o):
    return struct.unpack_from("<I", b, o)[0]


def s32(b, o):
    return struct.unpack_from("<i", b, o)[0]


def cstr(raw):
    return raw.split(b"\x00", 1)[0].decode("ascii", errors="replace")

def diamond_points(x, y, w=ISO_TILE_W, h=ISO_TILE_H, border=0):
    return [
        (x + w // 2, y + border),
        (x + w - 1 - border, y + h // 2),
        (x + w // 2, y + h - 1 - border),
        (x + border, y + h // 2),
    ]


# ============================================================
# TEXTURE DECODING
# ============================================================

def decode_4444(data, width, height):
    """Decode ARGB4444 using bulk NumPy conversion where available."""
    pixel_count = width * height
    max_pixels = min(len(data) // 2, pixel_count)
    if np is not None:
        values = np.frombuffer(data, dtype="<u2", count=max_pixels)
        rgba = np.zeros((pixel_count, 4), dtype=np.uint8)
        rgba[:max_pixels, 0] = ((values >> 8) & 0xF) * 17
        rgba[:max_pixels, 1] = ((values >> 4) & 0xF) * 17
        rgba[:max_pixels, 2] = (values & 0xF) * 17
        rgba[:max_pixels, 3] = ((values >> 12) & 0xF) * 17
        return pygame.image.frombuffer(rgba.tobytes(), (width, height), "RGBA").copy()

    # Portable fallback; much slower than the NumPy path.
    surf = pygame.Surface((width, height), pygame.SRCALPHA)
    for i in range(max_pixels):
        v = struct.unpack_from("<H", data, i * 2)[0]
        surf.set_at((i % width, i // width), (
            ((v >> 8) & 0xF) * 17,
            ((v >> 4) & 0xF) * 17,
            (v & 0xF) * 17,
            ((v >> 12) & 0xF) * 17,
        ))
    return surf


def decode_type6_bgra(data, width, height):
    """Decode already-packed BGRA pixels without a Python per-pixel loop."""
    needed = width * height * 4
    raw = bytes(data[:needed])
    if len(raw) < needed:
        raw += b"\x00" * (needed - len(raw))
    return pygame.image.frombuffer(raw, (width, height), "BGRA").copy()



# ============================================================
# TILES.PAK READER
# ============================================================

class TileDef:
    def __init__(self, idx, filename, tile_number, raw):
        self.idx = idx
        self.filename = filename
        self.tile_number = tile_number
        self.raw = raw


class TilesPak:
    def __init__(self, path, log=None):
        self.path = Path(path)
        self.defs = []
        self.log = log
        self._load()

    def _read_count(self, header, file_size):
        count16 = u16(header, 4)
        count32 = u32(header, 4)
        max_desc_count = max(0, (file_size - PAK_HEADER_SIZE) // PAK_DESC_SIZE)
        if 0 <= count32 <= max_desc_count:
            return count32, "u32@+4"
        if 0 <= count16 <= max_desc_count:
            return count16, "u16@+4 fallback"
        raise RuntimeError(
            f"Cannot determine tiles.pak entry count. "
            f"count16={count16}, count32={count32}, max={max_desc_count}"
        )

    def _load(self):
        file_size = self.path.stat().st_size
        with self.path.open("rb") as f:
            header = f.read(PAK_HEADER_SIZE)
            count, mode = self._read_count(header, file_size)

            if self.log:
                self.log.add(f"TILES.PAK parse count: {count} ({mode})")

            descs = []
            for _ in range(count):
                d = f.read(PAK_DESC_SIZE)
                if len(d) != PAK_DESC_SIZE:
                    break
                descs.append((u32(d, 0), u32(d, 4), u32(d, 8)))

            for i, (_, off, size) in enumerate(descs):
                if off <= 0 or size <= 0 or off + size > file_size:
                    self.defs.append(TileDef(i, "", 0, b""))
                    continue
                f.seek(off)
                data = f.read(size)
                filename = cstr(data[0x00:0x20])
                tile_number = u32(data, 0x24) if len(data) >= 0x28 else 0
                self.defs.append(TileDef(i, filename, tile_number, data))

        if self.log:
            nonempty = [d for d in self.defs if d.filename]
            self.log.add(f"TILES.PAK loaded defs: {len(self.defs)}, named: {len(nonempty)}")

    def get(self, tile_id):
        if 0 <= tile_id < len(self.defs):
            return self.defs[tile_id]
        return None


@dataclass(frozen=True)
class TextureRecord:
    idx: int
    name: str
    offset: int
    size: int
    width: int
    height: int
    typ: int


@dataclass(frozen=True)
class TileSource:
    """How one logical map tile is produced from RAM-backed texture.pak data."""
    tile_id: int
    texture_name: str
    texture_offset: int
    texture_size: int
    width: int
    height: int
    texture_type: int
    subtile: int
    crop_rect: tuple


@dataclass(frozen=True)
class RamImageBlob:
    """Raw RGBA bytes used for resident tile/sprite/liquid texture uploads."""
    width: int
    height: int
    rgba: bytes


@dataclass(frozen=True)
class JpegLodBlob:
    """Compressed LOD image: JPEG RGB plus a lossless compressed alpha plane."""
    width: int
    height: int
    jpeg_rgb: bytes
    alpha_zlib: bytes


class TexturePak:
    """texture.pak loaded once into RAM and indexed by its embedded texture names."""
    def __init__(self, path, log=None):
        self.path = Path(path)
        self.data = self.path.read_bytes()
        self.records = []
        self.by_name = {}
        self.by_offset = {}
        self.log = log
        self._index()

    def _read_count(self, header, file_size):
        count16 = u16(header, 4)
        count32 = u32(header, 4)
        max_desc_count = max(0, (file_size - PAK_HEADER_SIZE) // PAK_DESC_SIZE)
        if 0 <= count32 <= max_desc_count:
            return count32, "u32@+4"
        if 0 <= count16 <= max_desc_count:
            return count16, "u16@+4 fallback"
        raise RuntimeError(f"Cannot determine texture.pak entry count. count16={count16}, count32={count32}, max={max_desc_count}")

    def _add_lookup_name(self, name, rec):
        if not name:
            return
        key = name.lower()
        stem = os.path.splitext(key)[0]
        self.by_name[key] = rec
        self.by_name.setdefault(stem, rec)
        # Mixed records can name a .444 resource while texture archives may
        # expose the same stem with a different/case-normalized suffix.
        if stem.startswith("mix"):
            self.by_name.setdefault(stem + ".444", rec)
        m = re.fullmatch(r"(iso)(\d+)(\.tga)", key)
        if m:
            num = int(m.group(2))
            for width in (1, 2, 3, 4):
                self.by_name[f"iso{num:0{width}d}.tga"] = rec
            self.by_name[f"iso{num}.tga"] = rec

    def _index(self):
        file_size = len(self.data)
        header = self.data[:PAK_HEADER_SIZE]
        count, mode = self._read_count(header, file_size)
        if self.log:
            self.log.add(f"TEXTURE.PAK loaded into RAM: {file_size} bytes; parse count: {count} ({mode})")

        type_counts = defaultdict(int)
        desc_base = PAK_HEADER_SIZE
        for i in range(count):
            p = desc_base + i * PAK_DESC_SIZE
            d = self.data[p:p + PAK_DESC_SIZE]
            if len(d) != PAK_DESC_SIZE:
                break
            off, size = u32(d, 4), u32(d, 8)
            if off <= 0 or size <= 0 or off + min(size, 0x50) > file_size:
                continue
            embedded = self.data[off:off + 0x50]
            name = cstr(embedded[0x00:0x20])
            rec = TextureRecord(i, name, off, size, u16(embedded, 0x20), u16(embedded, 0x22), u8(embedded, 0x24))
            self.records.append(rec)
            self.by_offset[off] = rec
            self._add_lookup_name(name, rec)
            type_counts[rec.typ] += 1

        if self.log:
            self.log.add(f"TEXTURE.PAK indexed records: {len(self.records)}")
            self.log.add(f"TEXTURE.PAK type counts: {dict(sorted(type_counts.items()))}")
            for r in self.records[:20]:
                self.log.add(f"  sample texture {r.idx}: {r.name!r} {r.width}x{r.height} type={r.typ} size={r.size}")

    def get_record(self, filename):
        key = (filename or "").lower()
        return self.by_name.get(key) or self.by_name.get(os.path.splitext(key)[0])

    def load_record_surface(self, rec):
        if rec is None:
            return None
        # Preserve the prior decoder's payload span while sourcing from immutable RAM.
        data = memoryview(self.data)[rec.offset + 0x50: rec.offset + 0x50 + rec.size]
        try:
            if rec.typ == 6:
                surf = decode_type6_bgra(data, rec.width, rec.height)
            elif rec.typ == 4:
                surf = decode_4444(zlib.decompress(data), rec.width, rec.height)
            else:
                print(f"Unsupported texture type {rec.typ} for {rec.name}")
                return None
            # Black is valid artwork; preserve source alpha without colour-key conversion.
            return surf
        except Exception as e:
            print(f"Failed to decode texture {rec.name}: {e}")
            return None

    def load_by_name(self, filename):
        return self.load_record_surface(self.get_record(filename))


    def close(self):
        self.data = b""
        self.records.clear()
        self.by_name.clear()
        self.by_offset.clear()



# ============================================================
# FLOOR.PAK TILE OVERLAYS (WATER / PAVEMENT / BLENDED TRANSITIONS)
# ============================================================

@dataclass(frozen=True)
class FloorOverlayRecord:
    floor_id: int
    descriptor_type: int
    payload_size: int
    tile_or_blend_ref: int       # +0x04, confirmed by WorldRenderer_DrawFloorOverlays
    next_floor_id: int           # +0x0C, confirmed linked-list field
    raw: bytes

    @property
    def tile_id_a(self):
        return self.tile_or_blend_ref & FLOOR_PRIMARY_TILE_MASK

    @property
    def tile_id_b(self):
        return (self.tile_or_blend_ref >> FLOOR_SECONDARY_TILE_SHIFT) & FLOOR_SECONDARY_TILE_MASK

    @property
    def is_blend(self):
        return self.tile_id_b != 0


@dataclass(frozen=True)
class FloorOverlayInstance:
    floor_id: int
    tile_or_blend_ref: int
    iso_x: float
    iso_y: float
    chain_depth: int
    sector_id: int
    local_x: int
    local_y: int
    terrain_height: float = 0.0
    corner_heights: tuple = (0.0, 0.0, 0.0, 0.0)
    corner_tints: tuple = (255, 255, 255, 255)

    @property
    def tile_id_a(self):
        return self.tile_or_blend_ref & FLOOR_PRIMARY_TILE_MASK

    @property
    def tile_id_b(self):
        return (self.tile_or_blend_ref >> FLOOR_SECONDARY_TILE_SHIFT) & FLOOR_SECONDARY_TILE_MASK

    @property
    def is_blend(self):
        return self.tile_id_b != 0


class FloorPak:
    """Load fixed 0x10-byte FLOOR.PAK records as used by the normal floor renderer."""
    RECORD_SIZE = 0x10

    def __init__(self, path, log=None):
        self.path = Path(path)
        self.log = log
        self.records_by_id = {}
        self._load()

    def _read_count(self, header, file_size):
        count16 = u16(header, 4)
        count32 = u32(header, 4)
        max_desc_count = max(0, (file_size - PAK_HEADER_SIZE) // PAK_DESC_SIZE)
        if 0 <= count32 <= max_desc_count:
            return count32, "u32@+4"
        if 0 <= count16 <= max_desc_count:
            return count16, "u16@+4 fallback"
        raise RuntimeError(
            f"Cannot determine FLOOR.PAK entry count. count16={count16}, "
            f"count32={count32}, max={max_desc_count}"
        )

    def _load(self):
        file_size = self.path.stat().st_size
        with self.path.open("rb") as f:
            header = f.read(PAK_HEADER_SIZE)
            count, mode = self._read_count(header, file_size)
            if self.log:
                self.log.add(f"FLOOR.PAK descriptor count: {count} ({mode})")
            descriptors = []
            for floor_id in range(count):
                d = f.read(PAK_DESC_SIZE)
                if len(d) != PAK_DESC_SIZE:
                    break
                descriptors.append((floor_id, u32(d, 0), u32(d, 4), u32(d, 8)))
            for floor_id, desc_type, off, size in descriptors:
                if floor_id == 0 or off <= 0 or off + self.RECORD_SIZE > file_size:
                    continue
                f.seek(off)
                raw = f.read(self.RECORD_SIZE)
                if len(raw) != self.RECORD_SIZE:
                    continue
                self.records_by_id[floor_id] = FloorOverlayRecord(
                    floor_id=floor_id,
                    descriptor_type=desc_type,
                    payload_size=size,
                    tile_or_blend_ref=u32(raw, 0x04),
                    next_floor_id=u32(raw, 0x0C),
                    raw=raw,
                )
        if self.log:
            blend_count = sum(1 for rec in self.records_by_id.values() if rec.is_blend)
            self.log.add(
                f"FLOOR.PAK runtime-readable records: {len(self.records_by_id)}, "
                f"packed two-tile blend records: {blend_count}"
            )

    def get(self, floor_id):
        return self.records_by_id.get(int(floor_id))


def collect_floor_overlay_instances(sectors, floor_pak, tiles_pak, log=None):
    """Collect FLOOR records linked from Tile +0x0C, including packed two-tile blends."""
    instances = []
    tile_ids = set()
    tile_heads = reached = missing = invalid_tile = cycles = limit_hits = blend_instances = 0
    for sec in sectors:
        tiles = sec["tiles"]
        for ly in range(SECTOR_H):
            for lx in range(SECTOR_W):
                off = (ly * SECTOR_W + lx) * TILE_DESC_SIZE
                floor_id = u32(tiles, off + 0x0C)
                if floor_id == 0:
                    continue
                tile_heads += 1
                local_seen = set()
                depth = 0
                wx = sec["origin_x"] + lx
                wy = sec["origin_y"] + ly
                iso_x, iso_y = world_to_iso(wx, wy)
                corner_heights = FLAT_TILE_HEIGHTS
                corner_tints = terrain_tile_corner_tints(tiles, off)
                terrain_height = sum(corner_heights) * 0.25
                while floor_id:
                    if floor_id in local_seen:
                        cycles += 1
                        break
                    if depth >= FLOOR_CHAIN_MAX_DEPTH:
                        limit_hits += 1
                        break
                    local_seen.add(floor_id)
                    rec = floor_pak.get(floor_id)
                    if rec is None:
                        missing += 1
                        break
                    reached += 1
                    valid = False
                    for tile_id in (rec.tile_id_a, rec.tile_id_b):
                        if not tile_id:
                            continue
                        tile_def = tiles_pak.get(tile_id)
                        if tile_def is not None and tile_def.filename:
                            tile_ids.add(tile_id)
                            valid = True
                        else:
                            invalid_tile += 1
                    if valid:
                        if rec.is_blend:
                            blend_instances += 1
                        instances.append(FloorOverlayInstance(
                            floor_id=rec.floor_id,
                            tile_or_blend_ref=rec.tile_or_blend_ref,
                            iso_x=iso_x,
                            iso_y=iso_y,
                            chain_depth=depth,
                            sector_id=sec["sector_id"],
                            local_x=lx,
                            local_y=ly,
                            terrain_height=terrain_height,
                            corner_heights=corner_heights,
                            corner_tints=corner_tints,
                        ))
                    depth += 1
                    floor_id = rec.next_floor_id
    line = (
        "FLOOR overlays confirmed +0x04 layout: "
        f"tile_heads={tile_heads}, reached_records={reached}, drawable_instances={len(instances)}, "
        f"packed_blends={blend_instances}, unique_tile_ids={len(tile_ids)}, "
        f"invalid_tile_refs={invalid_tile}, missing_records={missing}, cycles={cycles}, limit_hits={limit_hits}"
    )
    if log:
        log.add(line)
    return instances, tile_ids


def index_instances_by_sector(instances):
    """Retain draw order while avoiding full-world scans for each live LOD1 frame."""
    indexed = defaultdict(list)
    for item in instances:
        indexed[item.sector_id].append(item)
    return dict(indexed)




# ============================================================
# LIQUID SURFACE OVERLAY (WLDX TILE +0x1F TYPES 0x90 / 0xA0)
# ============================================================

@dataclass(frozen=True)
class AnimatedSurfaceCandidate:
    sector_id: int
    local_x: int
    local_y: int
    surface_type: int
    liquid_kind: str
    style_id: int
    texture_kind: str | None
    texture_family: str | None
    main_alpha_multiplier: int
    detail_enabled: bool
    iso_x: float
    iso_y: float
    terrain_height: float = 0.0
    corner_heights: tuple = (0.0, 0.0, 0.0, 0.0)
    corner_tints: tuple = (255, 255, 255, 255)
    corner_liquid_alpha: tuple = (255, 255, 255, 255)
    corner_liquid_second_intensity: tuple = (0, 0, 0, 0)
    corner_liquid_raw: tuple = (0, 0, 0, 0)


def liquid_kind_from_surface_type(surface_type):
    if surface_type == LIQUID_SURFACE_TYPE_WATER:
        return "water"
    if surface_type == LIQUID_SURFACE_TYPE_A0:
        return "lava"  # internal compatibility key; UI/logs call this surface A0.
    return None


def animated_surface_style_id(sec, surface_type):
    if surface_type == LIQUID_SURFACE_TYPE_WATER:
        return int(sec.get("animated_surface_style_90", -1))
    if surface_type == LIQUID_SURFACE_TYPE_A0:
        return int(sec.get("animated_surface_style_a0", -1))
    return -1


def liquid_corner_raw_values(tiles, off):
    """Return engine animated-surface vertex inputs in +0x10,+0x11,+0x13,+0x12 order."""
    if off + 0x14 > len(tiles):
        return (0, 0, 0, 0)
    return (s8(tiles, off + 0x10), s8(tiles, off + 0x11),
            s8(tiles, off + 0x13), s8(tiles, off + 0x12))


def liquid_corner_scaled_values(raw, multiplier):
    """Implement clamp(s8(corner) * multiplier, 0, 255)."""
    if not LIQUID_USE_VERTEX_ALPHA:
        return (255, 255, 255, 255)
    multiplier = int(multiplier)
    return tuple(max(0, min(255, int(value) * multiplier)) for value in raw)


def liquid_reorder_for_projection(values):
    """Map engine vertices (v0,v1,v2,v3) onto projected (left,top,right,bottom)."""
    # Verified engine mapping is v0->left, v1->top, v2->bottom, v3->right.
    return (values[0], values[1], values[3], values[2])


def liquid_projected_rect(screen_x, screen_y, zoom):
    draw_w = max(1, int(round(LIQUID_PROJECTED_TILE_W * zoom)))
    draw_h = max(1, int(round(LIQUID_PROJECTED_TILE_H * zoom)))
    offset_x = LIQUID_PROJECTED_OFFSET_X * zoom
    offset_y = LIQUID_PROJECTED_OFFSET_Y * zoom
    draw_x = screen_x + offset_x
    draw_y = screen_y + offset_y
    return draw_x, draw_y, draw_w, draw_h


def style_definition_for_candidate(surface_type, style_id):
    """Return recovered SurfaceConfig style definitions for either liquid channel."""
    if surface_type in (LIQUID_SURFACE_TYPE_WATER, LIQUID_SURFACE_TYPE_A0):
        return ANIMATED_SURFACE_STYLE_DEFINITIONS.get(int(style_id))
    return None


def collect_animated_surface_candidates(sectors, log=None):
    """Collect animated-surface tiles using KEYX SectorSurfaceInfo style selectors."""
    candidates = []
    per_kind_sector = {"water": defaultdict(int), "lava": defaultdict(int)}
    per_style_tiles = {"water": defaultdict(int), "lava": defaultdict(int)}
    ranges = {"water": [127, -128], "lava": [127, -128]}
    unresolved_styles = set()

    for sec in sectors:
        tiles = sec["tiles"]
        for ly in range(SECTOR_H):
            for lx in range(SECTOR_W):
                off = (ly * SECTOR_W + lx) * TILE_DESC_SIZE
                surface_type = u8(tiles, off + 0x1F) & LIQUID_SURFACE_TYPE_MASK
                liquid_kind = liquid_kind_from_surface_type(surface_type)
                if liquid_kind is None:
                    continue

                style_id = animated_surface_style_id(sec, surface_type)
                style_def = style_definition_for_candidate(surface_type, style_id)
                if style_def is None:
                    unresolved_styles.add((surface_type, style_id))
                texture_kind = style_def.get("texture_kind") if style_def is not None else None
                texture_family = style_def["family"] if style_def is not None else None
                main_multiplier = (
                    style_def["main_alpha_multiplier"]
                    if style_def is not None else UNKNOWN_STYLE_MAIN_ALPHA_MULTIPLIER
                )
                detail_enabled = (
                    style_def["detail_enabled"]
                    if style_def is not None else UNKNOWN_STYLE_DETAIL_ENABLED
                )

                raw = liquid_corner_raw_values(tiles, off)
                ranges[liquid_kind][0] = min(ranges[liquid_kind][0], *raw)
                ranges[liquid_kind][1] = max(ranges[liquid_kind][1], *raw)
                per_kind_sector[liquid_kind][sec["sector_id"]] += 1
                per_style_tiles[liquid_kind][style_id] += 1

                wx = sec["origin_x"] + lx
                wy = sec["origin_y"] + ly
                iso_x, iso_y = world_to_iso(wx, wy)
                corner_heights = FLAT_TILE_HEIGHTS
                corner_tints = terrain_tile_corner_tints(tiles, off)
                candidates.append(AnimatedSurfaceCandidate(
                    sector_id=sec["sector_id"], local_x=lx, local_y=ly,
                    surface_type=surface_type, liquid_kind=liquid_kind,
                    style_id=style_id, texture_kind=texture_kind, texture_family=texture_family,
                    main_alpha_multiplier=main_multiplier,
                    detail_enabled=detail_enabled,
                    iso_x=iso_x, iso_y=iso_y,
                    terrain_height=sum(corner_heights) * 0.25,
                    corner_heights=corner_heights, corner_tints=corner_tints,
                    corner_liquid_alpha=liquid_corner_scaled_values(raw, main_multiplier),
                    corner_liquid_second_intensity=liquid_corner_scaled_values(
                        raw, LIQUID_SECOND_PASS_INTENSITY_MULTIPLIER
                    ),
                    corner_liquid_raw=raw,
                ))

    for kind, surface_type, label in (
        ("water", LIQUID_SURFACE_TYPE_WATER, "Water"),
        ("lava", LIQUID_SURFACE_TYPE_A0, "Animated surface A0"),
    ):
        count = sum(per_kind_sector[kind].values())
        signed_range = tuple(ranges[kind]) if count else (None, None)
        styles = ", ".join(
            f"{style_id}:{tile_count}" for style_id, tile_count in sorted(per_style_tiles[kind].items())
        ) or "none"
        line = (
            f"{label} tiles: {count} in {len(per_kind_sector[kind])} sector(s); "
            f"KEYX styles={{ {styles} }}; signed corner range={signed_range}; "
            f"style-table=full; detail={'on' if DRAW_LIQUID_SECOND_PASS else 'off'} "
            f"RGB x{LIQUID_SECOND_PASS_INTENSITY_MULTIPLIER}"
        )
        if log:
            log.add(line)

    if unresolved_styles:
        line = (
            "Unmapped animated-surface KEYX style IDs (using editable fallback texture/alpha): "
            + ", ".join(f"type=0x{stype:02X}/style={style}" for stype, style in sorted(unresolved_styles))
        )
        if log:
            log.add(line)
    return candidates


class LiquidSurfaceStore:
    """Resolve KEYX/style-selected main water frames and shared CAUST detail frames."""
    def __init__(self, texture_pak, families=None, frames=None, log=None):
        self.texture_pak = texture_pak
        self.fallback_families = dict(families or LIQUID_TEXTURE_FAMILY_DEFAULT)
        self.frames = dict(frames or LIQUID_STATIC_FRAME_DEFAULT)
        self.caustic_frame = LIQUID_SECOND_PASS_FRAME_DEFAULT % LIQUID_SECOND_PASS_FRAME_COUNT
        self.family_overrides = {"water": None, "lava": None}
        self.texture_mode_overrides = {"water": None, "lava": None}
        self.fallback_texture_modes = {"water": "water", "lava": "water"}
        self.log = log
        self._blob_cache = {}
        self._warned_missing = set()
        self.available = self._find_available()
        self.available_caustic_frames = self._find_caustic_frames()
        # Style selection is reflected in the viewer status line; avoid verbose startup dump.

    def _name(self, suffix_kind, family, frame=None):
        frame = 0 if frame is None else int(frame) % 50
        suffix = {"water": "WATER", "lava": "LAVA", "schwefel": "SCHWEFEL"}.get(suffix_kind)
        if suffix is None:
            suffix = "WATER"
        return f"{family}_{suffix}{frame:02d}.TGA"

    def caustic_texture_name(self, frame=None):
        frame = self.caustic_frame if frame is None else int(frame) % LIQUID_SECOND_PASS_FRAME_COUNT
        return f"{LIQUID_SECOND_PASS_TEXTURE_PREFIX}{frame:02d}.TGA"

    def _find_available(self):
        result = {kind: {} for kind in LIQUID_TEXTURE_FAMILIES}
        for kind, families in LIQUID_TEXTURE_FAMILIES.items():
            for family in families:
                present = [
                    frame for frame in range(50)
                    if self.texture_pak.get_record(self._name(kind, family, frame)) is not None
                ]
                if present:
                    result[kind][family] = present
        return result

    def _find_caustic_frames(self):
        return [
            frame for frame in range(LIQUID_SECOND_PASS_FRAME_COUNT)
            if self.texture_pak.get_record(self.caustic_texture_name(frame)) is not None
        ]

    def resolve_texture_mode(self, kind, engine_texture_kind=None):
        mode = self.texture_mode_overrides.get(kind) or engine_texture_kind or self.fallback_texture_modes.get(kind, "water")
        return mode if mode in self.available else "water"

    def resolve_family(self, kind, engine_family=None, engine_texture_kind=None):
        texture_mode = self.resolve_texture_mode(kind, engine_texture_kind)
        requested = self.family_overrides.get(kind) or engine_family or self.fallback_families[texture_mode]
        if requested in self.available[texture_mode]:
            return requested
        available = list(self.available[texture_mode])
        fallback = (
            self.fallback_families[texture_mode]
            if self.fallback_families[texture_mode] in self.available[texture_mode]
            else (available[0] if available else requested)
        )
        warning_key = (kind, texture_mode, requested, fallback)
        if warning_key not in self._warned_missing:
            self._warned_missing.add(warning_key)
            line = f"Animated surface texture family {kind}/{texture_mode}/{requested!r} unavailable; using {fallback!r}."
            print(line)
            if self.log:
                self.log.add(line)
        return fallback

    def texture_name(self, kind, engine_family=None, engine_texture_kind=None):
        texture_mode = self.resolve_texture_mode(kind, engine_texture_kind)
        family = self.resolve_family(kind, engine_family, engine_texture_kind)
        frame = self.frames[kind] if kind in self.frames else 0
        return self._name(texture_mode, family, frame)

    def get_blob(self, kind, engine_family=None, engine_texture_kind=None):
        name = self.texture_name(kind, engine_family, engine_texture_kind)
        if name not in self._blob_cache:
            surf = self.texture_pak.load_by_name(name)
            self._blob_cache[name] = surface_to_blob(surf) if surf is not None else None
        return self._blob_cache[name]

    def get_caustic_blob(self):
        name = self.caustic_texture_name()
        if name not in self._blob_cache:
            surf = self.texture_pak.load_by_name(name)
            self._blob_cache[name] = surface_to_blob(surf) if surf is not None else None
            if surf is None and ("caustic", name) not in self._warned_missing:
                self._warned_missing.add(("caustic", name))
                line = f"Caustic detail texture {name!r} unavailable; secondary water pass skipped."
                print(line)
                if self.log:
                    self.log.add(line)
        return self._blob_cache[name]




# ============================================================
# MIXED / STATIC 2D OBJECT OVERLAY
# ============================================================

@dataclass(frozen=True)
class MixedCutoutRecord:
    mixed_id: int
    piece_index: int
    atlas_name: str
    cutout_id: int
    right: int
    bottom: int
    left: int
    top: int
    uv0: float
    uv1: float
    uv2: float
    uv3: float


@dataclass(frozen=True)
class MixedSpriteBlob:
    mixed_id: int
    width: int
    height: int
    anchor_x: int
    anchor_y: int
    blob: RamImageBlob


@dataclass(frozen=True)
class ItemTypeRecord:
    type_id: int
    descriptor_type: int
    mixed_base_group_id: int        # TypeManager record +0x10
    raw: bytes

    @property
    def graphic_render_flags(self):
        # BuildVisibleRenderQueues: flags = *FUN_00413bb0(typeManager, static.type_id)
        # ITEMS payload is the corresponding type record already used for +0x10.
        return u32(self.raw, 0x00) if len(self.raw) >= 4 else 0

    @property
    def render_class(self):
        # AssetDefinition +0x2E. 0x0C enters the game's rear/normal/front
        # special-object routing used by indoor/adjacent building sprites.
        return u8(self.raw, 0x2E) if len(self.raw) > 0x2E else 0


@dataclass(frozen=True)
class StaticObjectRecord:
    static_id: int                  # descriptor index; the ID used by the engine
    descriptor_type: int
    payload_size: int
    payload_instance_id: int        # payload +0x00; usually matches/static-related
    type_id: int                    # +0x04: TypeManager lookup key
    flags: int                      # +0x08
    sector_id: int                  # +0x0C u16, confirmed owning sector ID
    projected_x: int                # +0x0E s32, already in isometric/world-pixel space
    projected_y: int                # +0x12 s32, already in isometric/world-pixel space
    first_upper_level_id: int       # +0x17
    level_root_id: int              # +0x1B
    next_static_id: int             # +0x1F
    support_metadata_id: int        # +0x27
    surface_render_layer: int       # +0x2B s16
    sprite_param_2e: int            # +0x2E
    sprite_param_2f: int            # +0x2F
    orientation_or_frame: int       # +0x30
    animation_param_31: int         # +0x31
    animation_param_32: int         # +0x32
    elevation_tier: int             # +0x33, selected support height += tier * 28
    raw: bytes


class MixedPak2D:
    """Parse 2D MIXED.PAK sprite groups used by ordinary world statics."""
    def __init__(self, path, log=None):
        self.path = Path(path)
        self.log = log
        self.groups = {}
        self.cutout_id_to_group = {}
        self._load()

    def _read_count(self, header, file_size):
        count16 = u16(header, 4)
        count32 = u32(header, 4)
        max_desc_count = max(0, (file_size - PAK_HEADER_SIZE) // PAK_DESC_SIZE)
        if 0 <= count32 <= max_desc_count:
            return count32, "u32@+4"
        if 0 <= count16 <= max_desc_count:
            return count16, "u16@+4 fallback"
        raise RuntimeError(
            f"Cannot determine MIXED.PAK entry count. count16={count16}, "
            f"count32={count32}, max={max_desc_count}"
        )

    def _load(self):
        file_size = self.path.stat().st_size
        with self.path.open("rb") as f:
            header = f.read(PAK_HEADER_SIZE)
            count, mode = self._read_count(header, file_size)
            if self.log:
                self.log.add(f"MIXED.PAK parse count: {count} ({mode})")

            descs = []
            for mixed_id in range(count):
                d = f.read(PAK_DESC_SIZE)
                if len(d) != PAK_DESC_SIZE:
                    break
                descs.append((mixed_id, u32(d, 0), u32(d, 4), u32(d, 8)))

            piece_total = 0
            for mixed_id, _desc_type, off, size in descs:
                if off <= 0 or size <= 0x10 or off + size > file_size:
                    continue
                f.seek(off)
                head = f.read(0x10)
                if len(head) != 0x10:
                    continue
                piece_count = min(u32(head, 0), max(0, (size - 0x10) // 0x40))
                if piece_count <= 0:
                    continue

                pieces = []
                for piece_index in range(piece_count):
                    name_raw = f.read(0x20)
                    rec = f.read(0x20)
                    if len(name_raw) != 0x20 or len(rec) != 0x20:
                        break
                    pieces.append(MixedCutoutRecord(
                        mixed_id=mixed_id,
                        piece_index=piece_index,
                        atlas_name=cstr(name_raw),
                        cutout_id=u32(rec, 0x00),
                        right=u16(rec, 0x04),
                        bottom=u16(rec, 0x06),
                        left=s16(rec, 0x08),
                        top=s16(rec, 0x0A),
                        uv0=struct.unpack_from("<f", rec, 0x10)[0],
                        uv1=struct.unpack_from("<f", rec, 0x14)[0],
                        uv2=struct.unpack_from("<f", rec, 0x18)[0],
                        uv3=struct.unpack_from("<f", rec, 0x1C)[0],
                    ))
                if pieces:
                    self.groups[mixed_id] = pieces
                    piece_total += len(pieces)
                    for piece in pieces:
                        self.cutout_id_to_group.setdefault(piece.cutout_id, mixed_id)

            if self.log:
                self.log.add(
                    f"MIXED.PAK 2D groups with content: {len(self.groups)}, pieces: {piece_total}"
                )

    def resolve_group_id(self, reference_id):
        reference_id = int(reference_id)
        if reference_id in self.groups:
            return reference_id, "group-index"
        group_id = self.cutout_id_to_group.get(reference_id)
        if group_id is not None:
            return group_id, "cutout-id"
        return None, "unmatched"


class ItemsPakTypeTable:
    """Load the TypeManager primary 0x80-byte records from PAK\\ITEMS.PAK.

    Runtime mapping used for static sprites:
        static_record.type_id -> ITEMS.PAK record[type_id]
        type_record +0x10     -> MIXED.PAK base 2D group ID
    """
    RECORD_SIZE = 0x80

    def __init__(self, path, log=None):
        self.path = Path(path)
        self.log = log
        self.records = {}
        self._load()

    def _read_count(self, header, file_size):
        count16 = u16(header, 4)
        count32 = u32(header, 4)
        max_desc_count = max(0, (file_size - PAK_HEADER_SIZE) // PAK_DESC_SIZE)
        if 0 <= count32 <= max_desc_count:
            return count32, "u32@+4"
        if 0 <= count16 <= max_desc_count:
            return count16, "u16@+4 fallback"
        raise RuntimeError(
            f"Cannot determine ITEMS.PAK entry count. count16={count16}, "
            f"count32={count32}, max={max_desc_count}"
        )

    def _load(self):
        file_size = self.path.stat().st_size
        with self.path.open("rb") as f:
            header = f.read(PAK_HEADER_SIZE)
            count, mode = self._read_count(header, file_size)
            if self.log:
                self.log.add(f"ITEMS.PAK parse count: {count} ({mode})")

            descriptors = []
            for type_id in range(count):
                d = f.read(PAK_DESC_SIZE)
                if len(d) != PAK_DESC_SIZE:
                    break
                descriptors.append((type_id, u32(d, 0), u32(d, 4), u32(d, 8)))

            for type_id, desc_type, off, size in descriptors:
                if type_id <= 0 or off <= 0 or off + self.RECORD_SIZE > file_size:
                    continue
                f.seek(off)
                raw = f.read(self.RECORD_SIZE)
                if len(raw) != self.RECORD_SIZE:
                    continue
                self.records[type_id] = ItemTypeRecord(
                    type_id=type_id,
                    descriptor_type=desc_type,
                    mixed_base_group_id=u32(raw, 0x10),
                    raw=raw,
                )
        if self.log:
            nonzero = sum(1 for rec in self.records.values() if rec.mixed_base_group_id)
            self.log.add(
                f"ITEMS.PAK type records loaded: {len(self.records)}, "
                f"nonzero MIXED base IDs: {nonzero}"
            )

    def get(self, type_id):
        return self.records.get(int(type_id))

    def mixed_group_id(self, type_id, mixed_pak):
        record = self.get(type_id)
        if record is None or record.mixed_base_group_id == 0:
            return None
        group_id, _mode = mixed_pak.resolve_group_id(record.mixed_base_group_id)
        return group_id


class StaticPak:
    """Read fixed 0x40-byte STATIC.PAK records exactly as World_GetStaticRecordById.

    The runtime loader indexes descriptors at 0x100 + static_id * 0x0C and
    reads 0x40 bytes from the descriptor payload offset, regardless of the
    descriptor type field. The visible object set is selected by tile chains,
    not by scanning all records as independent instances.
    """
    RECORD_SIZE = 0x40

    def __init__(self, path, log=None):
        self.path = Path(path)
        self.log = log
        self.records_by_id = {}
        self._load()

    def _read_count(self, header, file_size):
        count16 = u16(header, 4)
        count32 = u32(header, 4)
        max_desc_count = max(0, (file_size - PAK_HEADER_SIZE) // PAK_DESC_SIZE)
        if 0 <= count32 <= max_desc_count:
            return count32, "u32@+4"
        if 0 <= count16 <= max_desc_count:
            return count16, "u16@+4 fallback"
        raise RuntimeError(
            f"Cannot determine STATIC.PAK entry count. count16={count16}, "
            f"count32={count32}, max={max_desc_count}"
        )

    @property
    def records(self):
        return list(self.records_by_id.values())

    def _load(self):
        file_size = self.path.stat().st_size
        with self.path.open("rb") as f:
            header = f.read(PAK_HEADER_SIZE)
            count, mode = self._read_count(header, file_size)
            if self.log:
                self.log.add(f"STATIC.PAK descriptor count: {count} ({mode})")

            descriptors = []
            for static_id in range(count):
                d = f.read(PAK_DESC_SIZE)
                if len(d) != PAK_DESC_SIZE:
                    break
                descriptors.append((static_id, u32(d, 0), u32(d, 4), u32(d, 8)))

            for static_id, desc_type, off, size in descriptors:
                if static_id == 0 or off <= 0 or off + self.RECORD_SIZE > file_size:
                    continue
                f.seek(off)
                raw = f.read(self.RECORD_SIZE)
                if len(raw) != self.RECORD_SIZE:
                    continue
                self.records_by_id[static_id] = StaticObjectRecord(
                    static_id=static_id,
                    descriptor_type=desc_type,
                    payload_size=size,
                    payload_instance_id=u32(raw, 0x00),
                    type_id=u32(raw, 0x04),
                    flags=u32(raw, 0x08),
                    sector_id=u16(raw, 0x0C),
                    projected_x=s32(raw, 0x0E),
                    projected_y=s32(raw, 0x12),
                    first_upper_level_id=u32(raw, 0x17),
                    level_root_id=u32(raw, 0x1B),
                    next_static_id=u32(raw, 0x1F),
                    support_metadata_id=u32(raw, 0x27),
                    surface_render_layer=s16(raw, 0x2B),
                    sprite_param_2e=u8(raw, 0x2E),
                    sprite_param_2f=u8(raw, 0x2F),
                    orientation_or_frame=u8(raw, 0x30),
                    animation_param_31=u8(raw, 0x31),
                    animation_param_32=u8(raw, 0x32),
                    elevation_tier=u8(raw, 0x33),
                    raw=raw,
                )
        if self.log:
            self.log.add(f"STATIC.PAK runtime-readable 0x40 records: {len(self.records_by_id)}")


class MixedSpriteStore:
    """Compose complete 2D sprites from MIXED.PAK pieces and texture atlases."""
    def __init__(self, texture_pak, mixed_pak, log=None):
        self.texture_pak = texture_pak
        self.mixed_pak = mixed_pak
        self.log = log
        self.atlas_cache = {}
        self.sprite_cache = {}

    def _get_atlas_surface(self, atlas_name):
        key = (atlas_name or "").lower()
        if not key:
            return None
        if key not in self.atlas_cache:
            self.atlas_cache[key] = self.texture_pak.load_by_name(atlas_name)
        return self.atlas_cache[key]

    def build_sprite(self, group_id):
        group_id = int(group_id)
        if group_id in self.sprite_cache:
            return self.sprite_cache[group_id]
        pieces = self.mixed_pak.groups.get(group_id)
        if not pieces:
            self.sprite_cache[group_id] = None
            return None

        blits = []
        min_x = min_y = None
        max_x = max_y = None
        for piece in pieces:
            atlas = self._get_atlas_surface(piece.atlas_name)
            if atlas is None:
                continue
            aw, ah = atlas.get_size()
            src_l = int(round(min(piece.uv0, piece.uv2) * aw))
            src_t = int(round(min(piece.uv1, piece.uv3) * ah))
            src_r = int(round(max(piece.uv0, piece.uv2) * aw))
            src_b = int(round(max(piece.uv1, piece.uv3) * ah))
            src_l = max(0, min(src_l, aw))
            src_t = max(0, min(src_t, ah))
            src_r = max(0, min(src_r, aw))
            src_b = max(0, min(src_b, ah))
            if src_r <= src_l or src_b <= src_t:
                continue

            dst_l = min(piece.left, piece.right)
            dst_t = min(piece.top, piece.bottom)
            dst_r = max(piece.left, piece.right)
            dst_b = max(piece.top, piece.bottom)
            if dst_r <= dst_l or dst_b <= dst_t:
                continue

            crop = atlas.subsurface((src_l, src_t, src_r - src_l, src_b - src_t)).copy()
            if crop.get_width() != dst_r - dst_l or crop.get_height() != dst_b - dst_t:
                # Ordinary world sprites in the original renderer are not smoothed.
                crop = pygame.transform.scale(crop, (dst_r - dst_l, dst_b - dst_t))
            blits.append((crop, dst_l, dst_t))
            min_x = dst_l if min_x is None else min(min_x, dst_l)
            min_y = dst_t if min_y is None else min(min_y, dst_t)
            max_x = dst_r if max_x is None else max(max_x, dst_r)
            max_y = dst_b if max_y is None else max(max_y, dst_b)

        if not blits or min_x is None or min_y is None or max_x is None or max_y is None:
            self.sprite_cache[group_id] = None
            return None

        width = max(1, max_x - min_x)
        height = max(1, max_y - min_y)
        surf = pygame.Surface((width, height), pygame.SRCALPHA)
        for crop, dst_l, dst_t in blits:
            surf.blit(crop, (dst_l - min_x, dst_t - min_y))

        sprite = MixedSpriteBlob(
            mixed_id=group_id,
            width=width,
            height=height,
            anchor_x=-min_x,
            anchor_y=-min_y,
            blob=surface_to_blob(surf),
        )
        self.sprite_cache[group_id] = sprite
        return sprite

    def get_sprite(self, group_id):
        return self.build_sprite(group_id)


def resolve_static_mixed_group(rec, items_pak, mixed_pak):
    """Resolve the engine's STATIC -> TypeManager -> MIXED sprite path."""
    return items_pak.mixed_group_id(rec.type_id, mixed_pak)


@dataclass(frozen=True)
class PreparedStaticSprite:
    record: StaticObjectRecord
    sector_id: int
    insertion_order: int
    group_id: int
    width: int
    height: int
    anchor_x: int
    anchor_y: int


def prepare_static_sprites(static_records, items_pak, mixed_pak, sprite_store):
    """Resolve each STATIC sprite once; owning sector is read from STATIC +0x0C."""
    prepared = []
    texture_blobs = {}
    unresolved = filtered = 0
    for order, rec in enumerate(static_records):
        if rec.flags & STATIC_NORMAL_RENDER_EXCLUDE_FLAGS:
            filtered += 1
            continue
        group_id = resolve_static_mixed_group(rec, items_pak, mixed_pak)
        if group_id is None:
            unresolved += 1
            continue
        sprite = sprite_store.get_sprite(group_id)
        if sprite is None:
            unresolved += 1
            continue
        texture_blobs.setdefault(group_id, sprite.blob)
        prepared.append(PreparedStaticSprite(
            record=rec,
            sector_id=rec.sector_id,
            insertion_order=order,
            group_id=group_id,
            width=sprite.width,
            height=sprite.height,
            anchor_x=sprite.anchor_x,
            anchor_y=sprite.anchor_y,
        ))
    print(
        f"STATIC prepared: {len(prepared)} instance(s), "
        f"{len(texture_blobs)} unique resident sprite texture(s), "
        f"filtered={filtered}, unresolved={unresolved}"
    )
    return prepared, texture_blobs


def collect_tile_chained_static_records(sectors, static_pak, items_pak, mixed_pak, log=None):
    """Walk tile +0x04 STATIC chains in the engine's painter traversal order.

    BuildVisibleRenderQueues appends objects while it walks floor cells from the
    back/top of the isometric map toward the front/bottom. It does not sort
    objects later.  The previous viewer read the WLDX tile arrays row-major and
    then re-sorted sprite anchors, which loses the owning-tile order used by the
    game.  Here we first order tile visits by their isometric diamond position,
    then preserve linked-list order within each tile.
    """
    reached = {}
    head_count = 0
    missing_ids = set()
    cycle_count = 0
    chain_limit_hits = 0

    tile_visits = []
    for sec in sectors:
        tiles = sec["tiles"]
        for ly in range(SECTOR_H):
            for lx in range(SECTOR_W):
                wx = sec["origin_x"] + lx
                wy = sec["origin_y"] + ly
                off = (ly * SECTOR_W + lx) * TILE_DESC_SIZE
                static_id = u32(tiles, off + 0x04)
                if static_id:
                    # Same back-to-front order used by the floor pass.
                    tile_visits.append((wx + wy, wy, wx, sec["sector_id"], static_id))

    tile_visits.sort(key=lambda item: item[:4])

    for _depth_key, _wy, _wx, _sid, static_id in tile_visits:
        head_count += 1
        local_seen = set()
        depth = 0
        while static_id:
            if static_id in local_seen:
                cycle_count += 1
                break
            if depth >= STATIC_CHAIN_MAX_DEPTH:
                chain_limit_hits += 1
                break
            local_seen.add(static_id)
            depth += 1

            rec = static_pak.records_by_id.get(static_id)
            if rec is None:
                missing_ids.add(static_id)
                break
            if static_id in reached:
                break
            # Python dict insertion order is intentionally the render-queue
            # insertion order. Do not re-sort this list by sprite anchor.
            reached[static_id] = rec
            static_id = rec.next_static_id

    records = list(reached.values())
    drawable = 0
    filtered = 0
    type_missing = 0
    group_missing = 0
    for rec in records:
        if rec.flags & STATIC_NORMAL_RENDER_EXCLUDE_FLAGS:
            filtered += 1
            continue
        type_rec = items_pak.get(rec.type_id)
        if type_rec is None:
            type_missing += 1
            continue
        if resolve_static_mixed_group(rec, items_pak, mixed_pak) is None:
            group_missing += 1
            continue
        drawable += 1

    line = (
        "STATIC tile-chain engine order: "
        f"tile_heads={head_count}, unique_reached={len(records)}, drawable_2d={drawable}, "
        f"filtered_flags_0x{STATIC_NORMAL_RENDER_EXCLUDE_FLAGS:X}={filtered}, "
        f"missing_type={type_missing}, missing_mixed_group={group_missing}, "
        f"missing_static_ids={len(missing_ids)}, cycles={cycle_count}, limit_hits={chain_limit_hits}"
    )
    if log:
        log.add(line)
        if missing_ids:
            log.add(f"  Missing STATIC ID samples: {sorted(missing_ids)[:20]}")
    return records


def static_projected_position(rec):
    """STATIC +0x0E/+0x12 anchors, with an optional manual 2D-object alignment offset."""
    return (
        float(rec.projected_x) + STATIC_OBJECT_SHIFT_X,
        float(rec.projected_y) + STATIC_OBJECT_SHIFT_Y,
    )


def static_layer_is_visible(rec, active_layer, layer_view):
    if layer_view == "all" or active_layer is None:
        return True
    if layer_view == "interior":
        return rec.surface_render_layer == active_layer
    return rec.surface_render_layer <= active_layer


def static_engine_queue_index(rec, items_pak, active_layer=None, layer_view="exterior"):
    """Route a STATIC/MIXED sprite into the renderer vector that the game draws.

    There are two observed submit paths:

    1. Ordinary STATIC tile-chain sprites, previously reconstructed from
       WorldRenderer_BuildVisibleRenderQueues:
         q0 @ +0x96AD0: graphics flag 0x4 on layer 1 / flagged overlay
         q2 @ +0x96AE8: graphics flag 0x4 on non-layer-1 surfaces
         q3 @ +0x96AF4: ordinary statics
         q4 @ +0x96B00: graphics flag 0x800000 statics

    2. Indoor/adjacent building objects:
         asset +0x2E == 0x0C and flags & 0x00000004 -> q0 rear
         asset +0x2E == 0x0C and flags & 0x00800000 -> q4 front
         otherwise                                   -> q3 normal

    WorldRenderer_DrawVisibleObjectsAndStatics consumes q0..q4 sequentially,
    preserving insertion order within each vector.

    For PageUp/I interior view, the current viewer has no separate world-object
    instance record to reproduce the game's exact indoor wall test. The practical
    substitute is to force selected-layer class-12 building sprites into q0 so
    walls/backgrounds are painted before interior props, while exterior drawing
    retains the metadata-derived rear/front split.
    """
    if not STATIC_USE_ENGINE_QUEUE_BUCKETS:
        return 3

    type_rec = items_pak.get(rec.type_id)
    gfx_flags = type_rec.graphic_render_flags if type_rec is not None else 0
    render_class = type_rec.render_class if type_rec is not None else 0

    # When inside a building, paint its class-12 wall/background components first.
    # Restrict the override to interior view so outdoor facades keep their existing
    # foreground/background routing.
    if (
        STATIC_INTERIOR_CLASS12_WALLS_FIRST
        and layer_view == "interior"
        and active_layer is not None
        and active_layer > 1
        and render_class == STATIC_SPECIAL_RENDER_CLASS
    ):
        return 0

    # Exterior/default routing for special building components.
    # Prefer the front flag when both bits are present; a true outside foreground
    # component must not be pulled behind other exterior objects.
    if render_class == STATIC_SPECIAL_RENDER_CLASS:
        if gfx_flags & 0x00800000:
            return 4
        if gfx_flags & 0x00000004:
            return 0
        return 3

    # Existing ordinary STATIC path, retained for non-special objects.
    if gfx_flags & 0x00000004:
        if (rec.flags & 0x20) or rec.surface_render_layer == 1:
            return 0
        return 2
    if gfx_flags & 0x00800000:
        return 4
    return 3


def _collect_static_draw_queues(
    prepared_sprites_by_sector, visible_entries, items_pak, zoom, pan_x, pan_y,
    screen_w, screen_h, active_layer, layer_view
):
    """Route only objects belonging to currently visible sectors into engine queues."""
    visible_prepared = []
    for entry in visible_entries:
        visible_prepared.extend(
            prepared_sprites_by_sector.get(entry["sector"]["sector_id"], ())
        )
    visible_prepared.sort(key=lambda item: item.insertion_order)

    queues = [[] for _ in range(5)]
    filtered = layer_filtered = 0
    margin = max(256, int(512 * zoom))
    for prepared in visible_prepared:
        rec = prepared.record
        if not static_layer_is_visible(rec, active_layer, layer_view):
            layer_filtered += 1
            continue
        iso_x, iso_y = static_projected_position(rec)
        foot_sx = pan_x + iso_x * zoom
        foot_sy = pan_y + iso_y * zoom
        if (
            foot_sx < -margin or foot_sy < -margin
            or foot_sx > screen_w + margin or foot_sy > screen_h + margin
        ):
            continue
        queue_index = static_engine_queue_index(
            rec, items_pak, active_layer=active_layer, layer_view=layer_view
        )
        queues[queue_index].append(prepared)
    return queues, filtered, layer_filtered


def draw_static_mixed_objects_gl(
    screen,
    prepared_sprites_by_sector,
    visible_entries,
    items_pak,
    object_texture_store,
    zoom,
    pan_x,
    pan_y,
    max_visible=STATIC_OBJECTS_MAX_VISIBLE,
    active_layer=STATIC_ACTIVE_LAYER_DEFAULT,
    layer_view=STATIC_LAYER_VIEW_DEFAULT,
):
    """Draw LOD1 statics using sector indexing and startup-resident GL textures."""
    screen_w, screen_h = get_display_size(screen)
    drawn = visible = 0
    queues, filtered, layer_filtered = _collect_static_draw_queues(
        prepared_sprites_by_sector, visible_entries, items_pak, zoom, pan_x, pan_y,
        screen_w, screen_h, active_layer, layer_view
    )

    for queue in queues:
        for prepared in queue:
            rec = prepared.record
            iso_x, iso_y = static_projected_position(rec)
            foot_sx = pan_x + iso_x * zoom
            foot_sy = pan_y + iso_y * zoom
            sx = foot_sx - prepared.anchor_x * zoom
            sy = foot_sy - prepared.anchor_y * zoom
            sw = max(1, int(round(prepared.width * zoom)))
            sh = max(1, int(round(prepared.height * zoom)))
            if sx + sw < 0 or sy + sh < 0 or sx > screen_w or sy > screen_h:
                continue

            visible += 1
            if STATIC_OBJECTS_DEBUG_ORIGINS:
                draw_solid_rect(foot_sx - 2, foot_sy - 2, 5, 5, (255, 220, 0, 230))
            tex = object_texture_store.get(prepared.group_id)
            if tex is None:
                continue
            if max_visible is not None and drawn >= max_visible:
                continue
            draw_textured_quad(tex, int(round(sx)), int(round(sy)), sw, sh)
            drawn += 1

    return drawn, visible, 0, filtered, layer_filtered


# ============================================================
# SECTOR LOADING
# ============================================================

def load_keyx_entries(log=None):
    entries = {}
    with Path(KEYX_PATH).open("rb") as f:
        header = f.read(KEYX_HEADER_SIZE)
        count16 = u16(header, 4)
        count32 = u32(header, 4)
        file_size = Path(KEYX_PATH).stat().st_size
        max_count = max(0, (file_size - KEYX_HEADER_SIZE) // KEYX_ENTRY_SIZE)
        count = count32 if 0 <= count32 <= max_count else count16
        mode = "u32@+4" if count == count32 else "u16@+4 fallback"
        if log:
            log.add(f"KEYX parse count: {count} ({mode}), max={max_count}")

        for _ in range(count):
            entry = f.read(KEYX_ENTRY_SIZE)
            if len(entry) != KEYX_ENTRY_SIZE:
                break
            sid = u32(entry, 0x024)
            entries[sid] = entry

    if log:
        log.add(f"KEYX sectors indexed: {len(entries)}")
    return entries


def keyx_raw_absolute_position(entry):
    """Return the confirmed KEYX absolute coordinate source pair at +0x3C/+0x40."""
    return (
        struct.unpack_from("<i", entry, KEYX_ABSOLUTE_RAW_X_OFFSET)[0],
        struct.unpack_from("<i", entry, KEYX_ABSOLUTE_RAW_Y_OFFSET)[0],
    )


def _round_to_sector_origin(value):
    """Snap a runtime-origin estimate to the 64-tile sector lattice."""
    return int(round(float(value) / SECTOR_W)) * SECTOR_W


def infer_keyx_engine_position_scale(keyx_entries):
    """Infer the raw-coordinate scale directly from absolute KEYX positions.

    KEYX stores sector origins on a regular lattice. The smallest non-zero adjacent
    spacing on either absolute axis is one 64-tile sector.
    """
    if KEYX_ENGINE_SCALE_OVERRIDE is not None:
        return float(KEYX_ENGINE_SCALE_OVERRIDE)

    diffs = []
    for axis in (0, 1):
        values = sorted({keyx_raw_absolute_position(entry)[axis] for entry in keyx_entries.values()})
        diffs.extend(b - a for a, b in zip(values, values[1:]) if b > a)
    if not diffs:
        raise RuntimeError("Cannot infer KEYX absolute-position scale from sector positions.")
    return SECTOR_W / float(min(diffs))


def build_keyx_absolute_layout(keyx_entries):
    """Build sector origins solely from KEYX +0x3C/+0x40 absolute positions."""
    scale = infer_keyx_engine_position_scale(keyx_entries)
    layout = {}
    for sector_id, entry in keyx_entries.items():
        raw_x, raw_y = keyx_raw_absolute_position(entry)
        origin_x = _round_to_sector_origin((raw_x + KEYX_ABSOLUTE_BIAS) * scale)
        origin_y = _round_to_sector_origin((raw_y + KEYX_ABSOLUTE_BIAS) * scale)
        layout[sector_id] = {
            "grid": (origin_x // SECTOR_W, origin_y // SECTOR_H),
            "origin": (origin_x, origin_y),
        }
    return layout


def load_sector(sector_id, keyx_entries, keyx_layout):
    """Read WLDX tile bytes while taking placement and liquid style selectors from KEYX."""
    entry = keyx_entries.get(sector_id)
    if entry is None:
        raise RuntimeError(f"sector {sector_id} not found")
    layout = keyx_layout.get(sector_id)
    if layout is None:
        raise RuntimeError(f"sector {sector_id} has no absolute KEYX placement")

    comp_off = u32(entry, 0x0EC)
    comp_size = u32(entry, 0x0F0)
    tiles_rel = u32(entry, 0x0D4)
    tiles_size = u32(entry, 0x0D8)

    with Path(WLDX_PATH).open("rb") as f:
        f.seek(comp_off)
        decompressed = zlib.decompress(f.read(comp_size))

    tiles = decompressed[tiles_rel:tiles_rel + tiles_size]
    if len(tiles) < SECTOR_W * SECTOR_H * TILE_DESC_SIZE:
        raise RuntimeError(f"sector {sector_id} has short tile block: {len(tiles)}")

    grid_x, grid_y = layout["grid"]
    return {
        "sector_id": sector_id,
        "grid_x": grid_x,
        "grid_y": grid_y,
        "origin_x": grid_x * SECTOR_W,
        "origin_y": grid_y * SECTOR_H,
        "tiles": tiles,
        # World_LoadSectorIndexExtended copies KEYX +0x1E9..+0x2E8 to
        # SectorSurfaceInfo (runtime Sector +0x17C in the full game); the renderer reads +0xF7/+0xF8.
        "animated_surface_style_90": u8(entry, KEYX_STYLE_90_OFFSET),
        "animated_surface_style_a0": u8(entry, KEYX_STYLE_A0_OFFSET),
    }


# ============================================================
# TILE CROPPING AND RAM-BACKED TILE SOURCES
# ============================================================

def crop_iso_tile(sheet, tile_number, positions=None):
    """Copy the engine source rectangle for one 18-pattern diamond.

    The engine cuts the visible shape through diamond geometry and UVs, not by
    multiplying a pre-cut alpha mask. Keeping the complete 100x50 source rectangle
    avoids deleting edge pixels that the 96.4x48.4 diamond samples for crack-free
    floor rendering.
    """
    if sheet is None:
        return None
    positions = positions if positions is not None else TILE_POSITIONS
    tile_number %= len(positions)
    x, y = positions[tile_number]
    rect = pygame.Rect(int(x), int(y), ISO_TILE_W, ISO_TILE_H)
    out = pygame.Surface((ISO_TILE_W, ISO_TILE_H), pygame.SRCALPHA, 32)
    if not sheet.get_rect().contains(rect):
        out.fill((255, 0, 0, 80))
        return out
    out.blit(sheet, (0, 0), rect)
    return out



def load_sectors_parallel(sector_ids, keyx_entries, keyx_layout):
    """Read/decompress sectors concurrently; zlib decompression benefits from concurrent workers."""
    total = len(sector_ids)
    results = [None] * total
    skipped = 0

    def _load_indexed(index_and_id):
        index, sector_id = index_and_id
        try:
            return index, load_sector(sector_id, keyx_entries, keyx_layout), None
        except Exception as exc:
            return index, None, (sector_id, exc)

    print(f"Loading/decompressing sectors using {SECTOR_LOAD_WORKERS} worker thread(s)...")
    with ThreadPoolExecutor(max_workers=SECTOR_LOAD_WORKERS, thread_name_prefix="sacred_sector") as executor:
        complete = 0
        for index, sector, error in executor.map(_load_indexed, enumerate(sector_ids), chunksize=8):
            complete += 1
            if error is None:
                results[index] = sector
            else:
                skipped += 1
                print(f"Skipping sector {error[0]}: {error[1]}")
            if complete % 250 == 0 or complete == total:
                print(f"  sector reads complete: {complete}/{total}")
    return [sector for sector in results if sector is not None], skipped


def collect_used_tile_ids(sectors):
    """Collect base tile IDs concurrently across independent sector tile blocks."""
    def _scan(sec):
        local = set()
        tiles = sec["tiles"]
        for off in range(0, SECTOR_W * SECTOR_H * TILE_DESC_SIZE, TILE_DESC_SIZE):
            local.add(u32(tiles, off))
        return local

    used = set()
    with ThreadPoolExecutor(max_workers=SECTOR_LOAD_WORKERS, thread_name_prefix="sacred_tilescan") as executor:
        for local in executor.map(_scan, sectors, chunksize=16):
            used.update(local)
    return used, 0


def build_tile_sources(used_tile_ids, tiles_pak, texture_pak, log=None):
    """Build compact tile-id -> texture.pak offset/crop metadata without image files."""
    sources = {}
    missing_defs = 0
    missing_textures = 0
    for tile_id in sorted(used_tile_ids):
        tile_def = tiles_pak.get(tile_id)
        if not tile_def or not tile_def.filename:
            missing_defs += 1
            continue
        rec = texture_pak.get_record(tile_def.filename)
        if rec is None:
            missing_textures += 1
            continue
        subtile = int(tile_def.tile_number) % len(TILE_POSITIONS)
        x, y = TILE_POSITIONS[subtile]
        sources[tile_id] = TileSource(
            tile_id=tile_id,
            texture_name=rec.name,
            texture_offset=rec.offset,
            texture_size=rec.size,
            width=rec.width,
            height=rec.height,
            texture_type=rec.typ,
            subtile=subtile,
            crop_rect=(int(x), int(y), ISO_TILE_W, ISO_TILE_H),
        )
    if log:
        log.add(
            f"RAM tile sources: {len(sources)} mapped, missing defs={missing_defs}, "
            f"missing textures={missing_textures}; no per-tile PNGs are generated"
        )
    return sources


def _mp_init_sheet_decode_worker():
    os.environ["SDL_VIDEODRIVER"] = "dummy"
    pygame.init()
    pygame.display.init()
    pygame.display.set_mode((1, 1))


def _mp_decode_and_crop_sheet(task):
    """Decode one texture sheet and return only its required precropped tile blobs."""
    texture_type, width, height, payload, tile_specs = task
    if texture_type == 6:
        sheet = decode_type6_bgra(payload, width, height)
    elif texture_type == 4:
        sheet = decode_4444(zlib.decompress(payload), width, height)
    else:
        return [], len(tile_specs)

    # Keep source alpha: black pixels inside ground artwork are not a colour key.
    results = []
    failed = 0
    for tile_id, subtile in tile_specs:
        img = crop_iso_tile(sheet, subtile)
        if img is None:
            failed += 1
        else:
            results.append((tile_id, surface_to_blob(img)))
    return results, failed


class TileSurfaceStore:
    """Immutable precropped CPU tile cache used for both VRAM upload and RAM LOD builds."""
    def __init__(self, texture_pak, tile_sources):
        self.texture_pak = texture_pak
        self.tile_sources = tile_sources
        self.tile_surfaces = {}
        self.scaled_surfaces = {}

    def prebuild_base_tiles(self, log=None):
        """Decode/crop texture sheets in worker processes, then retain the tiles in RAM."""
        grouped = defaultdict(list)
        for source in self.tile_sources.values():
            grouped[source.texture_offset].append(source)

        tasks = []
        for texture_offset, sources in grouped.items():
            rec = self.texture_pak.by_offset.get(texture_offset)
            if rec is None:
                continue
            payload = bytes(memoryview(self.texture_pak.data)[rec.offset + 0x50: rec.offset + 0x50 + rec.size])
            tile_specs = [(source.tile_id, source.subtile) for source in sources]
            tasks.append((rec.typ, rec.width, rec.height, payload, tile_specs))

        made = 0
        failed = 0
        print(
            f"Decoding and precutting {len(self.tile_sources)} tile(s) from "
            f"{len(tasks)} texture sheet(s) using {SHEET_DECODE_WORKERS} worker process(es)..."
        )
        with ProcessPoolExecutor(
            max_workers=SHEET_DECODE_WORKERS,
            initializer=_mp_init_sheet_decode_worker,
        ) as executor:
            futures = [executor.submit(_mp_decode_and_crop_sheet, task) for task in tasks]
            done = 0
            for future in as_completed(futures):
                done += 1
                try:
                    results, local_failed = future.result()
                    failed += local_failed
                    for tile_id, blob in results:
                        self.tile_surfaces[tile_id] = blob_to_surface(blob)
                        made += 1
                except Exception as exc:
                    print(f"  sheet decode worker failed: {exc}")
                if done % 100 == 0 or done == len(futures):
                    print(f"  decoded sheets: {done}/{len(futures)}; precut tiles: {made}")

        msg = (
            f"Precut tile cache complete: {made} tile(s), failed={failed}; "
            f"NumPy vectorization={'enabled' if np is not None else 'disabled'}"
        )
        print(msg)
        if log:
            log.add(msg)
        return made

    def release_base_tiles(self):
        self.tile_surfaces.clear()

    def get_tile_surface(self, tile_id, factor=1):
        if factor == 1:
            return self.tile_surfaces.get(int(tile_id))
        return self.scaled_surfaces.get((int(tile_id), int(factor)))


# ============================================================
# SECTOR LOD CACHE
# ============================================================

LOCAL_TILE_DRAW_ORDER = tuple(
    sorted((lx + ly, ly, lx) for ly in range(SECTOR_H) for lx in range(SECTOR_W))
)



def world_to_iso(world_x, world_y):
    # Use the engine's projected tile spacing, not the padded tile-image size.
    return (world_x - world_y) * (ISO_STEP_W / 2), (world_x + world_y) * (ISO_STEP_H / 2)


def terrain_tile_corner_heights(tiles, off):
    """Return the four engine terrain corner heights in projected pixels.

    World_InterpolateTerrainHeightAtTilePos reads signed bytes at descriptor
    +0x18..+0x1B and multiplies each by 2.5.  Its interpolation constants map
    these values around the isometric diamond; order here is left/top/right/bottom.
    """
    if not READ_TERRAIN_HEIGHT_VALUES or off + 0x1C > len(tiles):
        return (0.0, 0.0, 0.0, 0.0)
    return tuple(float(s8(tiles, off + 0x18 + i)) * TERRAIN_HEIGHT_SCALE for i in range(4))


def terrain_tile_corner_tints(tiles, off):
    """Return game floor-shadow intensity values for left/top/right/bottom vertices."""
    if not DRAW_TERRAIN_VERTEX_TINT or off + 0x18 > len(tiles):
        return (255, 255, 255, 255)
    raw = tuple(max(TERRAIN_TINT_MINIMUM, u8(tiles, off + 0x14 + i)) for i in range(4))
    return tuple(raw[i] for i in TERRAIN_TINT_ORDER)


def tint_rgbf(tint, alpha=1.0):
    value = max(0.0, min(1.0, float(tint) / 255.0))
    return value, value, value, alpha


def sector_iso_bounds(sec):
    ox, oy = sec["origin_x"], sec["origin_y"]
    points = [(ox, oy), (ox + 63, oy), (ox, oy + 63), (ox + 63, oy + 63)]
    xs = [(x - y) * (ISO_STEP_W / 2) for x, y in points]
    ys = [(x + y) * (ISO_STEP_H / 2) for x, y in points]
    margin = 0.0
    return min(xs), min(ys) - margin, max(xs) + ISO_TILE_W, max(ys) + ISO_TILE_H + margin


def local_sector_base_bounds():
    local_points = [(0, 0), (63, 0), (0, 63), (63, 63)]
    xs = [(x - y) * (ISO_STEP_W / 2) for x, y in local_points]
    ys = [(x + y) * (ISO_STEP_H / 2) for x, y in local_points]
    margin = 0.0
    return min(xs), min(ys) - margin, max(xs) + ISO_TILE_W, max(ys) + ISO_TILE_H + margin


def surface_to_blob(surface):
    surf = surface.convert_alpha() if pygame.display.get_surface() is not None else surface
    width, height = surf.get_size()
    return RamImageBlob(width, height, pygame.image.tostring(surf, "RGBA", True))


def surface_to_jpeg_lod_blob(surface):
    """Compress LOD RGB as JPEG while preserving transparency as a zlib alpha plane."""
    width, height = surface.get_size()
    rgb = pygame.Surface((width, height), depth=24)
    rgb.fill((0, 0, 0))
    rgb.blit(surface, (0, 0))
    jpeg_out = io.BytesIO()
    pygame.image.save(rgb, jpeg_out, "lod.jpg")
    alpha_bytes = pygame.surfarray.array_alpha(surface).tobytes()
    return JpegLodBlob(
        width=width,
        height=height,
        jpeg_rgb=jpeg_out.getvalue(),
        alpha_zlib=zlib.compress(alpha_bytes, LOD_ALPHA_COMPRESSION_LEVEL),
    )


def jpeg_lod_blob_to_surface(blob):
    rgb = pygame.image.load(io.BytesIO(blob.jpeg_rgb), "lod.jpg")
    surf = pygame.Surface((blob.width, blob.height), pygame.SRCALPHA, 32)
    surf.blit(rgb, (0, 0))
    alpha_bytes = zlib.decompress(blob.alpha_zlib)
    alpha = pygame.surfarray.pixels_alpha(surf)
    alpha[:, :] = np.frombuffer(alpha_bytes, dtype=np.uint8).reshape((blob.width, blob.height))
    del alpha
    return surf.convert_alpha() if pygame.display.get_surface() is not None else surf


def blob_to_surface(blob):
    if isinstance(blob, JpegLodBlob):
        return jpeg_lod_blob_to_surface(blob)
    surf = pygame.image.fromstring(blob.rgba, (blob.width, blob.height), "RGBA", True)
    if pygame.display.get_surface() is not None:
        surf = surf.convert_alpha()
    return surf


class LazyReducedLodRenderer:
    """Render reduced sector images from tiny retained LOD8 source tiles.

    LOD16 source tiles are derived lazily from the retained LOD8 tiles; the
    high-resolution archive data does not need to stay resident for overview rendering.
    """
    def __init__(self, tile_store):
        self.tile_store = tile_store
        self.lod16_tiles = {}
        self.lock = threading.RLock()

    def _tile(self, tile_id, factor):
        lod8 = self.tile_store.get_tile_surface(tile_id, 8)
        if factor == 8 or lod8 is None:
            return lod8
        with self.lock:
            cached = self.lod16_tiles.get(tile_id)
            if cached is not None:
                return cached
        size = (max(1, ISO_TILE_W // 16), max(1, ISO_TILE_H // 16))
        scaled = pygame.transform.scale(lod8, size)
        with self.lock:
            return self.lod16_tiles.setdefault(tile_id, scaled)

    def render_sector(self, sec, factor=8):
        factor = int(factor)
        min_ix, min_iy, max_ix, max_iy = local_sector_base_bounds()
        width = int(math.ceil((max_ix - min_ix) / factor)) + 4
        height = int(math.ceil((max_iy - min_iy) / factor)) + 4
        surf = pygame.Surface((width, height), pygame.SRCALPHA, 32)
        tiles = sec["tiles"]
        for _depth, ly, lx in LOCAL_TILE_DRAW_ORDER:
            off = (ly * SECTOR_W + lx) * TILE_DESC_SIZE
            tile_id = u32(tiles, off)
            img = self._tile(tile_id, factor)
            sx = int(round(((lx - ly) * (ISO_STEP_W / 2) - min_ix) / factor))
            sy = int(round(((lx + ly) * (ISO_STEP_H / 2) - min_iy) / factor))
            if img is not None:
                surf.blit(img, (sx, sy))
            else:
                tw = max(1, ISO_TILE_W // factor)
                th = max(1, ISO_TILE_H // factor)
                pygame.draw.polygon(surf, (160, 40, 160, 180), diamond_points(sx, sy, tw, th))
        return surf

    def render_sector_blob(self, sec, factor=8):
        return surface_to_jpeg_lod_blob(self.render_sector(sec, factor))

    def clear(self):
        with self.lock:
            self.lod16_tiles.clear()


class LazySectorLod8Store:
    """Asynchronous compressed RAM cache for only the LOD8 sectors encountered."""
    def __init__(self, renderer, executor):
        self.renderer = renderer
        self.executor = executor
        self.ready = OrderedDict()
        self.pending = {}

    def request_visible(self, visible_entries):
        submitted = 0
        for entry in visible_entries:
            if submitted >= LOD8_REQUESTS_PER_FRAME:
                break
            sec = entry["sector"]
            sid = sec["sector_id"]
            if sid in self.ready:
                self.ready.move_to_end(sid)
            elif sid not in self.pending:
                self.pending[sid] = self.executor.submit(self.renderer.render_sector_blob, sec)
                submitted += 1

    def pump(self):
        completed = 0
        for sid, future in list(self.pending.items()):
            if not future.done():
                continue
            self.pending.pop(sid, None)
            try:
                self.ready[sid] = future.result()
                self.ready.move_to_end(sid)
                while len(self.ready) > LOD8_RAM_CACHE_MAX_SECTORS:
                    self.ready.popitem(last=False)
                completed += 1
            except Exception as exc:
                print(f"LOD8 background render failed for sector {sid}: {exc}")
        return completed

    def get(self, sector_id):
        blob = self.ready.get(sector_id)
        if blob is not None:
            self.ready.move_to_end(sector_id)
        return blob

    def shutdown(self):
        self.pending.clear()
        self.ready.clear()




# ============================================================
# LAZY FAR-ZOOM OVERVIEW CACHE (LOD16)
# ============================================================

def build_overview_chunk_layout(sector_entries):
    """Return overview chunk coverage metadata in base isometric coordinates."""
    chunk_layout = defaultdict(list)
    factor = OVERVIEW_LOD_FACTOR
    for entry in sector_entries:
        ix0, iy0, ix1, iy1 = entry["iso_bounds"]
        ox0, oy0 = ix0 / factor, iy0 / factor
        x0 = math.floor(ox0 / OVERVIEW_CHUNK_PX)
        y0 = math.floor(oy0 / OVERVIEW_CHUNK_PX)
        x1 = math.floor(((ix1 / factor) - 1) / OVERVIEW_CHUNK_PX)
        y1 = math.floor(((iy1 / factor) - 1) / OVERVIEW_CHUNK_PX)
        for cy in range(int(y0), int(y1) + 1):
            for cx in range(int(x0), int(x1) + 1):
                chunk_layout[(cx, cy)].append((entry, ox0, oy0))
    return chunk_layout


def build_overview_chunk_entries_from_layout(chunk_layout):
    entries = []
    factor = OVERVIEW_LOD_FACTOR
    for cx, cy in chunk_layout:
        ix0 = cx * OVERVIEW_CHUNK_PX * factor
        iy0 = cy * OVERVIEW_CHUNK_PX * factor
        entries.append({
            "key": (cx, cy),
            "iso_bounds": (
                ix0, iy0,
                ix0 + OVERVIEW_CHUNK_PX * factor,
                iy0 + OVERVIEW_CHUNK_PX * factor,
            ),
        })
    return entries


class LazyOverviewLod16Store:
    """Build only visible whole-map overview chunks directly at LOD16."""
    def __init__(self, sector_entries, renderer, executor):
        self.renderer = renderer
        self.executor = executor
        self.layout = build_overview_chunk_layout(sector_entries)
        self.entries = build_overview_chunk_entries_from_layout(self.layout)
        self.ready = OrderedDict()
        self.pending = {}

    def _render_chunk(self, key):
        cx, cy = key
        origin_x = cx * OVERVIEW_CHUNK_PX
        origin_y = cy * OVERVIEW_CHUNK_PX
        surf = pygame.Surface((OVERVIEW_CHUNK_PX, OVERVIEW_CHUNK_PX), pygame.SRCALPHA, 32)
        for entry, ox0, oy0 in self.layout.get(key, ()):
            sector_img = self.renderer.render_sector(entry["sector"], OVERVIEW_LOD_FACTOR)
            surf.blit(sector_img, (int(round(ox0 - origin_x)), int(round(oy0 - origin_y))))
        return surface_to_jpeg_lod_blob(surf)

    def request_visible(self, visible_chunks):
        submitted = 0
        for entry in visible_chunks:
            if submitted >= LOD16_REQUESTS_PER_FRAME:
                break
            key = entry["key"]
            if key in self.ready:
                self.ready.move_to_end(key)
            elif key not in self.pending:
                self.pending[key] = self.executor.submit(self._render_chunk, key)
                submitted += 1

    def pump(self):
        completed = 0
        for key, future in list(self.pending.items()):
            if not future.done():
                continue
            self.pending.pop(key, None)
            try:
                self.ready[key] = future.result()
                self.ready.move_to_end(key)
                while len(self.ready) > LOD16_RAM_CACHE_MAX_CHUNKS:
                    self.ready.popitem(last=False)
                completed += 1
            except Exception as exc:
                print(f"LOD16 overview render failed for chunk {key}: {exc}")
        return completed

    def get(self, key):
        blob = self.ready.get(key)
        if blob is not None:
            self.ready.move_to_end(key)
        return blob

    def shutdown(self):
        self.pending.clear()
        self.ready.clear()


# ============================================================
# OPENGL 2D RENDERING HELPERS
# ============================================================

def init_gl_2d(width, height, viewport_size=None):
    """Set up a top-left-origin 2D projection.

    Use the same logical size for glViewport and glOrtho. This avoids the
    stretched/offset window behavior some pygame/OpenGL drivers show when
    the screen and actual drawable size disagree.
    """
    width = max(1, int(width))
    height = max(1, int(height))
    glViewport(0, 0, width, height)
    glMatrixMode(GL_PROJECTION)
    glLoadIdentity()
    glOrtho(0, width, height, 0, -1, 1)
    glMatrixMode(GL_MODELVIEW)
    glLoadIdentity()
    glEnable(GL_BLEND)
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)
    glEnable(GL_TEXTURE_2D)
    glClearColor(
        BACKGROUND_COLOR[0] / 255.0,
        BACKGROUND_COLOR[1] / 255.0,
        BACKGROUND_COLOR[2] / 255.0,
        1.0,
    )


def get_display_size(screen):
    """Return the logical window size used for mouse coords and 2D projection."""
    try:
        w, h = pygame.display.get_window_size()
    except Exception:
        w, h = screen.get_size()
    return max(1, int(w)), max(1, int(h))


def set_gl_window(size):
    w = max(1, int(size[0]))
    h = max(1, int(size[1]))
    screen = pygame.display.set_mode((w, h), pygame.RESIZABLE | pygame.OPENGL | pygame.DOUBLEBUF)
    logical_size = get_display_size(screen)
    init_gl_2d(logical_size[0], logical_size[1])
    return screen


def recenter_after_resize(old_size, new_size, zoom, pan_x, pan_y):
    """Keep the same map point at the window center after resizing."""
    old_w, old_h = old_size
    new_w, new_h = new_size
    center_ix = (old_w / 2 - pan_x) / zoom
    center_iy = (old_h / 2 - pan_y) / zoom
    pan_x = new_w / 2 - center_ix * zoom
    pan_y = new_h / 2 - center_iy * zoom
    return pan_x, pan_y




class GLTexture:
    def __init__(self, tex_id, width, height):
        self.tex_id = tex_id
        self.width = width
        self.height = height

    def delete(self):
        if self.tex_id:
            glDeleteTextures([self.tex_id])
            self.tex_id = 0


def surface_to_gl_texture(surface, linear=True):
    """Upload a pygame Surface as an OpenGL RGBA texture."""
    surf = surface.convert_alpha()
    width, height = surf.get_size()
    data = pygame.image.tostring(surf, "RGBA", True)
    tex_id = glGenTextures(1)
    glBindTexture(GL_TEXTURE_2D, tex_id)
    filt = GL_LINEAR if linear else GL_NEAREST
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filt)
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filt)
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE)
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE)
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, data)
    glBindTexture(GL_TEXTURE_2D, 0)
    return GLTexture(tex_id, width, height)



# ============================================================
# PERSISTENT FULL-COMPOSITE LOD CACHE
# ============================================================

def _cache_file_fingerprint(path):
    p = Path(path)
    if not p.exists():
        return (str(p), None, None)
    stat = p.stat()
    return (str(p), int(stat.st_size), int(stat.st_mtime_ns))


def persistent_cache_signature():
    payload = {
        "schema": PERSISTENT_CACHE_SCHEMA,
        "files": [_cache_file_fingerprint(path) for path in (
            KEYX_PATH, WLDX_PATH, TILES_PAK_PATH, TEXTURE_PAK_PATH,
            MIXED_PAK_PATH, STATIC_PAK_PATH, FLOOR_PAK_PATH, ITEMS_PAK_PATH
        )],
        "sector_ids": None if SECTOR_IDS is None else list(SECTOR_IDS),
        "floor": (FLOOR_PRIMARY_TILE_MASK, FLOOR_SECONDARY_TILE_SHIFT),
        "atlas": (TILE_POSITIONS, ENGINE_DIAMOND_UV_PIXELS),
        "liquids": ANIMATED_SURFACE_STYLE_DEFINITIONS,
        "static": (STATIC_OBJECT_SHIFT_X, STATIC_OBJECT_SHIFT_Y,
                   STATIC_ACTIVE_LAYER_DEFAULT, STATIC_LAYER_VIEW_DEFAULT),
        "lod": (PERSISTENT_LOD8_FACTOR, PERSISTENT_LOD16_FACTOR, OVERVIEW_CHUNK_PX),
    }
    return hashlib.sha256(json.dumps(payload, sort_keys=True, default=str).encode("utf-8")).hexdigest()[:24]


def _atomic_save_pickle(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, suffix=".tmp", delete=False) as file:
        temp_path = Path(file.name)
        pickle.dump(value, file, protocol=pickle.HIGHEST_PROTOCOL)
    temp_path.replace(path)


class PersistentViewerCache:
    def __init__(self):
        self.signature = persistent_cache_signature()
        self.root = PERSISTENT_CACHE_ROOT / self.signature
        self.metadata_path = self.root / "world_state.pkl"
        self.prepared_static_path = self.root / "prepared_static_layout.pkl"
        self.liquid_geometry_path = self.root / "lod1_liquid_geometry.pkl"
        self.manifest_path = self.root / "lod_manifest.json"
        self.lod8_dir = self.root / "lod8"
        self.lod16_dir = self.root / "lod16"

    def load_world(self):
        if not self.metadata_path.exists():
            return None
        try:
            with self.metadata_path.open("rb") as file:
                saved = pickle.load(file)
            if saved.get("schema") == PERSISTENT_CACHE_SCHEMA:
                print(f"Loaded cached interpreted world state: {self.metadata_path}")
                return saved
        except Exception as exc:
            print(f"Ignoring invalid interpreted-world cache: {exc}")
        return None

    def save_world(self, **values):
        _atomic_save_pickle(self.metadata_path, {"schema": PERSISTENT_CACHE_SCHEMA, **values})
        print(f"Saved interpreted world state: {self.metadata_path}")

    def load_prepared_static_layout(self):
        if not self.prepared_static_path.exists():
            return None
        try:
            with self.prepared_static_path.open("rb") as file:
                value = pickle.load(file)
            print(f"Loaded cached STATIC draw preparation: {len(value)} instance(s)")
            return value
        except Exception as exc:
            print(f"Ignoring invalid STATIC preparation cache: {exc}")
            return None

    def save_prepared_static_layout(self, prepared):
        _atomic_save_pickle(self.prepared_static_path, list(prepared))
        print(f"Saved STATIC draw preparation cache: {len(prepared)} instance(s)")

    def load_liquid_geometry(self):
        if not self.liquid_geometry_path.exists():
            return None
        try:
            with self.liquid_geometry_path.open("rb") as file:
                value = pickle.load(file)
            print(f"Loaded cached LOD1 liquid geometry: {len(value)} sector(s)")
            return value
        except Exception as exc:
            print(f"Ignoring invalid liquid geometry cache: {exc}")
            return None

    def save_liquid_geometry(self, geometry_by_sector):
        _atomic_save_pickle(self.liquid_geometry_path, geometry_by_sector)
        print(f"Saved LOD1 liquid geometry cache: {len(geometry_by_sector)} sector(s)")

    def lod8_path(self, sector_id):
        return self.lod8_dir / f"sector_{int(sector_id):06d}.png"

    def lod16_path(self, key):
        return self.lod16_dir / f"chunk_{int(key[0]):+06d}_{int(key[1]):+06d}.png"

    def complete_lods_exist(self, sector_entries, overview_entries):
        try:
            manifest = json.loads(self.manifest_path.read_text(encoding="utf-8"))
        except Exception:
            return False
        return (
            manifest.get("schema") == PERSISTENT_CACHE_SCHEMA
            and manifest.get("lod8_sectors") == len(sector_entries)
            and manifest.get("lod16_chunks") == len(overview_entries)
            and all(self.lod8_path(e["sector"]["sector_id"]).exists() for e in sector_entries)
            and all(self.lod16_path(e["key"]).exists() for e in overview_entries)
        )

    def save_lod_manifest(self, sector_entries, overview_entries):
        self.root.mkdir(parents=True, exist_ok=True)
        self.manifest_path.write_text(json.dumps({
            "schema": PERSISTENT_CACHE_SCHEMA,
            "lod8_sectors": len(sector_entries),
            "lod16_chunks": len(overview_entries),
            "format": "lossless PNG",
            "layers": ["terrain", "FLOOR", "liquid-main", "default-exterior-objects"],
        }, indent=2), encoding="utf-8")


class CompositeLodDiskBuilder:
    """One-time lossless LOD bake: base terrain, FLOOR, liquids and default objects."""
    def __init__(self, cache, tile_store, floor_atlas_store, floor_by_sector,
                 liquid_by_sector, liquid_surface_store, static_by_sector, object_blobs):
        self.cache = cache
        self.tile_store = tile_store
        self.floor_atlas_store = floor_atlas_store
        self.floor_by_sector = floor_by_sector
        self.liquid_by_sector = liquid_by_sector
        self.liquid_surface_store = liquid_surface_store
        self.static_by_sector = static_by_sector
        self.object_blobs = object_blobs
        self.floor_scaled = {}
        self.liquid_scaled = {}
        self.object_scaled = {}

    @staticmethod
    def _scale(surface, factor=8):
        if surface is None:
            return None
        size = (max(1, round(surface.get_width() / factor)),
                max(1, round(surface.get_height() / factor)))
        return pygame.transform.smoothscale(surface, size)

    def _floor_image(self, ref):
        if ref not in self.floor_scaled:
            self.floor_scaled[ref] = self._scale(self.floor_atlas_store._compose_ref(ref))
        return self.floor_scaled[ref]

    def _liquid_image(self, item):
        name = liquid_main_texture_name(self.liquid_surface_store, item)
        if name not in self.liquid_scaled:
            blob = self.liquid_surface_store.get_blob(item.liquid_kind, item.texture_family, item.texture_kind)
            self.liquid_scaled[name] = self._scale(blob_to_surface(blob) if blob else None)
        return self.liquid_scaled[name]

    def _object_image(self, group_id):
        if group_id not in self.object_scaled:
            blob = self.object_blobs.get(group_id)
            self.object_scaled[group_id] = self._scale(blob_to_surface(blob) if blob else None)
        return self.object_scaled[group_id]

    def render_lod8_sector(self, entry):
        factor = PERSISTENT_LOD8_FACTOR
        sec = entry["sector"]
        ix0, iy0, ix1, iy1 = entry["iso_bounds"]
        image = pygame.Surface((max(1, math.ceil((ix1 - ix0) / factor)),
                                max(1, math.ceil((iy1 - iy0) / factor))), pygame.SRCALPHA, 32)
        tiles = sec["tiles"]
        for _depth, ly, lx in LOCAL_TILE_DRAW_ORDER:
            off = (ly * SECTOR_W + lx) * TILE_DESC_SIZE
            full_tile = self.tile_store.get_tile_surface(u32(tiles, off))
            tile = self._scale(full_tile, factor) if full_tile is not None else None
            if tile:
                px, py = world_to_iso(sec["origin_x"] + lx, sec["origin_y"] + ly)
                image.blit(tile, (round((px - ix0) / factor), round((py - iy0) / factor)))

        for item in sorted(self.floor_by_sector.get(sec["sector_id"], ()), key=lambda x: (x.iso_y, x.iso_x, x.chain_depth)):
            tile = self._floor_image(item.tile_or_blend_ref)
            if tile:
                image.blit(tile, (round((item.iso_x - ix0) / factor), round((item.iso_y - iy0) / factor)))

        for item in sorted(self.liquid_by_sector.get(sec["sector_id"], ()), key=lambda x: (x.iso_y, x.iso_x)):
            tile = self._liquid_image(item)
            if tile:
                tile = tile.copy()
                alphas = liquid_corner_scaled_values(item.corner_liquid_raw, item.main_alpha_multiplier)
                tile.set_alpha(max(0, min(255, round(sum(alphas) / 4))))
                image.blit(tile, (round((item.iso_x + LIQUID_PROJECTED_OFFSET_X - ix0) / factor),
                                  round((item.iso_y + LIQUID_PROJECTED_OFFSET_Y - iy0) / factor)))

        for item in self.static_by_sector.get(sec["sector_id"], ()):
            if not static_layer_is_visible(item.record, STATIC_ACTIVE_LAYER_DEFAULT, STATIC_LAYER_VIEW_DEFAULT):
                continue
            sprite = self._object_image(item.group_id)
            if sprite:
                px, py = static_projected_position(item.record)
                image.blit(sprite, (round((px - item.anchor_x - ix0) / factor),
                                    round((py - item.anchor_y - iy0) / factor)))
        return image

    def build_lod8(self, entries):
        self.cache.lod8_dir.mkdir(parents=True, exist_ok=True)
        print(f"Building persistent composite LOD8: {len(entries)} sector(s)...")
        for index, entry in enumerate(entries, 1):
            out = self.cache.lod8_path(entry["sector"]["sector_id"])
            if not out.exists():
                pygame.image.save(self.render_lod8_sector(entry), str(out))
            if index % 100 == 0 or index == len(entries):
                print(f"  LOD8 completed: {index}/{len(entries)}")

    def build_lod16(self, sector_entries):
        layout = build_overview_chunk_layout(sector_entries)
        entries = build_overview_chunk_entries_from_layout(layout)
        self.cache.lod16_dir.mkdir(parents=True, exist_ok=True)
        print(f"Building persistent LOD16 from LOD8: {len(entries)} chunk(s)...")
        for index, entry in enumerate(entries, 1):
            out = self.cache.lod16_path(entry["key"])
            if not out.exists():
                cx, cy = entry["key"]
                chunk = pygame.Surface((OVERVIEW_CHUNK_PX, OVERVIEW_CHUNK_PX), pygame.SRCALPHA, 32)
                for sector_entry, ox0, oy0 in layout.get(entry["key"], ()):
                    lod8 = pygame.image.load(str(self.cache.lod8_path(sector_entry["sector"]["sector_id"]))).convert_alpha()
                    lod16 = pygame.transform.smoothscale(lod8, (max(1, lod8.get_width() // 2), max(1, lod8.get_height() // 2)))
                    chunk.blit(lod16, (round(ox0 - cx * OVERVIEW_CHUNK_PX), round(oy0 - cy * OVERVIEW_CHUNK_PX)))
                pygame.image.save(chunk, str(out))
            if index % 10 == 0 or index == len(entries):
                print(f"  LOD16 completed: {index}/{len(entries)}")
        return entries


class DiskLodStore:
    """Visible PNG-to-VRAM staging only. This class never renders new map imagery."""
    def __init__(self, cache, overview_entries=None, overview=False):
        self.cache = cache
        self.entries = list(overview_entries or [])
        self.overview = overview
        self.ready = OrderedDict()
        self.pending = {}

    def request_visible(self, _entries):
        return

    def pump(self):
        return 0

    def get(self, key):
        blob = self.ready.get(key)
        if blob is not None:
            self.ready.move_to_end(key)
            return blob
        path = self.cache.lod16_path(key) if self.overview else self.cache.lod8_path(key)
        if not path.exists():
            return None
        blob = surface_to_blob(pygame.image.load(str(path)).convert_alpha())
        self.ready[key] = blob
        maximum = LOD16_RAM_CACHE_MAX_CHUNKS if self.overview else LOD8_RAM_CACHE_MAX_SECTORS
        while len(self.ready) > maximum:
            self.ready.popitem(last=False)
        return blob

    def shutdown(self):
        self.ready.clear()
        self.pending.clear()


@dataclass(frozen=True)
class GroundAtlasRegion:
    page: int
    u_left: float
    u_right: float
    v_top: float
    v_bottom: float


class GroundTileAtlasStore:
    """Pack already-cut base tiles into resident texture pages once at startup.

    Atlas construction only blits the existing precut pygame tile surfaces during
    loading. Rendering never slices atlas pixels in Python: compiled vertices use UVs.
    """
    def __init__(self, tile_store, tile_ids):
        self.tile_store = tile_store
        self.tile_ids = sorted(
            tile_id for tile_id in set(tile_ids)
            if tile_store.get_tile_surface(tile_id) is not None
        )
        self.page_surfaces = []
        self.page_textures = []
        self.regions = {}
        self._build_pages()

    def _build_pages(self):
        if not self.tile_ids:
            return
        sample = self.tile_store.get_tile_surface(self.tile_ids[0])
        tile_w, tile_h = sample.get_size()
        gl_limit = int(glGetIntegerv(GL_MAX_TEXTURE_SIZE))
        requested = min(int(GROUND_ATLAS_MAX_PAGE_SIZE), gl_limit)
        columns = max(1, requested // (tile_w + GROUND_ATLAS_PADDING))
        rows = max(1, requested // (tile_h + GROUND_ATLAS_PADDING))
        page_w = columns * (tile_w + GROUND_ATLAS_PADDING)
        page_h = rows * (tile_h + GROUND_ATLAS_PADDING)
        capacity = columns * rows

        for tile_index, tile_id in enumerate(self.tile_ids):
            page_index = tile_index // capacity
            slot = tile_index % capacity
            while len(self.page_surfaces) <= page_index:
                self.page_surfaces.append(pygame.Surface((page_w, page_h), pygame.SRCALPHA, 32))
            x = (slot % columns) * (tile_w + GROUND_ATLAS_PADDING)
            y = (slot // columns) * (tile_h + GROUND_ATLAS_PADDING)
            self.page_surfaces[page_index].blit(self.tile_store.get_tile_surface(tile_id), (x, y))
            self.regions[tile_id] = GroundAtlasRegion(
                page=page_index,
                u_left=x / page_w,
                u_right=(x + tile_w) / page_w,
                v_top=1.0 - y / page_h,
                v_bottom=1.0 - (y + tile_h) / page_h,
            )
        print(
            f"Ground atlas built: tiles={len(self.tile_ids)}, pages={len(self.page_surfaces)}, "
            f"page={page_w}x{page_h}, capacity/page={capacity}"
        )

    def upload_all(self):
        self.clear_gl()
        for page in self.page_surfaces:
            self.page_textures.append(surface_to_gl_texture(page, linear=False))
        estimated = sum(page.get_width() * page.get_height() * 4 for page in self.page_surfaces)
        print(
            f"Ground atlas VRAM upload complete: {len(self.page_textures)} page(s), "
            f"estimated RGBA8 storage={estimated / (1024 * 1024):.2f} MiB"
        )

    def region(self, tile_id):
        return self.regions.get(tile_id)

    def texture_for_page(self, page_index):
        return self.page_textures[page_index] if 0 <= page_index < len(self.page_textures) else None

    def clear_gl(self):
        for texture in self.page_textures:
            if texture is not None:
                texture.delete()
        self.page_textures.clear()

    def release_cpu_pages(self):
        self.page_surfaces.clear()

    def shutdown(self):
        self.clear_gl()
        self.page_surfaces.clear()
        self.regions.clear()


@dataclass(frozen=True)
class CompiledGroundBatch:
    list_id: int
    min_x: float
    min_y: float
    max_x: float
    max_y: float
    tile_count: int


def _region_uv_at_source_pixel(region, px, py):
    u = region.u_left + (float(px) / float(ISO_TILE_W)) * (region.u_right - region.u_left)
    v = region.v_top + (float(py) / float(ISO_TILE_H)) * (region.v_bottom - region.v_top)
    return u, v


def _emit_atlas_ground_diamond(ix, iy, region, corner_tints):
    """Emit one engine-style floor diamond.

    Logical placement remains 96x48. Rendered geometry is the engine's
    ±48.2/±24.2 crack-prevention diamond, and UVs use the recovered 18-pattern
    source points in left/top/bottom/right order.
    """
    x, y = float(ix), float(iy)
    cx = x + ISO_STEP_W * 0.5
    cy = y + ISO_STEP_H * 0.5

    left_t, top_t, right_t, bottom_t = [float(value) for value in corner_tints]
    center_t = (left_t + top_t + right_t + bottom_t) * 0.25

    left_uv = _region_uv_at_source_pixel(region, *ENGINE_DIAMOND_UV_PIXELS["left"])
    top_uv = _region_uv_at_source_pixel(region, *ENGINE_DIAMOND_UV_PIXELS["top"])
    bottom_uv = _region_uv_at_source_pixel(region, *ENGINE_DIAMOND_UV_PIXELS["bottom"])
    right_uv = _region_uv_at_source_pixel(region, *ENGINE_DIAMOND_UV_PIXELS["right"])

    center_uv = (
        (left_uv[0] + top_uv[0] + bottom_uv[0] + right_uv[0]) * 0.25,
        (left_uv[1] + top_uv[1] + bottom_uv[1] + right_uv[1]) * 0.25,
    )

    left_xy = (cx - ENGINE_FLOOR_RENDER_HALF_W, cy)
    top_xy = (cx, cy - ENGINE_FLOOR_RENDER_HALF_H)
    bottom_xy = (cx, cy + ENGINE_FLOOR_RENDER_HALF_H)
    right_xy = (cx + ENGINE_FLOOR_RENDER_HALF_W, cy)

    glBegin(GL_TRIANGLE_FAN)
    glColor4f(*tint_rgbf(center_t, 1.0)); glTexCoord2f(*center_uv); glVertex2f(cx, cy)
    glColor4f(*tint_rgbf(top_t, 1.0)); glTexCoord2f(*top_uv); glVertex2f(*top_xy)
    glColor4f(*tint_rgbf(right_t, 1.0)); glTexCoord2f(*right_uv); glVertex2f(*right_xy)
    glColor4f(*tint_rgbf(bottom_t, 1.0)); glTexCoord2f(*bottom_uv); glVertex2f(*bottom_xy)
    glColor4f(*tint_rgbf(left_t, 1.0)); glTexCoord2f(*left_uv); glVertex2f(*left_xy)
    glColor4f(*tint_rgbf(top_t, 1.0)); glTexCoord2f(*top_uv); glVertex2f(*top_xy)
    glEnd()


class CompiledGroundMeshStore:
    """Compile one atlas-backed LOD1 display list per visible sector.

    The old fixed 128-tile batching created up to 32 calls per sector. One list
    preserves the exact painter order while removing almost all Python/GL call overhead.
    """
    def __init__(self, atlas_store):
        self.atlas_store = atlas_store
        self.batches_by_sector = OrderedDict()
        self.compile_queue = deque()
        self.queued_sector_ids = set()

    def _delete_batches(self, batches):
        for batch in batches:
            if batch.list_id:
                glDeleteLists(batch.list_id, 1)

    def _compile_sector(self, sec):
        items = [
            item for item in build_sector_draw_items(sec)
            if self.atlas_store.region(item[4]) is not None
        ]
        if not items:
            return []
        list_id = glGenLists(1)
        if not list_id:
            return []
        glNewList(list_id, GL_COMPILE)
        current_page = None
        for _, _, ix, iy, tile_id, _heights, corner_tints, _average in items:
            region = self.atlas_store.region(tile_id)
            if region.page != current_page:
                texture = self.atlas_store.texture_for_page(region.page)
                if texture is None:
                    continue
                glBindTexture(GL_TEXTURE_2D, texture.tex_id)
                current_page = region.page
            _emit_atlas_ground_diamond(ix, iy, region, corner_tints)
        glBindTexture(GL_TEXTURE_2D, 0)
        glColor4f(1.0, 1.0, 1.0, 1.0)
        glEndList()
        return [CompiledGroundBatch(
            list_id=list_id,
            min_x=min(item[2] for item in items),
            min_y=min(item[3] for item in items),
            max_x=max(item[2] + ISO_STEP_W + 1 for item in items),
            max_y=max(item[3] + ISO_STEP_H + 1 for item in items),
            tile_count=len(items),
        )]

    def request_visible(self, visible_entries):
        for entry in visible_entries:
            sid = entry["sector"]["sector_id"]
            if sid in self.batches_by_sector:
                self.batches_by_sector.move_to_end(sid)
            elif sid not in self.queued_sector_ids:
                self.compile_queue.append(entry)
                self.queued_sector_ids.add(sid)

    def compile_pending(self, limit=GROUND_COMPILES_PER_FRAME):
        compiled = 0
        while self.compile_queue and compiled < int(limit):
            entry = self.compile_queue.popleft()
            sid = entry["sector"]["sector_id"]
            self.queued_sector_ids.discard(sid)
            if sid in self.batches_by_sector:
                continue
            self.batches_by_sector[sid] = self._compile_sector(entry["sector"])
            self.batches_by_sector.move_to_end(sid)
            while len(self.batches_by_sector) > GROUND_COMPILED_SECTOR_CACHE_LIMIT:
                _old_sid, old_batches = self.batches_by_sector.popitem(last=False)
                self._delete_batches(old_batches)
            compiled += 1
        return compiled

    def draw(self, screen, visible_entries, zoom, pan_x, pan_y):
        self.request_visible(visible_entries)
        screen_w, screen_h = get_display_size(screen)
        tiles_drawn = 0
        glEnable(GL_TEXTURE_2D)
        glMatrixMode(GL_MODELVIEW)
        glPushMatrix()
        glTranslatef(float(pan_x), float(pan_y), 0.0)
        glScalef(float(zoom), float(zoom), 1.0)
        for entry in visible_entries:
            sid = entry["sector"]["sector_id"]
            for batch in self.batches_by_sector.get(sid, ()):
                if (
                    pan_x + batch.max_x * zoom < 0 or pan_y + batch.max_y * zoom < 0
                    or pan_x + batch.min_x * zoom > screen_w or pan_y + batch.min_y * zoom > screen_h
                ):
                    continue
                glCallList(batch.list_id)
                tiles_drawn += batch.tile_count
        glPopMatrix()
        glBindTexture(GL_TEXTURE_2D, 0)
        glColor4f(1.0, 1.0, 1.0, 1.0)
        return tiles_drawn

    def clear(self):
        for batches in self.batches_by_sector.values():
            self._delete_batches(batches)
        self.batches_by_sector.clear()
        self.compile_queue.clear()
        self.queued_sector_ids.clear()

    def shutdown(self):
        self.clear()




class FloorAppearanceAtlasStore:
    """Precompose distinct FLOOR appearances once, then pack them into atlas pages.

    Full-game WorldRenderer_DrawFloorBlendOverlays binds the lower 17-bit tile as
    the primary visible texture and the upper 15-bit tile as its blend-mask input.
    Drawing samples the resulting appearance atlas directly; it does not recompute
    blends or bind one texture per overlay instance.
    """
    def __init__(self, tile_store, overlay_instances):
        self.tile_store = tile_store
        self.refs = sorted({int(item.tile_or_blend_ref) for item in overlay_instances})
        self.page_surfaces = []
        self.page_textures = []
        self.regions = {}
        self._build_pages()

    def _compose_ref(self, packed_ref):
        primary_id = int(packed_ref) & FLOOR_PRIMARY_TILE_MASK
        mask_id = (int(packed_ref) >> FLOOR_SECONDARY_TILE_SHIFT) & FLOOR_SECONDARY_TILE_MASK

        # Single-texture FLOOR records are rendered directly from the primary field.
        primary = self.tile_store.get_tile_surface(primary_id)
        if primary is None:
            return None
        if mask_id == 0:
            return primary.copy()

        # The renderer passes the primary tile as texture stage 0 and the mask
        # tile as stage 1. Preserve the primary artwork and use the transition
        # texture alpha when building the resident single-texture atlas.
        mask = self.tile_store.get_tile_surface(mask_id)
        if mask is None:
            return primary.copy()

        result = primary.copy()
        if mask.get_size() != result.get_size():
            mask = pygame.transform.scale(mask, result.get_size())
        mask_alpha = pygame.surfarray.array_alpha(mask)
        result_alpha = pygame.surfarray.pixels_alpha(result)
        result_alpha[:, :] = mask_alpha
        del result_alpha
        return result

    def _build_pages(self):
        appearances = []
        packed = ordinary = missing = 0
        for packed_ref in self.refs:
            surf = self._compose_ref(packed_ref)
            if surf is None:
                missing += 1
                continue
            appearances.append((packed_ref, surf))
            if (packed_ref >> FLOOR_SECONDARY_TILE_SHIFT) & FLOOR_SECONDARY_TILE_MASK:
                packed += 1
            else:
                ordinary += 1
        if not appearances:
            print("FLOOR appearance atlas: no drawable materials.")
            return

        tile_w, tile_h = appearances[0][1].get_size()
        gl_limit = int(glGetIntegerv(GL_MAX_TEXTURE_SIZE))
        requested = min(int(GROUND_ATLAS_MAX_PAGE_SIZE), gl_limit)
        columns = max(1, requested // (tile_w + GROUND_ATLAS_PADDING))
        rows = max(1, requested // (tile_h + GROUND_ATLAS_PADDING))
        page_w = columns * (tile_w + GROUND_ATLAS_PADDING)
        page_h = rows * (tile_h + GROUND_ATLAS_PADDING)
        capacity = columns * rows
        for index, (packed_ref, surface) in enumerate(appearances):
            page_index = index // capacity
            slot = index % capacity
            while len(self.page_surfaces) <= page_index:
                self.page_surfaces.append(pygame.Surface((page_w, page_h), pygame.SRCALPHA, 32))
            x = (slot % columns) * (tile_w + GROUND_ATLAS_PADDING)
            y = (slot // columns) * (tile_h + GROUND_ATLAS_PADDING)
            self.page_surfaces[page_index].blit(surface, (x, y))
            self.regions[packed_ref] = GroundAtlasRegion(
                page=page_index,
                u_left=x / page_w,
                u_right=(x + tile_w) / page_w,
                v_top=1.0 - y / page_h,
                v_bottom=1.0 - (y + tile_h) / page_h,
            )
        print(
            f"FLOOR appearance atlas built: materials={len(appearances)}, packed={packed}, "
            f"ordinary={ordinary}, missing={missing}, pages={len(self.page_surfaces)}"
        )

    def upload_all(self):
        self.clear_gl()
        for page in self.page_surfaces:
            self.page_textures.append(surface_to_gl_texture(page, linear=False))
        print(f"FLOOR appearance atlas VRAM upload complete: {len(self.page_textures)} page(s)")

    def region(self, packed_ref):
        return self.regions.get(int(packed_ref))

    def texture_for_page(self, page_index):
        return self.page_textures[page_index] if 0 <= page_index < len(self.page_textures) else None

    def release_cpu_pages(self):
        self.page_surfaces.clear()

    def clear_gl(self):
        for texture in self.page_textures:
            if texture is not None:
                texture.delete()
        self.page_textures.clear()

    def shutdown(self):
        self.clear_gl()
        self.page_surfaces.clear()
        self.regions.clear()


class CompiledFloorMeshStore:
    """Compile one FLOOR overlay display list per visible LOD1 sector."""
    def __init__(self, overlay_instances_by_sector, atlas_store):
        self.overlay_instances_by_sector = overlay_instances_by_sector
        self.atlas_store = atlas_store
        self.batches_by_sector = OrderedDict()
        self.compile_queue = deque()
        self.queued_sector_ids = set()

    def _delete_batches(self, batches):
        for batch in batches:
            if batch.list_id:
                glDeleteLists(batch.list_id, 1)

    def _compile_sector(self, sector_id):
        items = [
            item for item in self.overlay_instances_by_sector.get(sector_id, ())
            if self.atlas_store.region(item.tile_or_blend_ref) is not None
        ]
        if not items:
            return []
        list_id = glGenLists(1)
        if not list_id:
            return []
        glNewList(list_id, GL_COMPILE)
        current_page = None
        for item in items:
            region = self.atlas_store.region(item.tile_or_blend_ref)
            if region.page != current_page:
                texture = self.atlas_store.texture_for_page(region.page)
                if texture is None:
                    continue
                glBindTexture(GL_TEXTURE_2D, texture.tex_id)
                current_page = region.page
            _emit_atlas_ground_diamond(item.iso_x, item.iso_y, region, item.corner_tints)
        glBindTexture(GL_TEXTURE_2D, 0)
        glColor4f(1.0, 1.0, 1.0, 1.0)
        glEndList()
        return [CompiledGroundBatch(
            list_id=list_id,
            min_x=min(item.iso_x for item in items),
            min_y=min(item.iso_y for item in items),
            max_x=max(item.iso_x + ISO_STEP_W + 1 for item in items),
            max_y=max(item.iso_y + ISO_STEP_H + 1 for item in items),
            tile_count=len(items),
        )]

    def request_visible(self, visible_entries):
        for entry in visible_entries:
            sid = entry["sector"]["sector_id"]
            if sid in self.batches_by_sector:
                self.batches_by_sector.move_to_end(sid)
            elif sid not in self.queued_sector_ids and self.overlay_instances_by_sector.get(sid):
                self.compile_queue.append(sid)
                self.queued_sector_ids.add(sid)

    def compile_pending(self, limit=FLOOR_COMPILES_PER_FRAME):
        compiled = 0
        while self.compile_queue and compiled < int(limit):
            sid = self.compile_queue.popleft()
            self.queued_sector_ids.discard(sid)
            if sid in self.batches_by_sector:
                continue
            self.batches_by_sector[sid] = self._compile_sector(sid)
            self.batches_by_sector.move_to_end(sid)
            while len(self.batches_by_sector) > FLOOR_COMPILED_SECTOR_CACHE_LIMIT:
                _old_sid, old_batches = self.batches_by_sector.popitem(last=False)
                self._delete_batches(old_batches)
            compiled += 1
        return compiled

    def draw(self, screen, visible_entries, zoom, pan_x, pan_y, enabled=True):
        if not enabled:
            return 0, 0
        self.request_visible(visible_entries)
        drawn = visible = 0
        glEnable(GL_TEXTURE_2D)
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)
        glMatrixMode(GL_MODELVIEW)
        glPushMatrix()
        glTranslatef(float(pan_x), float(pan_y), 0.0)
        glScalef(float(zoom), float(zoom), 1.0)
        for entry in visible_entries:
            sid = entry["sector"]["sector_id"]
            for batch in self.batches_by_sector.get(sid, ()):
                glCallList(batch.list_id)
                drawn += batch.tile_count
                visible += batch.tile_count
        glPopMatrix()
        glBindTexture(GL_TEXTURE_2D, 0)
        glColor4f(1.0, 1.0, 1.0, 1.0)
        return drawn, visible

    def clear(self):
        for batches in self.batches_by_sector.values():
            self._delete_batches(batches)
        self.batches_by_sector.clear()
        self.compile_queue.clear()
        self.queued_sector_ids.clear()

    def shutdown(self):
        self.clear()


def draw_floor_overlays_gl(screen, visible_entries, floor_mesh_store, zoom, pan_x, pan_y, enabled):
    return floor_mesh_store.draw(screen, visible_entries, zoom, pan_x, pan_y, enabled=enabled)


class PreloadedGLObjectTextureStore:
    """Upload STATIC/MIXED textures incrementally while the cached far view is usable."""
    def __init__(self, source_blobs=None):
        self.source_blobs = dict(source_blobs or {})
        self.textures = {}
        self.pending = deque()
        self.release_sources_when_done = False
        self._announced_complete = False

    def __len__(self):
        return len(self.textures)

    def get(self, group_id):
        return self.textures.get(int(group_id))

    def set_sources(self, source_blobs):
        self.source_blobs = dict(source_blobs)
        self.pending.clear()
        self._announced_complete = False

    def begin_upload(self, release_cpu_sources=False):
        self.clear()
        self.pending = deque(sorted(self.source_blobs.items()))
        self.release_sources_when_done = bool(release_cpu_sources)
        self._announced_complete = False
        print(f"Queued {len(self.pending)} unique STATIC/MIXED sprite texture(s) for idle VRAM upload.")

    def pump(self, limit=LOD1_OBJECT_TEXTURE_UPLOADS_PER_IDLE_TICK):
        uploaded = 0
        while self.pending and uploaded < max(0, int(limit)):
            group_id, blob = self.pending.popleft()
            self.textures[group_id] = surface_to_gl_texture(
                blob_to_surface(blob), linear=STATIC_OBJECTS_LINEAR_FILTER
            )
            uploaded += 1
        if not self.pending and not self._announced_complete:
            estimated_bytes = sum(tex.width * tex.height * 4 for tex in self.textures.values())
            print(
                f"Object VRAM background preload complete: {len(self.textures)} texture(s), "
                f"estimated RGBA8 storage={estimated_bytes / (1024 * 1024):.2f} MiB"
            )
            if self.release_sources_when_done:
                self.source_blobs.clear()
                print("Released composed object RGBA sources after background VRAM upload.")
            self._announced_complete = True
        return uploaded

    def upload_all(self, release_cpu_sources=False):
        self.begin_upload(release_cpu_sources=release_cpu_sources)
        total = len(self.pending)
        self.pump(total)
        return total

    def clear(self):
        for texture in self.textures.values():
            if texture is not None:
                texture.delete()
        self.textures.clear()
        self.pending.clear()

    def shutdown(self):
        self.clear()
        self.source_blobs.clear()




def rebuild_object_texture_sources(prepared_sprites, sprite_store):
    """Recreate unique object RGBA sources only after a new OpenGL context is made."""
    blobs = {}
    for group_id in sorted({item.group_id for item in prepared_sprites}):
        sprite = sprite_store.get_sprite(group_id)
        if sprite is not None:
            blobs[group_id] = sprite.blob
    return blobs


class AsyncGLTextureCache:
    """Throttled RAM->OpenGL texture cache for startup-generated in-memory blobs."""
    def __init__(self, uploads_per_frame=ASYNC_TEXTURE_UPLOADS_PER_FRAME):
        self.uploads_per_frame = max(1, int(uploads_per_frame))
        self.textures = {}
        self.pending = {}
        self.failed = set()

    def __len__(self):
        return len(self.textures)

    def get(self, key):
        return self.textures.get(key)

    def request(self, key, blob, linear=True):
        if key in self.textures or key in self.pending or key in self.failed:
            return
        if blob is None:
            self.failed.add(key)
            return
        self.pending[key] = (blob, bool(linear))

    def pump(self, upload_limit=None):
        limit = self.uploads_per_frame if upload_limit is None else max(0, int(upload_limit))
        uploaded = 0
        for key, (blob, linear) in list(self.pending.items()):
            if uploaded >= limit:
                break
            self.pending.pop(key, None)
            try:
                surf = blob_to_surface(blob)
                self.textures[key] = surface_to_gl_texture(surf, linear=linear)
                uploaded += 1
            except Exception as e:
                print(f"RAM texture upload failed for {key}: {e}")
                self.failed.add(key)
        return uploaded

    def keep_only(self, keys, keep_pending=True):
        keys = set(keys)
        for key, tex in list(self.textures.items()):
            if key not in keys:
                if tex is not None:
                    tex.delete()
                self.textures.pop(key, None)
        if not keep_pending:
            for key in list(self.pending.keys()):
                if key not in keys:
                    self.pending.pop(key, None)

    def clear(self):
        for _key, tex in list(self.textures.items()):
            if tex is not None:
                tex.delete()
        self.textures.clear()
        self.pending.clear()
        self.failed.clear()

    def shutdown(self):
        self.clear()


def draw_textured_quad(tex, x, y, w=None, h=None, alpha=1.0):
    if tex is None or not tex.tex_id:
        return
    if w is None:
        w = tex.width
    if h is None:
        h = tex.height
    glEnable(GL_TEXTURE_2D)
    glBindTexture(GL_TEXTURE_2D, tex.tex_id)
    glColor4f(1.0, 1.0, 1.0, alpha)
    glBegin(GL_QUADS)
    glTexCoord2f(0.0, 1.0); glVertex2f(x, y)
    glTexCoord2f(1.0, 1.0); glVertex2f(x + w, y)
    glTexCoord2f(1.0, 0.0); glVertex2f(x + w, y + h)
    glTexCoord2f(0.0, 0.0); glVertex2f(x, y + h)
    glEnd()
    glBindTexture(GL_TEXTURE_2D, 0)


def _emit_liquid_main_diamond(x, y, w, h, corner_tints, corner_alpha, alpha_scale=1.0):
    left_t, top_t, right_t, bottom_t = [float(v) for v in corner_tints]
    left_a, top_a, right_a, bottom_a = [
        max(0.0, min(1.0, (float(v) / 255.0) * float(alpha_scale))) for v in corner_alpha
    ]
    center_t = (left_t + top_t + right_t + bottom_t) * 0.25
    center_a = (left_a + top_a + right_a + bottom_a) * 0.25
    cx, cy = x + w * 0.5, y + h * 0.5
    vertices = (
        (center_t, center_a, 0.5, 0.5, cx, cy),
        (top_t, top_a, 0.5, 1.0, cx, y),
        (right_t, right_a, 1.0, 0.5, x + w, cy),
        (bottom_t, bottom_a, 0.5, 0.0, cx, y + h),
        (left_t, left_a, 0.0, 0.5, x, cy),
    )
    for tri in ((0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 1)):
        for idx in tri:
            tint, alpha, u, v, vx, vy = vertices[idx]
            glColor4f(*tint_rgbf(tint, alpha))
            glTexCoord2f(u, v)
            glVertex2f(vx, vy)


def _emit_liquid_caustic_diamond(x, y, w, h, corner_intensity, intensity_scale=1.0, expand_pixels=0.0):
    expand_pixels = float(expand_pixels)
    x, y = float(x) - expand_pixels, float(y) - expand_pixels
    w, h = float(w) + expand_pixels * 2.0, float(h) + expand_pixels * 2.0
    left_i, top_i, right_i, bottom_i = [
        max(0.0, min(1.0, float(v) / 255.0 * float(intensity_scale)))
        for v in corner_intensity
    ]
    center_i = (left_i + top_i + right_i + bottom_i) * 0.25
    cx, cy = x + w * 0.5, y + h * 0.5
    vertices = (
        (center_i, 0.5, 0.5, cx, cy),
        (top_i, 0.5, 1.0, cx, y),
        (right_i, 1.0, 0.5, x + w, cy),
        (bottom_i, 0.5, 0.0, cx, y + h),
        (left_i, 0.0, 0.5, x, cy),
    )
    for tri in ((0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 1)):
        for idx in tri:
            intensity, u, v, vx, vy = vertices[idx]
            glColor4f(intensity, intensity, intensity, 1.0)
            glTexCoord2f(u, v)
            glVertex2f(vx, vy)




def liquid_main_texture_name(liquid_surface_store, item):
    return liquid_surface_store.texture_name(
        item.liquid_kind, item.texture_family, item.texture_kind
    )


def build_serializable_liquid_geometry(candidates_by_sector, liquid_surface_store):
    """Prepare LOD1 liquid vertex inputs once; safe to pickle and reuse next launch.

    OpenGL display-list IDs cannot be persisted across contexts. These CPU geometry
    records can: subsequent runs only compile currently visible sector lists.
    """
    result = {}
    tile_total = 0
    for sector_id, unsorted_items in candidates_by_sector.items():
        items = sorted(unsorted_items, key=lambda c: (c.iso_y, c.iso_x, c.local_y, c.local_x))
        counts = {"water": 0, "lava": 0}
        details = []
        main = defaultdict(list)
        for item in items:
            counts[item.liquid_kind] = counts.get(item.liquid_kind, 0) + 1
            x, y, w, h = liquid_projected_rect(item.iso_x, item.iso_y, 1.0)
            texture_name = liquid_main_texture_name(liquid_surface_store, item)
            alpha = liquid_reorder_for_projection(
                liquid_corner_scaled_values(item.corner_liquid_raw, item.main_alpha_multiplier)
            )
            main[texture_name].append((x, y, w, h, tuple(item.corner_tints), tuple(alpha)))
            if item.detail_enabled:
                intensity = liquid_reorder_for_projection(
                    liquid_corner_scaled_values(
                        item.corner_liquid_raw, LIQUID_SECOND_PASS_INTENSITY_MULTIPLIER
                    )
                )
                details.append((x, y, w, h, tuple(intensity)))
            tile_total += 1
        result[int(sector_id)] = {
            "counts": counts,
            "detail": details,
            "main": dict(main),
        }
    print(f"Prepared serializable LOD1 liquid geometry: tiles={tile_total}, sectors={len(result)}")
    return result


class PreloadedGLLiquidTextureStore:
    """Keep the currently selected liquid style textures and CAUST frame resident in VRAM."""
    def __init__(self, liquid_surface_store, candidates):
        self.liquid_surface_store = liquid_surface_store
        self.main_blobs = {}
        self.main_textures = {}
        self.caustic_name = liquid_surface_store.caustic_texture_name()
        self.caustic_blob = None
        self.caustic_texture = None

        for item in candidates:
            name = liquid_main_texture_name(liquid_surface_store, item)
            if name not in self.main_blobs:
                self.main_blobs[name] = liquid_surface_store.get_blob(
                    item.liquid_kind, item.texture_family, item.texture_kind
                )
            if item.detail_enabled and self.caustic_blob is None:
                self.caustic_blob = liquid_surface_store.get_caustic_blob()

    def upload_all(self):
        self.clear()
        for name, blob in self.main_blobs.items():
            if blob is not None:
                self.main_textures[name] = surface_to_gl_texture(
                    blob_to_surface(blob), linear=LIQUID_LINEAR_FILTER
                )
        if self.caustic_blob is not None:
            self.caustic_texture = surface_to_gl_texture(
                blob_to_surface(self.caustic_blob), linear=LIQUID_LINEAR_FILTER
            )
        print(
            f"Liquid VRAM preload complete: main={len(self.main_textures)}, "
            f"caustic={'1' if self.caustic_texture is not None else '0'}"
        )

    def get_main(self, texture_name):
        return self.main_textures.get(texture_name)

    def clear(self):
        for texture in self.main_textures.values():
            if texture is not None:
                texture.delete()
        self.main_textures.clear()
        if self.caustic_texture is not None:
            self.caustic_texture.delete()
            self.caustic_texture = None

    def shutdown(self):
        self.clear()
        self.main_blobs.clear()
        self.caustic_blob = None


class CompiledLiquidMeshStore:
    """Lazily compile cached liquid geometry only for visible LOD1 sectors.

    Geometry records are persisted on disk; OpenGL display lists are context-local
    and are therefore rebuilt only as a visible sector is visited.
    """
    def __init__(self, geometry_by_sector, texture_store):
        self.geometry_by_sector = geometry_by_sector
        self.texture_store = texture_store
        self.caustic_lists = {}
        self.main_lists_by_texture = defaultdict(dict)
        self.compiled_sector_ids = OrderedDict()
        self.compile_queue = deque()
        self.queued_sector_ids = set()

    @staticmethod
    def _compile_list(emit_callback):
        list_id = glGenLists(1)
        if not list_id:
            return 0
        glNewList(list_id, GL_COMPILE)
        glBegin(GL_TRIANGLES)
        emit_callback()
        glEnd()
        glEndList()
        return list_id

    def _drop_sector(self, sector_id):
        list_id = self.caustic_lists.pop(sector_id, None)
        if list_id:
            glDeleteLists(list_id, 1)
        for by_sector in self.main_lists_by_texture.values():
            list_id = by_sector.pop(sector_id, None)
            if list_id:
                glDeleteLists(list_id, 1)
        self.compiled_sector_ids.pop(sector_id, None)

    def _compile_sector(self, sector_id):
        geometry = self.geometry_by_sector.get(int(sector_id))
        if not geometry:
            self.compiled_sector_ids[sector_id] = True
            return
        detail_records = geometry.get("detail", ())
        if detail_records and self.texture_store.caustic_texture is not None:
            def emit_detail(records=detail_records):
                for x, y, w, h, intensity in records:
                    _emit_liquid_caustic_diamond(
                        x, y, w, h, intensity,
                        intensity_scale=LIQUID_SECOND_PASS_STRENGTH,
                        expand_pixels=LIQUID_SECOND_PASS_PIXEL_EXPAND,
                    )
            list_id = self._compile_list(emit_detail)
            if list_id:
                self.caustic_lists[sector_id] = list_id

        for texture_name, records in geometry.get("main", {}).items():
            if self.texture_store.get_main(texture_name) is None:
                continue
            def emit_main(records=records):
                for x, y, w, h, corner_tints, alpha in records:
                    _emit_liquid_main_diamond(
                        x, y, w, h, corner_tints, alpha,
                        alpha_scale=LIQUID_ALPHA_GLOBAL_SCALE,
                    )
            list_id = self._compile_list(emit_main)
            if list_id:
                self.main_lists_by_texture[texture_name][sector_id] = list_id

        self.compiled_sector_ids[sector_id] = True
        self.compiled_sector_ids.move_to_end(sector_id)
        while len(self.compiled_sector_ids) > GROUND_COMPILED_SECTOR_CACHE_LIMIT:
            oldest, _ = self.compiled_sector_ids.popitem(last=False)
            self._drop_sector(oldest)

    def request_visible(self, visible_entries):
        for entry in visible_entries:
            sid = int(entry["sector"]["sector_id"])
            if sid in self.compiled_sector_ids:
                self.compiled_sector_ids.move_to_end(sid)
            elif sid not in self.queued_sector_ids and sid in self.geometry_by_sector:
                self.compile_queue.append(sid)
                self.queued_sector_ids.add(sid)

    def compile_pending(self, limit=LOD1_IDLE_LIQUID_COMPILES_PER_TICK):
        compiled = 0
        while self.compile_queue and compiled < int(limit):
            sid = self.compile_queue.popleft()
            self.queued_sector_ids.discard(sid)
            if sid not in self.compiled_sector_ids:
                self._compile_sector(sid)
                compiled += 1
        return compiled

    def draw(self, visible_entries, zoom, pan_x, pan_y, enabled=True):
        if not enabled or zoom < LOD1_ZOOM:
            return {"water": 0, "lava": 0}, {"water": 0, "lava": 0}
        self.request_visible(visible_entries)
        sector_ids = [int(entry["sector"]["sector_id"]) for entry in visible_entries]
        visible_counts = {"water": 0, "lava": 0}
        drawn_counts = {"water": 0, "lava": 0}
        for sid in sector_ids:
            counts = self.geometry_by_sector.get(sid, {}).get("counts", {})
            for kind in visible_counts:
                visible_counts[kind] += int(counts.get(kind, 0))
                if sid in self.compiled_sector_ids:
                    drawn_counts[kind] += int(counts.get(kind, 0))

        glMatrixMode(GL_MODELVIEW)
        glPushMatrix()
        glTranslatef(float(pan_x), float(pan_y), 0.0)
        glScalef(float(zoom), float(zoom), 1.0)

        if self.texture_store.caustic_texture is not None:
            glBlendFunc(GL_ONE, GL_ONE)
            glBindTexture(GL_TEXTURE_2D, self.texture_store.caustic_texture.tex_id)
            for sid in sector_ids:
                list_id = self.caustic_lists.get(sid)
                if list_id:
                    glCallList(list_id)

        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)
        for texture_name, lists_by_sector in self.main_lists_by_texture.items():
            texture = self.texture_store.get_main(texture_name)
            if texture is None:
                continue
            glBindTexture(GL_TEXTURE_2D, texture.tex_id)
            for sid in sector_ids:
                list_id = lists_by_sector.get(sid)
                if list_id:
                    glCallList(list_id)

        glBindTexture(GL_TEXTURE_2D, 0)
        glPopMatrix()
        glColor4f(1.0, 1.0, 1.0, 1.0)
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)
        return drawn_counts, visible_counts

    def clear(self):
        for list_id in self.caustic_lists.values():
            if list_id:
                glDeleteLists(list_id, 1)
        self.caustic_lists.clear()
        for lists_by_sector in self.main_lists_by_texture.values():
            for list_id in lists_by_sector.values():
                if list_id:
                    glDeleteLists(list_id, 1)
        self.main_lists_by_texture.clear()
        self.compiled_sector_ids.clear()
        self.compile_queue.clear()
        self.queued_sector_ids.clear()

    def shutdown(self):
        self.clear()



def draw_liquid_surfaces_gl(visible_entries, liquid_mesh_store, zoom, pan_x, pan_y, enabled=True):
    return liquid_mesh_store.draw(visible_entries, zoom, pan_x, pan_y, enabled=enabled)


def draw_solid_rect(x, y, w, h, color):
    r, g, b, a = color
    glDisable(GL_TEXTURE_2D)
    glColor4f(r / 255.0, g / 255.0, b / 255.0, a / 255.0)
    glBegin(GL_QUADS)
    glVertex2f(x, y)
    glVertex2f(x + w, y)
    glVertex2f(x + w, y + h)
    glVertex2f(x, y + h)
    glEnd()
    glEnable(GL_TEXTURE_2D)


def draw_line_loop(points, color, width=1):
    r, g, b = color[:3]
    a = color[3] if len(color) > 3 else 255
    glDisable(GL_TEXTURE_2D)
    glLineWidth(width)
    glColor4f(r / 255.0, g / 255.0, b / 255.0, a / 255.0)
    glBegin(GL_LINE_LOOP)
    for x, y in points:
        glVertex2f(x, y)
    glEnd()
    glEnable(GL_TEXTURE_2D)


def draw_text_gl(font, text, pos, color=(230, 230, 230), cache=None):
    """Render text as an OpenGL texture.

    Pass a cache for mostly stable text such as tooltip labels. Leave cache=None
    for rapidly changing text such as the status line so old zoom/FPS strings
    do not accumulate as GPU textures.
    """
    if cache is None:
        surf = font.render(text, True, color)
        tex = surface_to_gl_texture(surf, linear=False)
        try:
            draw_textured_quad(tex, pos[0], pos[1])
            return tex.width, tex.height
        finally:
            tex.delete()

    key = (text, color, font.get_height())
    tex = cache.get(key)
    if tex is None:
        surf = font.render(text, True, color)
        tex = surface_to_gl_texture(surf, linear=False)
        cache[key] = tex
    draw_textured_quad(tex, pos[0], pos[1])
    return tex.width, tex.height


def save_opengl_screenshot(path, width, height):
    data = glReadPixels(0, 0, int(width), int(height), GL_RGBA, GL_UNSIGNED_BYTE)
    surf = pygame.image.fromstring(data, (int(width), int(height)), "RGBA", True)
    pygame.image.save(surf, path)


def release_gl_texture_cache(cache):
    if hasattr(cache, "clear") and not isinstance(cache, dict):
        cache.clear()
        return
    for tex in cache.values():
        if isinstance(tex, GLTexture):
            tex.delete()
    cache.clear()

# ============================================================
# VIEWER HELPERS
# ============================================================

def build_sector_draw_items(sec):
    # Precompute immutable tile draw order/positions once instead of rebuilding
    # and sorting 4096 items per visible sector on every redraw.
    items = []
    tiles = sec["tiles"]
    for ly in range(SECTOR_H):
        for lx in range(SECTOR_W):
            off = (ly * SECTOR_W + lx) * TILE_DESC_SIZE
            tile_id = u32(tiles, off)
            wx = sec["origin_x"] + lx
            wy = sec["origin_y"] + ly
            ix, iy = world_to_iso(wx, wy)
            corner_heights = terrain_tile_corner_heights(tiles, off)
            corner_tints = terrain_tile_corner_tints(tiles, off)
            average_height = sum(corner_heights) * 0.25
            items.append((wx + wy, wy, ix, iy, tile_id, corner_heights, corner_tints, average_height))
    items.sort()
    return items


def build_sector_entries(sectors):
    return [
        {"sector": sec, "iso_bounds": sector_iso_bounds(sec)}
        for sec in sectors
    ]


def compute_global_bounds(sector_entries):
    min_x = min(c["iso_bounds"][0] for c in sector_entries)
    min_y = min(c["iso_bounds"][1] for c in sector_entries)
    max_x = max(c["iso_bounds"][2] for c in sector_entries)
    max_y = max(c["iso_bounds"][3] for c in sector_entries)
    return min_x, min_y, max_x, max_y


def fit_camera(screen, bounds):
    min_x, min_y, max_x, max_y = bounds
    screen_w, screen_h = get_display_size(screen)
    w = max_x - min_x
    h = max_y - min_y
    zoom = min(screen_w / w, screen_h / h) * 0.9
    zoom = max(0.001, min(zoom, 1.0))
    pan_x = screen_w / 2 - ((min_x + max_x) / 2) * zoom
    pan_y = screen_h / 2 - ((min_y + max_y) / 2) * zoom
    return zoom, pan_x, pan_y


def focus_camera_on_sector(screen, sector_entries, sector_id, zoom):
    screen_w, screen_h = get_display_size(screen)
    chosen = None
    for entry in sector_entries:
        if entry["sector"]["sector_id"] == sector_id:
            chosen = entry
            break

    if chosen is None and sector_entries:
        chosen = sector_entries[0]

    if chosen is None:
        return zoom, 0, 0

    ix0, iy0, ix1, iy1 = chosen["iso_bounds"]
    cx = (ix0 + ix1) / 2
    cy = (iy0 + iy1) / 2

    pan_x = screen_w / 2 - cx * zoom
    pan_y = screen_h / 2 - cy * zoom
    return zoom, pan_x, pan_y


def choose_sector_lod_factor(zoom):
    if zoom >= LOD1_ZOOM:
        return 1
    if zoom >= LOD8_ZOOM:
        return 8
    return OVERVIEW_LOD_FACTOR


def screen_to_world_tile(mx, my, pan_x, pan_y, zoom):
    ix = (mx - pan_x) / zoom
    iy = (my - pan_y) / zoom
    a = ix / (ISO_STEP_W / 2)
    b = iy / (ISO_STEP_H / 2)
    wx = int(round((a + b) / 2))
    wy = int(round((b - a) / 2))
    return wx, wy


def get_hover_info_at_fast(sector_by_grid, tiles_pak, wx, wy):
    gx = wx // SECTOR_W
    gy = wy // SECTOR_H
    entry = sector_by_grid.get((gx, gy))
    if entry is None:
        return None

    sec = entry["sector"]
    lx = wx - sec["origin_x"]
    ly = wy - sec["origin_y"]
    if not (0 <= lx < SECTOR_W and 0 <= ly < SECTOR_H):
        return None

    off = (ly * SECTOR_W + lx) * TILE_DESC_SIZE
    tile_id = u32(sec["tiles"], off)
    floor_head_id = u32(sec["tiles"], off + 0x0C)
    corner_heights = terrain_tile_corner_heights(sec["tiles"], off)
    average_height = sum(corner_heights) * 0.25
    corner_tints = terrain_tile_corner_tints(sec["tiles"], off)
    tile_def = tiles_pak.get(tile_id)
    return {
        "sector_id": sec["sector_id"],
        "local": (lx, ly),
        "ppos": (wx, wy),
        "tile_id": tile_id,
        "floor_head_id": floor_head_id,
        "height_corners": corner_heights,
        "height_average": average_height,
        "corner_tints": corner_tints,
        "texture": tile_def.filename if tile_def else "<none>",
        "texture_tile_number": tile_def.tile_number if tile_def else -1,
    }


def draw_tooltip_gl(screen, font, lines, pos, text_cache):
    screen_w, screen_h = get_display_size(screen)
    mx, my = pos
    padding = 6
    line_h = font.get_height() + 2
    width = max(font.size(line)[0] for line in lines) + padding * 2
    height = len(lines) * line_h + padding * 2
    x = mx + 16
    y = my + 16
    if x + width > screen_w:
        x = mx - width - 16
    if y + height > screen_h:
        y = my - height - 16

    draw_solid_rect(x, y, width, height, (10, 10, 10, 220))
    draw_line_loop([(x, y), (x + width, y), (x + width, y + height), (x, y + height)], (230, 230, 230, 255))
    for i, line in enumerate(lines):
        draw_text_gl(font, line, (x + padding, y + padding + i * line_h), (245, 245, 245), text_cache)


def draw_sector_lod_images_gl(screen, visible_entries, zoom, pan_x, pan_y, previous_visible_cache, lod8_store):
    screen_w, screen_h = get_display_size(screen)
    new_cache = {}
    visible_count = 0
    lod8_store.request_visible(visible_entries)

    for entry in visible_entries:
        sec = entry["sector"]
        ix0, iy0, ix1, iy1 = entry["iso_bounds"]
        sx = pan_x + ix0 * zoom
        sy = pan_y + iy0 * zoom
        sw = (ix1 - ix0) * zoom
        sh = (iy1 - iy0) * zoom

        if sx + sw < 0 or sy + sh < 0 or sx > screen_w or sy > screen_h:
            continue

        key = (sec["sector_id"], 8)
        blob = lod8_store.get(sec["sector_id"])
        if blob is None:
            continue

        new_cache[key] = True
        tex = previous_visible_cache.get(key)
        if tex is None:
            previous_visible_cache.request(key, blob, linear=True)
            continue

        draw_textured_quad(tex, int(round(sx)), int(round(sy)), max(1, int(round(sw))), max(1, int(round(sh))))
        visible_count += 1

    previous_visible_cache.keep_only(new_cache.keys(), keep_pending=True)
    return previous_visible_cache, visible_count



def visible_overview_chunk_entries(overview_entries, zoom, pan_x, pan_y, screen):
    screen_w, screen_h = get_display_size(screen)
    result = []
    for entry in overview_entries:
        ix0, iy0, ix1, iy1 = entry["iso_bounds"]
        sx = pan_x + ix0 * zoom
        sy = pan_y + iy0 * zoom
        sw = (ix1 - ix0) * zoom
        sh = (iy1 - iy0) * zoom
        if sx + sw < 0 or sy + sh < 0 or sx > screen_w or sy > screen_h:
            continue
        result.append(entry)
    return result


def draw_overview_chunks_gl(screen, visible_chunks, zoom, pan_x, pan_y, texture_cache, overview_store):
    screen_w, screen_h = get_display_size(screen)
    visible_keys = []
    drawn = 0
    overview_store.request_visible(visible_chunks)
    for entry in visible_chunks:
        ix0, iy0, ix1, iy1 = entry["iso_bounds"]
        sx = pan_x + ix0 * zoom
        sy = pan_y + iy0 * zoom
        sw = (ix1 - ix0) * zoom
        sh = (iy1 - iy0) * zoom
        if sx + sw < 0 or sy + sh < 0 or sx > screen_w or sy > screen_h:
            continue
        key = entry["key"]
        blob = overview_store.get(key)
        if blob is None:
            continue
        visible_keys.append(key)
        tex = texture_cache.get(key)
        if tex is None:
            texture_cache.request(key, blob, linear=True)
            continue
        draw_textured_quad(tex, int(sx), int(sy), max(1, int(sw)), max(1, int(sh)))
        drawn += 1
    texture_cache.keep_only(visible_keys, keep_pending=True)
    return drawn


class SectorVisibilityIndex:
    """Sector-grid spatial index: camera queries touch nearby cells, not the whole world."""
    def __init__(self, sector_entries):
        self.entries_by_grid = {
            (int(entry["sector"]["grid_x"]), int(entry["sector"]["grid_y"])): entry
            for entry in sector_entries
        }

    @staticmethod
    def _iso_to_world(ix, iy):
        return (ix / ISO_STEP_W + iy / ISO_STEP_H,
                -ix / ISO_STEP_W + iy / ISO_STEP_H)

    def visible_entries(self, zoom, pan_x, pan_y, screen):
        screen_w, screen_h = get_display_size(screen)
        inv_zoom = 1.0 / max(float(zoom), 1e-9)
        ix0, iy0 = (-pan_x) * inv_zoom, (-pan_y) * inv_zoom
        ix1, iy1 = (screen_w - pan_x) * inv_zoom, (screen_h - pan_y) * inv_zoom
        world_corners = [
            self._iso_to_world(ix0, iy0), self._iso_to_world(ix1, iy0),
            self._iso_to_world(ix0, iy1), self._iso_to_world(ix1, iy1),
        ]
        # Two-cell pad covers the projected tile footprint and overscan.
        pad = 2
        gx0 = math.floor(min(p[0] for p in world_corners) / SECTOR_W) - pad
        gx1 = math.floor(max(p[0] for p in world_corners) / SECTOR_W) + pad
        gy0 = math.floor(min(p[1] for p in world_corners) / SECTOR_H) - pad
        gy1 = math.floor(max(p[1] for p in world_corners) / SECTOR_H) + pad
        result = []
        for gy in range(int(gy0), int(gy1) + 1):
            for gx in range(int(gx0), int(gx1) + 1):
                entry = self.entries_by_grid.get((gx, gy))
                if entry is None:
                    continue
                bx0, by0, bx1, by1 = entry["iso_bounds"]
                sx, sy = pan_x + bx0 * zoom, pan_y + by0 * zoom
                sw, sh = (bx1 - bx0) * zoom, (by1 - by0) * zoom
                if sx + sw < 0 or sy + sh < 0 or sx > screen_w or sy > screen_h:
                    continue
                result.append(entry)
        return result


def visible_sector_entries(sector_index, zoom, pan_x, pan_y, screen):
    return sector_index.visible_entries(zoom, pan_x, pan_y, screen)


@dataclass
class Lod1CpuAssets:
    tile_sources: dict
    tile_store: object
    liquid_surface_store: object
    prepared_static_sprites: list
    object_source_blobs: dict
    tiles_pak: object
    mixed_pak: object
    items_pak: object
    texture_pak: object


def build_lod1_cpu_assets(
    live_vram_tile_ids, base_tile_ids, floor_overlay_instances,
    liquid_surface_candidates, static_render_records, cached_prepared_static=None
):
    """Archive decoding/composition stage. This runs off the render loop."""
    worker_tiles_pak = TilesPak(TILES_PAK_PATH)
    worker_texture_pak = TexturePak(TEXTURE_PAK_PATH)
    worker_mixed_pak = MixedPak2D(MIXED_PAK_PATH)
    worker_items_pak = ItemsPakTypeTable(ITEMS_PAK_PATH)
    worker_sprite_store = MixedSpriteStore(worker_texture_pak, worker_mixed_pak)

    tile_sources = build_tile_sources(live_vram_tile_ids, worker_tiles_pak, worker_texture_pak)
    tile_store = TileSurfaceStore(worker_texture_pak, tile_sources)
    tile_store.prebuild_base_tiles()
    liquid_surface_store = LiquidSurfaceStore(
        worker_texture_pak, families=LIQUID_TEXTURE_FAMILY_DEFAULT,
        frames=LIQUID_STATIC_FRAME_DEFAULT
    )
    if cached_prepared_static is None:
        prepared_static_sprites, object_source_blobs = prepare_static_sprites(
            static_render_records, worker_items_pak, worker_mixed_pak, worker_sprite_store
        )
    else:
        prepared_static_sprites = list(cached_prepared_static)
        object_source_blobs = rebuild_object_texture_sources(prepared_static_sprites, worker_sprite_store)

    worker_sprite_store.atlas_cache.clear()
    worker_sprite_store.sprite_cache.clear()
    return Lod1CpuAssets(
        tile_sources=tile_sources, tile_store=tile_store,
        liquid_surface_store=liquid_surface_store,
        prepared_static_sprites=prepared_static_sprites,
        object_source_blobs=object_source_blobs,
        tiles_pak=worker_tiles_pak,
        mixed_pak=worker_mixed_pak,
        items_pak=worker_items_pak,
        texture_pak=worker_texture_pak,
    )


def activate_lod1_gpu_assets(
    cpu_assets, cache, base_tile_ids, floor_overlay_instances,
    floor_overlay_instances_by_sector, liquid_surface_candidates,
    liquid_surface_candidates_by_sector, release_tile_staging=True
):
    """Main-thread OpenGL stage after background CPU work is ready."""
    ground_atlas_store = GroundTileAtlasStore(cpu_assets.tile_store, base_tile_ids)
    ground_atlas_store.upload_all()
    floor_atlas_store = FloorAppearanceAtlasStore(cpu_assets.tile_store, floor_overlay_instances)
    floor_atlas_store.upload_all()
    ground_atlas_store.release_cpu_pages()
    floor_atlas_store.release_cpu_pages()

    liquid_texture_store = PreloadedGLLiquidTextureStore(
        cpu_assets.liquid_surface_store, liquid_surface_candidates
    )
    liquid_texture_store.upload_all()
    cpu_assets.texture_pak.close()
    liquid_geometry = cache.load_liquid_geometry()
    if liquid_geometry is None:
        liquid_geometry = build_serializable_liquid_geometry(
            liquid_surface_candidates_by_sector, cpu_assets.liquid_surface_store
        )
        cache.save_liquid_geometry(liquid_geometry)
    liquid_mesh_store = CompiledLiquidMeshStore(liquid_geometry, liquid_texture_store)

    object_texture_store = PreloadedGLObjectTextureStore(cpu_assets.object_source_blobs)
    object_texture_store.begin_upload(release_cpu_sources=True)
    ground_mesh_store = CompiledGroundMeshStore(ground_atlas_store)
    floor_mesh_store = CompiledFloorMeshStore(floor_overlay_instances_by_sector, floor_atlas_store)
    if release_tile_staging:
        cpu_assets.tile_store.release_base_tiles()
        cpu_assets.tile_store.scaled_surfaces.clear()
    return (
        ground_atlas_store, floor_atlas_store, ground_mesh_store, floor_mesh_store,
        liquid_texture_store, liquid_mesh_store, object_texture_store,
    )




# ============================================================
# MAIN
# ============================================================

def run():

    pygame.init()
    screen = set_gl_window(WINDOW_SIZE)
    pygame.display.set_caption("Sacred map viewer")
    font = pygame.font.SysFont("consolas", 16)

    cache = PersistentViewerCache()
    print(f"Loading map index/cache... persistent cache={cache.root}")

    # Archive tables needed only by LOD1 are also deferred when far imagery exists.
    tiles_pak = None
    mixed_pak = None
    items_pak = None

    saved_world = cache.load_world()
    if saved_world is None:
        tiles_pak = TilesPak(TILES_PAK_PATH)
        mixed_pak = MixedPak2D(MIXED_PAK_PATH)
        items_pak = ItemsPakTypeTable(ITEMS_PAK_PATH)
        keyx_entries = load_keyx_entries()
        keyx_layout = build_keyx_absolute_layout(keyx_entries)
        sector_ids = sorted(sid for sid in keyx_entries if sid != 0) if SECTOR_IDS is None else list(SECTOR_IDS)
        print(f"Loading sectors... selected={'all KEYX sectors' if SECTOR_IDS is None else 'manual list'} ({len(sector_ids)} sector id(s))")
        sectors, skipped = load_sectors_parallel(sector_ids, keyx_entries, keyx_layout)
        if not sectors:
            print("No sectors loaded.")
            pygame.quit()
            return
        floor_pak = FloorPak(FLOOR_PAK_PATH)
        floor_overlay_instances, floor_overlay_tile_ids = collect_floor_overlay_instances(sectors, floor_pak, tiles_pak)
        liquid_surface_candidates = collect_animated_surface_candidates(sectors)
        base_tile_ids, _ = collect_used_tile_ids(sectors)
        static_pak = StaticPak(STATIC_PAK_PATH)
        static_render_records = collect_tile_chained_static_records(sectors, static_pak, items_pak, mixed_pak)
        cache.save_world(
            sectors=sectors, floor_overlay_instances=floor_overlay_instances,
            floor_overlay_tile_ids=set(floor_overlay_tile_ids),
            liquid_surface_candidates=liquid_surface_candidates,
            base_tile_ids=set(base_tile_ids), static_render_records=static_render_records
        )
        print(f"Loaded sectors: {len(sectors)}, skipped: {skipped}")
    else:
        sectors = saved_world["sectors"]
        floor_overlay_instances = saved_world["floor_overlay_instances"]
        floor_overlay_tile_ids = set(saved_world["floor_overlay_tile_ids"])
        liquid_surface_candidates = saved_world["liquid_surface_candidates"]
        base_tile_ids = set(saved_world["base_tile_ids"])
        static_render_records = saved_world["static_render_records"]
        skipped = 0
        print(f"Cached world loaded: sectors={len(sectors)}, FLOOR={len(floor_overlay_instances)}, liquids={len(liquid_surface_candidates)}, statics={len(static_render_records)}")

    floor_overlay_instances_by_sector = index_instances_by_sector(floor_overlay_instances)
    liquid_surface_candidates_by_sector = index_instances_by_sector(liquid_surface_candidates)
    live_vram_tile_ids = set(base_tile_ids) | set(floor_overlay_tile_ids)
    print(
        f"LOD1 tile set: base={len(base_tile_ids)}, FLOOR={len(floor_overlay_tile_ids)}, "
        f"combined={len(live_vram_tile_ids)}"
    )

    sector_entries = build_sector_entries(sectors)
    sector_visibility_index = SectorVisibilityIndex(sector_entries)
    sector_by_grid = {(entry["sector"]["grid_x"], entry["sector"]["grid_y"]): entry for entry in sector_entries}
    bounds = compute_global_bounds(sector_entries)
    overview_entries = build_overview_chunk_entries_from_layout(build_overview_chunk_layout(sector_entries))
    have_far_cache = cache.complete_lods_exist(sector_entries, overview_entries)
    sector_lod8_store = DiskLodStore(cache, overview=False)
    overview_store = DiskLodStore(cache, overview_entries=overview_entries, overview=True)

    # Live LOD1 resources may be absent while cached far views are already interactive.
    ground_atlas_store = floor_atlas_store = None
    ground_mesh_store = floor_mesh_store = None
    liquid_texture_store = liquid_mesh_store = None
    object_texture_store = None
    liquid_surface_store = None
    prepared_static_sprites = []
    prepared_static_sprites_by_sector = {}
    tile_sources = {}
    lod1_cpu_assets = None
    lod1_ready = False
    lod1_loader_executor = None
    lod1_loader_future = None
    cached_prepared_static = cache.load_prepared_static_layout()

    if have_far_cache:
        print("Persistent LOD8/LOD16 cache ready: opening immediately while LOD1 artwork prepares in background.")
        lod1_loader_executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="sacred_lod1_assets")
        lod1_loader_future = lod1_loader_executor.submit(
            build_lod1_cpu_assets,
            live_vram_tile_ids, base_tile_ids, floor_overlay_instances,
            liquid_surface_candidates, static_render_records, cached_prepared_static
        )
    else:
        print("No complete persistent far-view cache; first run must build LOD8/LOD16 before map navigation.")
        lod1_cpu_assets = build_lod1_cpu_assets(
            live_vram_tile_ids, base_tile_ids, floor_overlay_instances,
            liquid_surface_candidates, static_render_records, cached_prepared_static
        )
        if cached_prepared_static is None:
            cache.save_prepared_static_layout(lod1_cpu_assets.prepared_static_sprites)
        prepared_static_sprites = lod1_cpu_assets.prepared_static_sprites
        prepared_static_sprites_by_sector = index_instances_by_sector(prepared_static_sprites)
        tiles_pak = lod1_cpu_assets.tiles_pak
        mixed_pak = lod1_cpu_assets.mixed_pak
        items_pak = lod1_cpu_assets.items_pak
        tile_sources = lod1_cpu_assets.tile_sources
        liquid_surface_store = lod1_cpu_assets.liquid_surface_store
        (
            ground_atlas_store, floor_atlas_store, ground_mesh_store, floor_mesh_store,
            liquid_texture_store, liquid_mesh_store, object_texture_store,
        ) = activate_lod1_gpu_assets(
            lod1_cpu_assets, cache, base_tile_ids, floor_overlay_instances,
            floor_overlay_instances_by_sector, liquid_surface_candidates,
            liquid_surface_candidates_by_sector, release_tile_staging=False
        )
        disk_builder = CompositeLodDiskBuilder(
            cache, lod1_cpu_assets.tile_store, floor_atlas_store, floor_overlay_instances_by_sector,
            liquid_surface_candidates_by_sector, liquid_surface_store,
            prepared_static_sprites_by_sector, lod1_cpu_assets.object_source_blobs
        )
        disk_builder.build_lod8(sector_entries)
        overview_entries = disk_builder.build_lod16(sector_entries)
        cache.save_lod_manifest(sector_entries, overview_entries)
        del disk_builder
        lod1_cpu_assets.tile_store.release_base_tiles()
        lod1_cpu_assets.tile_store.scaled_surfaces.clear()
        lod1_ready = True

    if START_FITTED_TO_WORLD:
        zoom, pan_x, pan_y = fit_camera(screen, bounds)
    else:
        zoom, pan_x, pan_y = focus_camera_on_sector(
            screen,
            sector_entries,
            START_SECTOR_ID,
            START_ZOOM,
        )

    static_active_layer = STATIC_ACTIVE_LAYER_DEFAULT
    static_layer_view = STATIC_LAYER_VIEW_DEFAULT
    floor_overlay_mode = FLOOR_OVERLAY_MODE
    draw_liquid_surfaces = DRAW_LIQUID_SURFACES
    show_hover_info = SHOW_HOVER_INFO_DEFAULT
    print("Opening viewer...")
    print("O toggles resident STATIC/MIXED sprites; D toggles anchor markers; H toggles hover information.")
    print(f"2D STATIC object shift: x={STATIC_OBJECT_SHIFT_X:g}, y={STATIC_OBJECT_SHIFT_Y:g} (edit constants near the top of this file).")
    print("Press F to toggle FLOOR overlays; blended appearances use an atlas and lazy compiled LOD1 sector batches.")
    liquid_status = liquid_surface_store.caustic_texture_name() if liquid_surface_store is not None else "loading"
    print(f"Animated liquids: 0x90 uses KEYX +0x2E0 and 0xA0 uses KEYX +0x2E1; CAUST detail={liquid_status}; fixed gradient mapping={("left", "top", "bottom", "right")}; W toggles liquids.")
    print("PageUp enters the next building layer; PageDown descends; Home returns exterior view; I toggles exact interior/exterior filtering.")
    print(f"Flat terrain vertex shadows: {'on' if DRAW_TERRAIN_VERTEX_TINT else 'off'}; LOD1 is live; LOD8/LOD16 are persistent full-composite disk caches.")

    clock = pygame.time.Clock()
    dragging = False
    dirty = True
    last_mouse_tile = None
    visible_sector_surface_cache = AsyncGLTextureCache()
    overview_chunk_cache = AsyncGLTextureCache()
    text_cache = {}
    last_display_size = get_display_size(screen)

    running = True
    while running:
        redraw = dirty

        for e in pygame.event.get():
            if e.type == pygame.QUIT:
                running = False

            elif e.type == pygame.VIDEORESIZE:
                old_size = get_display_size(screen)
                requested_size = (max(1, int(e.w)), max(1, int(e.h)))

                # Destroy resources belonging to the old GL context.
                if ground_mesh_store is not None:
                    ground_mesh_store.shutdown()
                if floor_mesh_store is not None:
                    floor_mesh_store.shutdown()
                if ground_atlas_store is not None:
                    ground_atlas_store.shutdown()
                if floor_atlas_store is not None:
                    floor_atlas_store.shutdown()
                release_gl_texture_cache(visible_sector_surface_cache)
                release_gl_texture_cache(overview_chunk_cache)
                if object_texture_store is not None:
                    object_texture_store.clear()
                if liquid_mesh_store is not None:
                    liquid_mesh_store.clear()
                if liquid_texture_store is not None:
                    liquid_texture_store.clear()
                release_gl_texture_cache(text_cache)

                screen = set_gl_window(requested_size)

                if lod1_ready:
                    # CPU staging was discarded after startup. Rebuild it only
                    # when a resize recreates OpenGL; uploads stay incremental.
                    resize_texture_pak = TexturePak(TEXTURE_PAK_PATH)
                    resize_tile_store = TileSurfaceStore(resize_texture_pak, tile_sources)
                    resize_tile_store.prebuild_base_tiles()

                    ground_atlas_store = GroundTileAtlasStore(resize_tile_store, base_tile_ids)
                    ground_atlas_store.upload_all()
                    ground_atlas_store.release_cpu_pages()
                    ground_mesh_store = CompiledGroundMeshStore(ground_atlas_store)

                    floor_atlas_store = FloorAppearanceAtlasStore(resize_tile_store, floor_overlay_instances)
                    floor_atlas_store.upload_all()
                    floor_atlas_store.release_cpu_pages()
                    floor_mesh_store = CompiledFloorMeshStore(floor_overlay_instances_by_sector, floor_atlas_store)

                    resize_mixed_store = MixedSpriteStore(resize_texture_pak, mixed_pak)
                    object_texture_store.set_sources(
                        rebuild_object_texture_sources(prepared_static_sprites, resize_mixed_store)
                    )
                    object_texture_store.begin_upload(release_cpu_sources=True)
                    resize_mixed_store.atlas_cache.clear()
                    resize_mixed_store.sprite_cache.clear()

                    liquid_texture_store.upload_all()
                    liquid_mesh_store.clear()

                    resize_tile_store.release_base_tiles()
                    resize_texture_pak.close()

                actual_size = get_display_size(screen)
                pan_x, pan_y = recenter_after_resize(old_size, actual_size, zoom, pan_x, pan_y)
                last_display_size = actual_size
                redraw = True

            elif e.type == pygame.MOUSEWHEEL:
                old_zoom = zoom
                zoom *= 1.12 if e.y > 0 else 0.88
                zoom = max(0.001, min(zoom, 8.0))
                mx, my = pygame.mouse.get_pos()
                pan_x = mx - (mx - pan_x) * (zoom / old_zoom)
                pan_y = my - (my - pan_y) * (zoom / old_zoom)
                redraw = True

            elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1:
                dragging = True

            elif e.type == pygame.MOUSEBUTTONUP and e.button == 1:
                dragging = False

            elif e.type == pygame.MOUSEMOTION:
                if dragging:
                    pan_x += e.rel[0]
                    pan_y += e.rel[1]
                    redraw = True
                elif show_hover_info:
                    wx, wy = screen_to_world_tile(e.pos[0], e.pos[1], pan_x, pan_y, zoom)
                    if (wx, wy) != last_mouse_tile:
                        last_mouse_tile = (wx, wy)
                        redraw = True

            elif e.type == pygame.KEYDOWN:
                if e.key == pygame.K_1:
                    if pygame.key.get_mods() & pygame.KMOD_SHIFT:
                        zoom, pan_x, pan_y = fit_camera(screen, bounds)
                        print("Fitted whole world. Visible overview chunks will fill in asynchronously.")
                    else:
                        zoom, pan_x, pan_y = focus_camera_on_sector(
                            screen,
                            sector_entries,
                            START_SECTOR_ID,
                            START_ZOOM,
                        )
                    redraw = True
                elif e.key == pygame.K_o:
                    global DRAW_STATIC_MIXED_OBJECTS
                    DRAW_STATIC_MIXED_OBJECTS = not DRAW_STATIC_MIXED_OBJECTS
                    redraw = True
                elif e.key == pygame.K_d:
                    global STATIC_OBJECTS_DEBUG_ORIGINS
                    STATIC_OBJECTS_DEBUG_ORIGINS = not STATIC_OBJECTS_DEBUG_ORIGINS
                    redraw = True
                elif e.key == pygame.K_f:
                    floor_overlay_mode = "off" if floor_overlay_mode == "all" else "all"
                    print(f"FLOOR overlay mode: {floor_overlay_mode}")
                    redraw = True
                elif e.key == pygame.K_w:
                    draw_liquid_surfaces = not draw_liquid_surfaces
                    if liquid_surface_store is None:
                        print(f"Animated liquids: {'on' if draw_liquid_surfaces else 'off'} (LOD1 assets still loading)")
                    else:
                        print(f"Animated liquids: {'on' if draw_liquid_surfaces else 'off'} (0x90={liquid_surface_store.texture_name('water')}, 0xA0={liquid_surface_store.texture_name('lava')}, detail={liquid_surface_store.caustic_texture_name()})")
                    redraw = True
                elif e.key == pygame.K_h:
                    show_hover_info = not show_hover_info
                    last_mouse_tile = None
                    redraw = True
                elif e.key == pygame.K_PAGEUP:
                    static_active_layer += 1
                    static_layer_view = "interior"
                    print(f"Interior view: exact STATIC layer {static_active_layer}; outside floor overlays hidden; class-12 building walls forced behind props")
                    redraw = True
                elif e.key == pygame.K_PAGEDOWN:
                    static_active_layer = max(1, static_active_layer - 1)
                    static_layer_view = "exterior" if static_active_layer == 1 else "interior"
                    print(f"STATIC view={static_layer_view}, active layer={static_active_layer}")
                    redraw = True
                elif e.key == pygame.K_HOME:
                    static_active_layer = 1
                    static_layer_view = "exterior"
                    print("Exterior ground view restored")
                    redraw = True
                elif e.key == pygame.K_i:
                    static_layer_view = "interior" if static_layer_view != "interior" else "exterior"
                    print(f"STATIC layer view: {static_layer_view}; active layer={static_active_layer}")
                    redraw = True
                elif e.key == pygame.K_s:
                    save_opengl_screenshot("current_view.png", *get_display_size(screen))
                    print("Saved current_view.png")

        # When persistent far views exist, CPU artwork extraction runs outside
        # the render loop. Activate its GL resources only while idle; until then
        # the viewer remains interactive using the baked LOD8 image.
        if (
            not lod1_ready and lod1_loader_future is not None
            and lod1_loader_future.done() and not dragging
        ):
            try:
                lod1_cpu_assets = lod1_loader_future.result()
                if cached_prepared_static is None:
                    cache.save_prepared_static_layout(lod1_cpu_assets.prepared_static_sprites)
                prepared_static_sprites = lod1_cpu_assets.prepared_static_sprites
                prepared_static_sprites_by_sector = index_instances_by_sector(prepared_static_sprites)
                tiles_pak = lod1_cpu_assets.tiles_pak
                mixed_pak = lod1_cpu_assets.mixed_pak
                items_pak = lod1_cpu_assets.items_pak
                tile_sources = lod1_cpu_assets.tile_sources
                liquid_surface_store = lod1_cpu_assets.liquid_surface_store
                (
                    ground_atlas_store, floor_atlas_store, ground_mesh_store, floor_mesh_store,
                    liquid_texture_store, liquid_mesh_store, object_texture_store,
                ) = activate_lod1_gpu_assets(
                    lod1_cpu_assets, cache, base_tile_ids, floor_overlay_instances,
                    floor_overlay_instances_by_sector, liquid_surface_candidates,
                    liquid_surface_candidates_by_sector, release_tile_staging=True
                )
                lod1_ready = True
                lod1_loader_future = None
                print("LOD1 artwork ready; visible sector geometry will compile only while idle.")
                redraw = True
            except Exception as exc:
                print(f"Background LOD1 preparation failed: {exc}")
                lod1_loader_future = None

        cur_display_size = get_display_size(screen)
        if cur_display_size != last_display_size:
            pan_x, pan_y = recenter_after_resize(last_display_size, cur_display_size, zoom, pan_x, pan_y)
            init_gl_2d(cur_display_size[0], cur_display_size[1])
            last_display_size = cur_display_size
            redraw = True

        # LOD1 visible-sector queues are serviced even when the camera is still.
        # Compile only while not dragging so camera motion is not interrupted by
        # sector display-list construction. Newly compiled sectors trigger redraws.
        factor_for_loading = choose_sector_lod_factor(zoom)
        idle_lod1_compiled = 0
        background_object_uploads = 0
        if lod1_ready and factor_for_loading == 1:
            visible_for_loading = visible_sector_entries(sector_visibility_index, zoom, pan_x, pan_y, screen)
            ground_mesh_store.request_visible(visible_for_loading)
            liquid_mesh_store.request_visible(visible_for_loading)
            hide_exterior_floor_for_loading = static_layer_view == "interior" and static_active_layer > 1
            if (
                DRAW_FLOOR_OVERLAYS and floor_overlay_mode != "off"
                and not hide_exterior_floor_for_loading
            ):
                floor_mesh_store.request_visible(visible_for_loading)
            if not dragging:
                idle_lod1_compiled += ground_mesh_store.compile_pending(LOD1_IDLE_GROUND_COMPILES_PER_TICK)
                idle_lod1_compiled += floor_mesh_store.compile_pending(LOD1_IDLE_FLOOR_COMPILES_PER_TICK)
                idle_lod1_compiled += liquid_mesh_store.compile_pending(LOD1_IDLE_LIQUID_COMPILES_PER_TICK)
                background_object_uploads = object_texture_store.pump(LOD1_OBJECT_TEXTURE_UPLOADS_PER_IDLE_TICK)
        elif lod1_ready and not dragging and object_texture_store is not None:
            background_object_uploads = object_texture_store.pump(LOD1_OBJECT_TEXTURE_UPLOADS_PER_IDLE_TICK)

        # Far LODs are prebuilt on disk; no imagery generation runs in the frame loop.
        completed_lods = 0
        uploaded_textures = visible_sector_surface_cache.pump() + overview_chunk_cache.pump()
        if idle_lod1_compiled or background_object_uploads or completed_lods or uploaded_textures:
            redraw = True

        if not redraw:
            clock.tick(60)
            continue

        glClear(GL_COLOR_BUFFER_BIT)

        requested_factor = choose_sector_lod_factor(zoom)
        factor = requested_factor if (requested_factor != 1 or lod1_ready) else 8

        visible_sectors_drawn = 0
        visible_tiles_drawn = 0
        visible_entries_count = 0
        visible_entries = []

        visible_chunks_count = 0
        if factor == OVERVIEW_LOD_FACTOR:
            visible_chunks = visible_overview_chunk_entries(overview_entries, zoom, pan_x, pan_y, screen)
            visible_chunks_count = len(visible_chunks)
            visible_entries = []
            visible_entries_count = 0
            visible_sectors_drawn = draw_overview_chunks_gl(
                screen, visible_chunks, zoom, pan_x, pan_y, overview_chunk_cache, overview_store
            )
            if visible_sector_surface_cache:
                release_gl_texture_cache(visible_sector_surface_cache)
        else:
            visible_entries = visible_sector_entries(sector_visibility_index, zoom, pan_x, pan_y, screen)
            visible_entries_count = len(visible_entries)
            if factor == 1:
                visible_tiles_drawn = ground_mesh_store.draw(screen, visible_entries, zoom, pan_x, pan_y)
                visible_sectors_drawn = len(visible_entries)
                if visible_sector_surface_cache:
                    release_gl_texture_cache(visible_sector_surface_cache)
            else:
                visible_sector_surface_cache, visible_sectors_drawn = draw_sector_lod_images_gl(
                    screen, visible_entries, zoom, pan_x, pan_y,
                    visible_sector_surface_cache, sector_lod8_store,
                )

        floor_overlays_drawn = 0
        floor_overlays_visible = 0
        hide_exterior_floor = static_layer_view == "interior" and static_active_layer > 1
        live_layer_mode = factor == 1 and lod1_ready
        floor_enabled_now = (
            live_layer_mode and DRAW_FLOOR_OVERLAYS
            and floor_overlay_mode != "off" and not hide_exterior_floor
        )

        liquid_surfaces_drawn = {"water": 0, "lava": 0}
        liquid_surfaces_visible = {"water": 0, "lava": 0}
        liquid_hidden_interior = static_layer_view == "interior" and static_active_layer > 1

        # LOD1 layer order: terrain -> GPU FLOOR overlays -> animated liquids -> statics.
        if floor_enabled_now:
            floor_overlays_drawn, floor_overlays_visible = draw_floor_overlays_gl(
                screen, visible_entries, floor_mesh_store, zoom, pan_x, pan_y, True
            )

        if live_layer_mode and draw_liquid_surfaces and not liquid_hidden_interior:
            liquid_surfaces_drawn, liquid_surfaces_visible = draw_liquid_surfaces_gl(
                visible_entries, liquid_mesh_store, zoom, pan_x, pan_y, enabled=True
            )

        static_objects_drawn = 0
        static_objects_visible = 0
        static_objects_unresolved = 0
        static_objects_filtered = 0
        static_objects_layer_filtered = 0
        if live_layer_mode and DRAW_STATIC_MIXED_OBJECTS:
            static_objects_drawn, static_objects_visible, static_objects_unresolved, static_objects_filtered, static_objects_layer_filtered = draw_static_mixed_objects_gl(
                screen,
                prepared_static_sprites_by_sector,
                visible_entries,
                items_pak,
                object_texture_store,
                zoom,
                pan_x,
                pan_y,
                active_layer=static_active_layer,
                layer_view=static_layer_view,
            )

        if show_hover_info:
            mx, my = pygame.mouse.get_pos()
            wx, wy = screen_to_world_tile(mx, my, pan_x, pan_y, zoom)
            hover = get_hover_info_at_fast(sector_by_grid, tiles_pak, wx, wy) if tiles_pak is not None else None
            if hover:
                draw_tooltip_gl(screen, font, [
                    f"sector:      {hover['sector_id']}",
                    f"local tile:  {hover['local'][0]}, {hover['local'][1]}",
                    f"ppos:        {hover['ppos'][0]}, {hover['ppos'][1]}",
                    f"tile id:     {hover['tile_id']} / 0x{hover['tile_id']:04X}",
                    f"floor head:  {hover['floor_head_id']} / 0x{hover['floor_head_id']:04X}",
                    f"height LTRB: {tuple(round(h, 1) for h in hover['height_corners'])}",
                    f"height avg:  {hover['height_average']:.1f}",
                    f"shade LTRB:  {hover.get('corner_tints', ())}",
                    f"texture:     {hover['texture']}",
                    f"subtile:     {hover['texture_tile_number']}",
                ], (mx, my), text_cache)

        status_lod = (
            f"LOD{factor} | zoom={zoom:.4f} | sectors={len(sector_entries)} "
            f"| visible={visible_entries_count if factor != OVERVIEW_LOD_FACTOR else visible_chunks_count} "
            f"| drawn={visible_sectors_drawn} | ground tiles={visible_tiles_drawn} "
            f"| meshes ground={len(ground_mesh_store.batches_by_sector) if ground_mesh_store else 0} floor={len(floor_mesh_store.batches_by_sector) if floor_mesh_store else 0}"
        )
        status_layers = (
            f"FLOOR={floor_overlay_mode} drawn={floor_overlays_drawn} "
            f"| liquid={'on' if draw_liquid_surfaces else 'off'} "
            f"0x90={liquid_surfaces_drawn['water']}/{liquid_surfaces_visible['water']} "
            f"0xA0={liquid_surfaces_drawn['lava']}/{liquid_surfaces_visible['lava']} "
            f"| objects={'on' if DRAW_STATIC_MIXED_OBJECTS else 'off'} "
            f"{static_objects_drawn}/{static_objects_visible} "
            f"| view={static_layer_view}:{static_active_layer} | hover={'on' if show_hover_info else 'off'}"
        )
        status_cache = (
            f"Cache: LOD1={'ready' if lod1_ready else 'loading'} ground={len(ground_mesh_store.batches_by_sector) if ground_mesh_store else 0} queued={len(ground_mesh_store.compile_queue) if ground_mesh_store else 0} "
            f"floor={len(floor_mesh_store.batches_by_sector) if floor_mesh_store else 0} queued={len(floor_mesh_store.compile_queue) if floor_mesh_store else 0} "
            f"liquid={len(liquid_mesh_store.compiled_sector_ids) if liquid_mesh_store else 0} | "
            f"LOD8 disk/RAM={len(sector_lod8_store.ready)} GL={len(visible_sector_surface_cache)} | "
            f"LOD16 disk/RAM={len(overview_store.ready)} GL={len(overview_chunk_cache)} | "
            f"[F] FLOOR [W] water [O] objects [H] hover [PgUp/PgDn] level [I] interior [Home] exterior [S] shot"
        )
        draw_text_gl(font, status_lod, (10, 10), (230, 230, 230), None)
        draw_text_gl(font, status_layers, (10, 30), (230, 230, 230), None)
        draw_text_gl(font, status_cache, (10, 50), (230, 230, 230), None)

        pygame.display.flip()
        dirty = False
        clock.tick(60)

    if lod1_loader_executor is not None:
        lod1_loader_executor.shutdown(wait=False, cancel_futures=True)
    if ground_mesh_store is not None:
        ground_mesh_store.shutdown()
    if floor_mesh_store is not None:
        floor_mesh_store.shutdown()
    sector_lod8_store.shutdown()
    overview_store.shutdown()
    if ground_atlas_store is not None:
        ground_atlas_store.shutdown()
    if floor_atlas_store is not None:
        floor_atlas_store.shutdown()
    visible_sector_surface_cache.shutdown()
    overview_chunk_cache.shutdown()
    if liquid_mesh_store is not None:
        liquid_mesh_store.shutdown()
    if liquid_texture_store is not None:
        liquid_texture_store.shutdown()
    if object_texture_store is not None:
        object_texture_store.shutdown()
    release_gl_texture_cache(text_cache)
    pygame.quit()


def main():
    run()


if __name__ == "__main__":
    main()
