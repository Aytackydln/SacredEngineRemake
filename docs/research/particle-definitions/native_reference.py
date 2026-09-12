"""Generate independent byte-for-byte reference blocks for the managed preprocessor.

Uses Unicorn and the captured original native code, not Sacred.Particles.Reader.
The executable's stored IDs are read from the real ID+name catalogue rows.
"""
import argparse
import json
from pathlib import Path
from native_image import NativeImage
from native_presets import dispatch, draw_selection, emulate


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--memory-image', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    image = NativeImage(args.memory_image)
    recovered = []
    for index in range(0x15f8):
        address = 0x8ec328 + index * 0x44
        name = image.string(address + 4)
        if name and name.startswith('TYPE_FX_'):
            type_id = image.u32(address)
            native = dispatch(image, type_id)
            if native and 'preset' in native:
                recovered.append((type_id, name, native))
    qualities = []
    for quality in range(3):
        entries = []
        for type_id, name, native in recovered:
            family = native['family']
            result = emulate(image, family, native['preset'], quality)
            draw = draw_selection(image, family, native['preset'])
            entries.append(dict(type_id=type_id, type_name=name, preset=native['preset'],
                                status=result['status'], texture=draw['texture_name'],
                                raw_renderer_flags=draw['raw_renderer_flags'],
                                slots=[dict(index=s['index'], emission=s['emission_raw_hex'], motion=s['motion_raw_hex'], colors=s['colors_raw_hex'])
                                       for s in result['slots'] if s['emission_raw_hex'] and s['motion_raw_hex']]))
        qualities.append(dict(quality=quality, entries=entries))
        print(f'Native quality {quality}: {len(entries)} class/preset results')
    Path(args.output).write_text(json.dumps(dict(text_sha256=image.text_hash,
        method='Original initializer code executed by Unicorn 2.1.4 with an empty particle vector; raw written blocks only.',
        qualities=qualities), indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
