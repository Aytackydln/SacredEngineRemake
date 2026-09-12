"""Run the original particle integrator on a controlled, nontrivial particle."""
import argparse
import json
import struct
from pathlib import Path
from native_image import NativeImage, BASE
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_ECX, UC_X86_REG_ESP, UC_X86_REG_EIP

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--memory-image', required=True)
p.add_argument('--output', required=True)
args = p.parse_args()
image = NativeImage(args.memory_image)
uc = Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(BASE, (len(image.data) + 4095) & ~4095)
uc.mem_write(BASE, image.data)
obj = (BASE + len(image.data) + 0xffff) & ~0xffff
uc.mem_map(obj, 0x20000)
particle, motion, stack, stop = obj+0x8000, obj+0x9000, obj+0x1f000, obj+0x1ff00
uc.mem_write(obj+0x68, struct.pack('<II', particle, particle+0x40))
initial = [3, 4, 100, 0, 0, 0, 20, -10, 15, -30, 5, 255, 0.5, 0, 0.25, 0]
uc.mem_write(particle, struct.pack('<16f', *initial))
rates = [20, -40, 2, 0.1, 0.2, 0, 0, 3]
uc.mem_write(motion, struct.pack('<8f', *rates))
steps = []
for dt in [0.03125, 0.015625, 0.03125]:
    uc.mem_write(stack, struct.pack('<IfII', stop, dt, motion, 0))
    uc.reg_write(UC_X86_REG_ECX, obj)
    uc.reg_write(UC_X86_REG_ESP, stack)
    uc.emu_start(0x7640c0, stop, count=10000)
    assert uc.reg_read(UC_X86_REG_EIP) == stop
    steps.append(dict(dt=dt, state=list(struct.unpack('<16f', uc.mem_read(particle, 64)))))
Path(args.output).write_text(json.dumps(dict(initial=initial, motion=rates, steps=steps), indent=2)+'\n')
