"""Version-checked helpers for the Sacred Gold unpacked memory image (base 0x400000)."""
import hashlib
import struct
from pathlib import Path

BASE = 0x400000
TEXT_HASH = 'ee60108ce8147721717df1632c47b89c2b445a0255922219fa14a5980feadf37'


class NativeImage:
    def __init__(self, path):
        self.data = Path(path).read_bytes()
        self.text_hash = hashlib.sha256(self.data[0x1000:0x48fa32]).hexdigest()
        if self.text_hash != TEXT_HASH:
            raise ValueError('Unrecognized unpacked .text; refusing version-specific addresses. '
                             'Pass a matching module memory dump, not the encrypted on-disk EXE.')

    def bytes(self, address, size):
        offset = address - BASE
        if offset < 0 or offset + size > len(self.data):
            raise ValueError(f'Address outside image: {address:#x}, size {size:#x}')
        return self.data[offset:offset + size]

    def u32(self, address):
        return struct.unpack('<I', self.bytes(address, 4))[0]

    def f32(self, address):
        return struct.unpack('<f', self.bytes(address, 4))[0]

    def string(self, address):
        raw = self.bytes(address, 256).split(b'\0', 1)[0]
        return raw.decode('ascii') if raw and all(32 <= c < 127 for c in raw) else None

    def instructions(self, start, end):
        from capstone import Cs, CS_ARCH_X86, CS_MODE_32
        return list(Cs(CS_ARCH_X86, CS_MODE_32).disasm_lite(self.bytes(start, end - start), start))

    def disassembly(self, start, end):
        return [f'{a:08x} {m:10} {op}' for a, size, m, op in self.instructions(start, end)]
