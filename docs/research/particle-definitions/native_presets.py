"""Extract native dispatch and preset writes for all discovered smoke/dwarf-magic FX types.

Runs the actual x86 initializer with Unicorn, without launching/rendering the game.
Unwritten fields stay null. Captures reads of uninitialized object bytes so that
zero-filled scratch memory cannot silently become an invented constructor default.
All version-specific addresses stay in this research tool, outside Sacred.*.
"""
import argparse
import json
import re
import struct
from pathlib import Path

from native_image import BASE, NativeImage

FAMILIES = {
    0x5a0ccf: dict(name='cParticleSystem_smoke', constructor=0x76e200,
                   constructor_end=0x76e2d0, initialize=0x76e980, size=0x2e4c,
                   motion=0x2cac, emission=0x2d0c, slots=3),
    0x5a17df: dict(name='cParticleSystem_dwarfmagic', constructor=0x78a2d0,
                   constructor_end=0x78a343, initialize=0x78a5d0, size=0x2530,
                   motion=0x24a8, emission=0x24c8, slots=1),
}
EMISSION = [
    (0x00, 'gravity', 'f'), (0x04, 'gravity_random_width', 'f'),
    (0x08, 'size', 'f'), (0x0c, 'size_random_width', 'f'),
    (0x10, 'rotation', 'f'), (0x14, 'rotation_random_width', 'f'),
    (0x18, 'angular_velocity', 'f'), (0x1c, 'angular_velocity_random_width', 'f'),
    (0x20, 'velocity', '3f'), (0x2c, 'velocity_random_width', '3f'),
    (0x38, 'position_offset', '3f'), (0x44, 'position_random_width', '3f'),
    (0x50, 'packed_color', 'I'), (0x54, 'unknown_color_parameter', 'I'),
    (0x58, 'emission_interval', 'f'), (0x5c, 'burst_count', 'I'),
    (0x60, 'variant_selection', 'B'), (0x61, 'unknown61', 'B'), (0x62, 'unknown62', 'H'),
]
MOTION = [
    (0, 'gravity_change_rate', 'f'), (4, 'fade_change_rate', 'f'),
    (8, 'size_change_rate', 'f'), (12, 'additional_angular_velocity', 'f'),
    (16, 'gravity_direction', '3f'), (28, 'inward_acceleration', 'f'),
]


def dispatch(image, type_id):
    # These bounds and two-level tables come from 0x5A0C50 and 0x48907A.
    index = type_id - 0x300
    if not 0 <= index <= 0x19d:
        return None
    factory = image.u32(0x5a3294 + 4 * image.bytes(0x5a3570 + index, 1)[0])
    family = FAMILIES.get(factory)
    if family is None:
        return None
    # The first creation jump table covers only 0x300..0x37B. Later type IDs
    # enter comparison branches before that table; emulate this small dispatcher.
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
    from unicorn.x86_const import UC_X86_REG_EAX
    uc = Uc(UC_ARCH_X86, UC_MODE_32)
    uc.mem_map(BASE, (len(image.data) + 4095) & ~4095)
    uc.mem_write(BASE, image.data)
    uc.reg_write(UC_X86_REG_EAX, type_id)
    found = []
    def visit(engine, address, size, user_data):
        if image.bytes(address, 7) == bytes.fromhex('68c00200005353'):
            found.append(address)
            engine.emu_stop()
    uc.hook_add(UC_HOOK_CODE, visit)
    try:
        uc.emu_start(0x48905b, 0x48a7c6, count=128)
    except Exception as ex:
        return dict(family=family, factory_address=hex(factory), error=f'Creation dispatcher: {ex}')
    if not found:
        return dict(family=family, factory_address=hex(factory), error='No literal creation selector found')
    creation = found[0]
    instructions = image.instructions(creation, creation + 32)
    # Require the recovered event-construction sequence, not an arbitrary immediate.
    first = [(m, op) for _, _, m, op in instructions[:4]]
    if first[:3] != [('push', '0x2c0'), ('push', 'ebx'), ('push', 'ebx')] or first[3][0] != 'push':
        return dict(family=family, factory_address=hex(factory), dispatch_address=hex(creation),
                    error='Unrecognized event-construction sequence', instructions=first)
    return dict(family=family, factory_address=hex(factory), dispatch_address=hex(creation),
                preset=int(first[3][1], 0), instructions=image.disassembly(creation, creation + 16))


