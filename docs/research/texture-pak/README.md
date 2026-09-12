# Texture.pak entry layout

This investigation identifies every serialized byte in the 0x50-byte
`sTextureEntry` header. The 39-byte tail at `+0x29` is reserved on disk. It does
not contain blend, tint, or lighting parameters. Every byte in that region is
zero in every populated entry scanned from the January 2004 demo and Sacred
Gold archives.

The texture's own color and alpha remain important, but they are pixel data:
the common type-4 payload expands to 16-bit A4R4G4B4 texels. Particle systems
then apply their dynamic color tables, world-light contribution, orientation,
atlas selection, alpha handling, and additive blend choice while drawing.

Particle rendering has a separate flags word. Native analysis names its values
`PS_RENDERMODE_ADDITIVE`, `ENERGY_ALPHA`, `PHI`, `ENERGY_COL`,
`ADDITIVE_COLOR`, `PARTICLE_COL`, `USE_WORLD_LIGHT`, `CENTER_AT_DOWN`, and
`MULTIPART`. These are represented by `SacredParticleRenderMode` in
`Sacred.Core`.

## Serialized archive layouts

Texture.pak starts with a 0x100-byte header. Its descriptor array contains one
0x0C-byte record for each entry ID. A populated descriptor points to a
0x50-byte entry header followed immediately by its encoded pixel payload.

### File header

| Offset | Size | Representation | Meaning |
| ---: | ---: | --- | --- |
| `0x00` | 3 | ASCII | `TEX` signature |
| `0x03` | 1 | UInt8 | format version; both archives use 3 |
| `0x04` | 4 | UInt32 | descriptor count |
| `0x08` | 248 | bytes | header padding |

### Entry descriptor

| Offset | Size | Representation | Meaning |
| ---: | ---: | --- | --- |
| `0x00` | 4 | UInt32 | storage type; repeated as UInt8 in the entry header |
| `0x04` | 4 | UInt32 | absolute offset of the entry header |
| `0x08` | 4 | UInt32 | encoded payload size, excluding the entry header |

### Entry header (`sTextureEntry`)

| Offset | Size | Native name | Meaning |
| ---: | ---: | --- | --- |
| `0x00` | 32 | `name` | NUL-terminated texture name |
| `0x20` | 2 | `width` | pixel width |
| `0x22` | 2 | `height` | pixel height |
| `0x24` | 1 | `type` | `SacredTextureStorageFormat` value |
| `0x25` | 4 | `RLEsize` | encoded payload size; the old name also covers zlib/JPEG/raw data |
| `0x29` | 39 | `reserved` | all zero in the physical archives examined |

The descriptor type and payload size agree with the copies in the entry header
for every populated entry examined. `TexturePakEntryHeaderLayout`,
`TexturePakEntryDescriptorLayout`, and the generated
`SacredExecutableTextureEntryLayout` encode these offsets.

## Storage type values

The demo function `cTextureLoader::load` at `0x5C21E0` and the Gold function at
`0x656DD0` use the same cases. Values 0, 3, 4, and 6 occur in the examined
archives; the names for the remaining values describe the loader branch.

| Value | `SacredTextureStorageFormat` | Loader behavior |
| ---: | --- | --- |
| 0 | `Argb4444` | raw scanline copy to A4R4G4B4 |
| 1 | `Argb4444Variant1` | same raw 16-bit fallback as value 0 |
| 2 | `Argb4444Variant2` | same raw 16-bit fallback as value 0 |
| 3 | `RleArgb4444` | `UncompressRLE` to A4R4G4B4 |
| 4 | `ZlibArgb4444` | `UncompressZIP` to A4R4G4B4 |
| 5 | `JpegArgb4444` | JPEG decode followed by conversion to A4R4G4B4 |
| 6 | `Bgra8888` | raw copy to A8R8G8B8 |
| 7 | `NoPayloadCopy` | creates and locks the surface, but skips payload copying |
| 8 | `JpegBgra8888` | JPEG decode through the loader's 32-bit output path |

Type 6 requests the 32-bit texture format before surface creation; all other
values request the 16-bit format at that point. Type 8 later follows code
equivalent to the native `UncompressJPEG32` routine. Type 7's empty-copy
behavior is explicit in both executables. No meaning beyond those observed
branches is assigned to either value.

## Gold runtime overlay

Gold supports `Texture.pak` followed by `Texture00.pak` through
`Texture15.pak`. Its constructor formats suffixes 0 through 15 and passes
archive slots 1 through 16 to the helper at `0x656700`. The helper reads each
serialized entry,
then overwrites five reserved bytes in the in-memory copy:

| Offset | Size | Runtime meaning |
| ---: | ---: | --- |
| `0x29` | 1 | archive slot, 0 through 16 |
| `0x2A` | 4 | absolute entry-header offset in that archive |
| `0x2E` | 34 | remaining reserved bytes |

`cTextureLoader::load` reads the archive slot at `0x656E13` and the header
offset at `0x6570AF`, adds 0x50, and seeks to the encoded payload. The overlay
also appears in `pak/Texture.TMP`, the generated lookup cache. In the examined
Gold installation, cached entries start at `0x118`; the file contains exactly
25,538 records (`(fileLength - 0x118) / 0x50`), equal to 25,535 main descriptors
plus three `Texture03.pak` descriptors. For example, cached `AJ_TEST.TGA` has
archive slot 0 and header offset `0x0004AE44`, which points back into the main
archive. The two populated `Texture03.pak` records carry slot 4, confirming
that the byte is the open-stream array index rather than the decimal suffix.
All 25,536 populated cached records were checked against the corresponding
physical archive entries: bytes `0x00..0x28` and every header offset match, each
archive slot selects the expected stream, and every byte at `0x2E..0x4F` is
zero.

This distinction matters when defining layouts: bytes `0x29..0x2D` are
reserved zeros in Texture.pak and archive-location fields only in Gold's
runtime/cache representation. `SacredGoldTextureEntryRuntimeLayout` models the
latter separately.

## Demo and Gold archive comparison

The comparison used these archives:

| Archive | SHA-256 | Descriptors | Populated | Types |
| --- | --- | ---: | ---: | --- |
| January demo `texture.pak` | `D13FBC773579EC00B6AFE3F35A1CAB9BDC526E54BA102CF104826D31A20BD01D` | 17,370 | 4,465 | 4: 4,340; 6: 125 |
| Gold `texture.pak` | `A4F1664C16B7EA387C5E816E15002CDE704B675B12339E905E8DEB3A2EE3ABAC` | 25,535 | 25,534 | 4: 25,307; 6: 227 |
| Gold `texture03.pak` | `F2D2AC9D55451B67FD65179067CE22AA948FBD77352ECCA0D3849634C46A0EF1` | 3 | 2 | 4: 2 |

All 4,465 demo texture names occur in Gold. Among same-name entries, 4,257 have
identical dimensions, type, encoded size, and reserved bytes. Encoded payloads
are byte-identical for 3,344 of 4,460 comparable entries. Five entries at the
truncated end of the demo archive were excluded from payload hashing.

The comparison is especially strong for the suspected effect metadata:

| Group | Common names | Same metadata | Same encoded payload |
| --- | ---: | ---: | ---: |
| `PARTICLE_*` | 53 | 53 | 53 |
| names containing `LIGHT`, `GLOW`, or `FLARE` | 42 | 42 | 42 |

Examples with identical headers and payloads include `PARTICLE_FIRE01.TGA`,
`PARTICLE_FLARE01.TGA`, `PARTICLE_GLOW01.TGA`, `PARTICLE_GLOW03.TGA`, and
`PARTICLE_SPARK04.TGA`. `FX_GROUNDPOISON.TGA` keeps the same type and 256x256
dimensions but has a different encoded size and payload, showing that the
comparison also detects real asset revisions.

The reproducible scanner is in `_scratch/TexturePakLayoutProbe`. It parses both
descriptor and header copies, counts every nonzero reserved-byte position, and
compares same-name encoded payloads by SHA-256. Supplying Gold's `Texture.TMP`
as its third argument also checks every cached record and discovers installed
`Texture00.pak` through `Texture15.pak` archives using the native slot mapping.

## Evidence sources

- Executable analysis: exact 80-byte
  `sTextureEntry` size, field types, and offsets.
- Executable analysis functions:
  `UncompressJPEG` `0x5C13A0`, `UncompressJPEG32` `0x5C14A0`,
  `UncompressZIP` `0x5C1530`, `UncompressRLE` `0x5C15C0`, and the texture loader
  functions at `0x5C1980..0x5C27B0`.
- Original Sacred Gold 2.0 executable SHA-256
  `4DF665806736CB778088919226F1D12CC88D0D6869895561D693AA03DB9C91CD`:
  texture loader constructor `0x656420`, archive helper `0x656700`, and texture
  load function `0x656DD0`.
- Complete physical archive scans and same-name payload comparison performed by
  `_scratch/TexturePakLayoutProbe`.
