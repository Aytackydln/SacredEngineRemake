"""Usage: py docs/research/model-particles/native_evidence.py decoded-image.bin > evidence.txt
Uses the hash-checked image reader from the existing particle research.
Requires capstone, as does native_image.py. Input is an image-base 0x400000 dump.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent / 'particle-definitions'))
from native_image import NativeImage

image = NativeImage(sys.argv[1])
ranges = {
    'bone-name lookup': (0x401000, 0x401030),
    'renderer glow texture': (0x40132F, 0x401360),
    'torch predicate': (0x428150, 0x428180),
    'whip predicate': (0x428190, 0x428200),
    'streak predicate': (0x4282F0, 0x428360),
    'worms predicate': (0x428360, 0x4283C0),
    'beam predicate': (0x4283C0, 0x428420),
    'weapon header and raw table loader': (0x434100, 0x434263),
    'derived item descriptor copying': (0x425A03, 0x425AB5),
    'weapon preview transform': (0x434270, 0x4343F1),
    'uniform preview scale matrix': (0x653A3A, 0x653A75),
    'world effect elapsed time': (0x5A6709, 0x5A6766),
    'world effect update dispatch': (0x5A99EE, 0x5A9A10),
    'preview effect update loop': (0x6F1550, 0x6F159F),
    'particle manager update': (0x7CC080, 0x7CC0B2),
    'equipped attach dispatch': (0x5CDB60, 0x5CE010),
    'beam draw dispatch': (0x5BF706, 0x5BF80B),
    'beam quads': (0x40DA80, 0x40DEF0),
    'render state enable': (0x643470, 0x643531),
    'render state disable': (0x643976, 0x643A2D),
    'torch': (0x774F00, 0x775410),
    'worms': (0x77C500, 0x77CA00),
    'whip': (0x787120, 0x7877C0),
    'streak': (0x7877C0, 0x788170),
}
for name, (start, end) in ranges.items():
    print(f'\n{name}: 0x{start:08X}..0x{end:08X}')
    print('\n'.join(image.disassembly(start, end)))
print('glow texture:', image.string(0x8E7B90))