def textures(image, family):
    result = []
    for address, _, mnemonic, operand in image.instructions(family['constructor'], family['constructor_end']):
        if mnemonic != 'push' or not operand.startswith('0x'):
            continue
        pointer = int(operand, 0)
        try:
            name = image.string(pointer)
        except (ValueError, UnicodeError):
            continue
        if name and name.endswith('.TGA'):
            result.append(dict(name=name, pointer=hex(pointer), push_address=hex(address)))
    return result


def draw_selection(image, family, preset):
    if family['name'] == 'cParticleSystem_dwarfmagic':
        return dict(texture_name=image.string(0x8e7c10), draw_address='0x78a3b0',
                    raw_renderer_flags=0x1d)
    if not 1 <= preset <= 13:
        return None
    table_index = image.bytes(0x76e7bc + preset - 1, 1)[0]
    branch = image.u32(0x76e7a8 + table_index * 4)
    instructions = image.instructions(branch, branch + 0x60)
    offset = int(re.search(r'\+ (0x[0-9a-f]+)\]', instructions[0][3])[1], 0)
    # Constructor lookup strings, not assumed Texture.pak indices.
    names = {0x2e3c: 0xa13e04, 0x2e40: 0xa13d40, 0x2e44: 0xa13dec, 0x2e48: 0xa13dd4}
    flags = None
    for _, _, mnemonic, operand in instructions:
        if mnemonic == 'jmp':
            break
        if mnemonic == 'push' and operand.startswith('0x'):
            flags = int(operand, 0)
    return dict(texture_name=image.string(names[offset]), draw_address=hex(branch),
                texture_handle_offset=hex(offset), raw_renderer_flags=flags)


