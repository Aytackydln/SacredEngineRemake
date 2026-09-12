# Sacred sound archives

Sacred Gold stores playable audio in `pak/sound.pak` and named selection tables in
`pak/sndProfiles.pak`.

## `sound.pak`

The archive begins with a `0x100`-byte header:

| Offset | Size | Meaning |
| --- | ---: | --- |
| `0x00` | 3 | ASCII signature `SND` |
| `0x03` | 1 | format version (`1`) |
| `0x04` | 4 | descriptor-slot count (`50000` in Sacred Gold) |

The header is followed by one 12-byte `PakEntryDescriptorLayout` per slot. The descriptor
index is the sound ID. Unused IDs have an all-zero descriptor. Live descriptors contain
the storage type, absolute payload offset, and exact payload byte count.

Storage type `32` is a complete RIFF/WAVE file. Observed WAVE encodings include 16-bit PCM
and Microsoft IMA ADPCM. Storage type `33` is raw MPEG Layer III data and can be saved with
an `.mp3` extension. No additional entry header or decoding step is needed. The inspected
Sacred Gold archive contains 6,598 live entries: 3,194 WAVE and 3,404 MP3 payloads.

## `sndProfiles.pak`

This archive uses the same `0x100`-byte header and 12-byte descriptor table, with signature
`SPF`, version `1`, and 8192 sparse profile slots. Populated and reserved profile descriptors
use type `35`. Each payload is exactly `0xB8` (184) bytes:

| Offset | Size | Meaning |
| --- | ---: | --- |
| `0x00` | 32 | null-terminated ISO-8859-1 profile name |
| `0x20` | 4 | defined flag (`1` for a populated slot) |
| `0x24` | 20 | reserved; zero in all defined Sacred Gold profiles |
| `0x38` | 128 | 64 little-endian `ushort` Sound.pak IDs |

The 64 IDs are an authored event/variant selection table. Repeated IDs are intentional and
must be preserved because they affect selection weighting. Some profile references point to
empty Sound.pak slots, so consumers should use the archive index as the source of truth.

`Items.pak` model-descriptor offset `0x24` is the profile ID. Sacred.exe indexes the
profile's sound slots with this value. A zero profile allows the executable's weapon-family
fallback, which reads the `Weapon.pak` equipment type; it is not a direct sound ID.

## Inventory interaction sounds

Inventory interaction sounds are a separate path from sound profiles. Sacred.exe switches on
the item category stored at `Items.pak` model-descriptor offset `0x2e` and plays these named
`sound.pak` entries:

| Category | Sound ID | Embedded Sacred.exe name |
| --- | ---: | --- |
| metal/default | 7005 | `SOUND_FX_UI_INV_PUTMETAL01` |
| ring | 7007 | `SOUND_FX_UI_INV_PUTRING01` |
| potion | 7019 | `SOUND_FX_UI_INV_POTION01` |
| amulet | 7020 | `SOUND_FX_UI_INV_PUTAMULET01` |
| quest book | 7107 | `SOUND_FX_UI_INV_PUTQUESTBOOK01` |
| helmet | 7113 | `SOUND_FX_UI_INV_PUTHELMET01` |
| key | 7114 | `SOUND_FX_UI_INV_PUTKEYS01` |
| shield | 7115 | `SOUND_FX_UI_INV_PUTSHIELD01` |
| clothes | 7116 | `SOUND_FX_UI_INV_PUTCLOTHES01` |

The sound names and IDs come from Sacred.exe's embedded registry; the audio payloads come from
`sound.pak`. The executable defaults every category without a dedicated branch to the metal sound;
no individual item IDs or `Weapon.pak` fallback are required.
