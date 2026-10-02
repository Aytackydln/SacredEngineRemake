"""Execute Gold stdMovement and stdMovementWithGround to record independent CPU/SIMD references."""
import argparse,json,struct
from pathlib import Path
from native_image import NativeImage, BASE
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_ECX, UC_X86_REG_ESP, UC_X86_REG_EIP
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--memory-image', required=True)
parser.add_argument('--output', required=True)
args=parser.parse_args()
im=NativeImage(args.memory_image)
uc=Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(BASE,(len(im.data)+4095)&~4095); uc.mem_write(BASE,im.data)
obj=(BASE+len(im.data)+0xffff)&~0xffff
uc.mem_map(obj,0x20000)
particle,motion,stack,stop=obj+0x8000,obj+0x9000,obj+0x1f000,obj+0x1ff00
uc.mem_write(obj+0x68,struct.pack('<II',particle,particle+64))
uc.mem_write(obj+8,struct.pack('<3f',0,0,0))
uc.mem_write(obj+0xa4,struct.pack('<f',3))
rates=[20,-40,2,0.1,0.2,-0.1,0.3,3]
uc.mem_write(motion,struct.pack('<8f',*rates))
cases=[]
for mode in ['None','Bounce','Die']:
 for z in [-4,-3,-2,100]:
  initial=[3,4,z,0,0,0,20,-10,-15,-30,5,255,0.5,0,0.25,0]
  uc.mem_write(particle,struct.pack('<16f',*initial))
  steps=[]
  for dt in [0,0.03125,0.015625,0.03125]:
   uc.mem_write(stack,struct.pack('<IfII',stop,dt,motion,2 if mode=='Die' else 0))
   uc.reg_write(UC_X86_REG_ECX,obj);uc.reg_write(UC_X86_REG_ESP,stack)
   uc.emu_start(0x7640c0 if mode=='None' else 0x7645c0,stop,count=10000)
   assert uc.reg_read(UC_X86_REG_EIP)==stop
   steps.append(dict(dt=dt,state=list(struct.unpack('<16f',uc.mem_read(particle,64)))))
  cases.append(dict(mode=mode,groundHeight=-3,initial=initial,steps=steps))
Path(args.output).write_text(json.dumps(dict(codeSha256=im.text_hash,motion=rates,cases=cases),indent=2)+'\n')
print('Recorded 48 native steps, including below/equal/above-ground collision.')