def emulate(image, family, preset, quality):
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_MEM_READ, UC_HOOK_MEM_WRITE
    from unicorn.x86_const import UC_X86_REG_ECX, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_FPCW
    uc = Uc(UC_ARCH_X86, UC_MODE_32)
    uc.mem_map(BASE, (len(image.data) + 4095) & ~4095)
    uc.mem_write(BASE, image.data)
    # Outside the image; zero-filled scratch object, event and stack.
    obj = (BASE + len(image.data) + 0xffff) & ~0xffff
    uc.mem_map(obj, 0x20000)
    event, stack, stop = obj + 0x10000, obj + 0x1f000, obj + 0x1ff00
    uc.mem_write(event + 0x38, struct.pack('<I', preset))
    uc.mem_write(0x182ee78, bytes([quality]))
    uc.mem_write(stack, struct.pack('<II', stop, event))
    uc.reg_write(UC_X86_REG_ECX, obj)
    uc.reg_write(UC_X86_REG_ESP, stack)
    uc.reg_write(UC_X86_REG_FPCW, 0x37f)
    written, uninitialized = bytearray(family['size']), set()
    # Initializers finish by resetting existing particles via the vector at +0x68/+0x6C.
    # Supply an explicitly empty vector, independent of the preset parameter blocks.
    uc.mem_write(obj + 0x68, struct.pack('<II', obj + 0x8000, obj + 0x8000))
    written[0x68:0x70] = b'\x01' * 8

    def on_write(engine, access, address, size, value, user_data):
        for offset in range(max(address - obj, 0), min(address - obj + size, len(written))):
            written[offset] = 1

    def on_read(engine, access, address, size, value, user_data):
        for offset in range(max(address - obj, 0), min(address - obj + size, len(written))):
            if not written[offset]:
                uninitialized.add(offset)

    uc.hook_add(UC_HOOK_MEM_WRITE, on_write, begin=obj, end=obj + family['size'] - 1)
    uc.hook_add(UC_HOOK_MEM_READ, on_read, begin=obj, end=obj + family['size'] - 1)
    error = None
    try:
        uc.emu_start(family['initialize'], stop, count=200000)
        if uc.reg_read(UC_X86_REG_EIP) != stop:
            error = 'Instruction limit reached before return'
    except Exception as ex:
        error = f'{type(ex).__name__}: {ex}'
    raw = bytes(uc.mem_read(obj, family['size']))

    def decode(offset, fields):
        result = {}
        for relative, name, fmt in fields:
            at = offset + relative
            size = struct.calcsize('<' + fmt)
            values = struct.unpack_from('<' + fmt, raw, at) if all(written[at:at + size]) else None
            result[name] = (values[0] if len(values) == 1 else values) if values else None
        return result

    return dict(status='complete' if not error and not uninitialized else 'incomplete',
                error=error, uninitialized_object_reads=[hex(x) for x in sorted(uninitialized)],
                  slots=[dict(index=i,
                              colors_raw_hex=raw[family['motion'] - (family['slots'] - i) * 0x400:
                                  family['motion'] - (family['slots'] - i) * 0x400 +
                                  (0x400 if all(written[family['motion'] - (family['slots'] - i) * 0x400:
                                      family['motion'] - (family['slots'] - i - 1) * 0x400]) else
                                   16 if all(written[family['motion'] - (family['slots'] - i) * 0x400:
                                       family['motion'] - (family['slots'] - i) * 0x400 + 16]) else 0)].hex(),
                            motion_raw_hex=raw[family['motion'] + i * 0x20:family['motion'] + (i + 1) * 0x20].hex()
                                if all(written[family['motion'] + i * 0x20:family['motion'] + (i + 1) * 0x20]) else None,
                            emission_raw_hex=raw[family['emission'] + i * 0x64:family['emission'] + (i + 1) * 0x64].hex()
                                if all(written[family['emission'] + i * 0x64:family['emission'] + (i + 1) * 0x64]) else None,
                            motion=decode(family['motion'] + i * 0x20, MOTION),
                            emission=decode(family['emission'] + i * 0x64, EMISSION))
                       for i in range(family['slots'])])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--memory-image', required=True)
    parser.add_argument('--script-summary', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--quality', type=int, choices=(0, 1, 2), default=2)
    args = parser.parse_args()
    image = NativeImage(args.memory_image)
    summary = json.loads(Path(args.script_summary).read_text(encoding='utf-8-sig'))
    records, unhandled = [], []
    for effect in summary['effect_types']:
        native = dispatch(image, effect['type_id'])
        if native is None:
            unhandled.append(effect)
            continue
        family = native.pop('family')
        native.update(effect)
        native['family'] = family['name']
        native['constructor_address'] = hex(family['constructor'])
        native['initializer_address'] = hex(family['initialize'])
        native['texture_lookups'] = textures(image, family)
        if 'preset' in native:
            native['draw_selection'] = draw_selection(image, family, native['preset'])
            native['initializer_result'] = emulate(image, family, native['preset'], args.quality)
        records.append(native)
        print(effect['type_name'], family['name'], native.get('preset'),
              native.get('initializer_result', {}).get('status', native.get('error')))
    result = dict(text_sha256=image.text_hash, image_base=hex(BASE),
                  quality_setting=args.quality, world_units_per_tile=image.f32(0x890dc0),
                  method='Original x86 initializer on a zeroed object with an explicitly empty particle vector at +0x68/+0x6C. Only bytes actually written are decoded. '
                         'No original constructor or simulation/render loop is executed; null means unwritten. '
                         'Incomplete results must not be treated as recovered presets.',
                  effects=records, other_effect_types_not_emulated=unhandled)
    path = Path(args.output)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(result, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    print(f'Wrote {len(records)} native dispatch records: {path}')


if __name__ == '__main__':
    main()
