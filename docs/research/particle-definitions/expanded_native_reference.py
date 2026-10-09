"""Compare newly decoded families with original x86 initializers using Unicorn.
Run from the repository root; the memory image must match the verified Gold hash.
"""
import sys,re,struct,json,argparse
from pathlib import Path
sys.path.insert(0,str(Path('docs/research/particle-definitions').resolve()))
from native_image import NativeImage,BASE
from unicorn import *
from unicorn.x86_const import *
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--memory-image', required=True)
parser.add_argument('--source-directory', default='Sacred.Particles/Generated')
args=parser.parse_args()
image=NativeImage(args.memory_image)
checks=0
for quality,name in enumerate(['Low','Medium','High']):
 source=(Path(args.source_directory)/f'EmbeddedParticleCatalogue{name}.g.cs').read_text()
 lines=[l for l in source.splitlines() if ('cParticleSystem_magicprison' in l and 'Status.Decoded' in l or 'IsEventPreset = true' in l)
        and not any('"'+family+'"' in l for family in ['cParticleSystem_sparks','cParticleSystem_dustcloud','cParticleSystem_windstrike','cParticleSystem_burningBone'])]
 for line in lines:
  event='IsEventPreset = true' in line
  preset=int(re.search(r'"cParticleSystem_[^"]+", (\d+),',line)[1])
  generic='"cParticleSystem_generic"' in line
  changeling='"cParticleSystem_changeling"' in line
  original='"cParticleSystem_puzzleSolved"' in line
  start=0x795e20 if changeling else 0x796e10 if generic else 0x76cd50 if original else 0x76d260 if event else 0x79cc80
  uc=Uc(UC_ARCH_X86,UC_MODE_32)
  uc.mem_map(BASE,(len(image.data)+4095)&~4095);uc.mem_write(BASE,image.data)
  obj=(BASE+len(image.data)+0xffff)&~0xffff
  uc.mem_map(obj,0x20000)
  eventaddr,stack,ret=obj+0x10000,obj+0x1f000,obj+0x1ff00
  stop=0x795fcb if changeling else {0:0x796ed8,2:0x797021,5:0x7971bd,6:0x797360,9:0x79764e}[preset] if generic else 0x76cdfe if original else {0:0x76d355,1:0x76d42c,2:0x76d545,3:ret,4:0x76d862,5:0x76d9f1}[preset] if event else 0x79cd8d
  if event and not original and not generic and preset>=4:
   uc.mem_map(0,0x1000)
   environment=obj+0x18000
   uc.reg_write(UC_X86_REG_EBP,environment);uc.reg_write(UC_X86_REG_EBX,0)
   uc.emu_start(0x418f80,0x418f94,count=10)
   uc.mem_write(0xaa4544,struct.pack('<I',environment))
  uc.mem_write(eventaddr+(0x3c if generic else 0x38),struct.pack('<I',preset))
  if generic:
   color=image.u32(0x57f524) if preset==2 else 0 if preset==9 else image.u32(0x541bdc)
   uc.mem_write(eventaddr+0x38,struct.pack('<I',color))
   if preset in (6,9):
    # Only identity resolution is external; the initializer reads no actor fields.
    # Stub the two lookups, preserving the original native stack conventions.
    def identity(engine,address,size,data):
     if address in (0x5fe000,0x84a961,0x428ce0):
      esp=engine.reg_read(UC_X86_REG_ESP)
      # Unit radius supplies a normalized parameter template. The actual runtime
      # radius comes from Items.pak; identity lookups return the mesh owner.
      engine.reg_write(UC_X86_REG_EAX,1 if address==0x428ce0 else obj+0x18000)
      engine.reg_write(UC_X86_REG_EIP,struct.unpack('<I',engine.mem_read(esp,4))[0])
      engine.reg_write(UC_X86_REG_ESP,esp+(8 if address in (0x5fe000,0x428ce0) else 4))
    uc.hook_add(UC_HOOK_CODE,identity)
  if changeling:
   # Execute both sides of the real initializer, including color/animal-list writes.
   # External identity/geometry calls preserve their native stack conventions.
   def actor_mesh(engine,address,size,data):
    cleanup={0x5fe000:8,0x84a961:4,0x44fc30:4,0x767fb0:20}
    if address in cleanup:
     esp=engine.reg_read(UC_X86_REG_ESP)
     if address==0x767fb0:
      assert struct.unpack('<I',engine.mem_read(esp+12,4))[0]==500
     engine.reg_write(UC_X86_REG_EAX,obj+0x18000)
     engine.reg_write(UC_X86_REG_EIP,struct.unpack('<I',engine.mem_read(esp,4))[0])
     engine.reg_write(UC_X86_REG_ESP,esp+cleanup[address])
   uc.hook_add(UC_HOOK_CODE,actor_mesh)
  uc.mem_write(eventaddr+0x0c,struct.pack('<I',obj+0x18000))
  uc.mem_write(stack,struct.pack('<II',ret,eventaddr))
  uc.mem_write(obj+0x68,struct.pack('<II',obj+0x8000,obj+0x8000))
  uc.mem_write(0x182ee78,bytes([quality]))
  uc.reg_write(UC_X86_REG_ECX,obj);uc.reg_write(UC_X86_REG_ESP,stack);uc.reg_write(UC_X86_REG_FPCW,0x37f)
  uc.emu_start(start,stop,count=200000)
  assert uc.reg_read(UC_X86_REG_EIP)==stop
  emission,motion=re.search(r'EmbeddedParticleParameters.Read\(0, "([a-f0-9]+)", "([a-f0-9]+)"',line).groups()
  colors=re.search(r'ReadColors\("([a-f0-9]+)"',line)[1]
  offsets=(0x20d8,0x20b8,0x20a8) if changeling else (0x20dc,0x20bc,0x20ac) if original else (0x24c8,0x24a8,0x20a8)
  for offset,expected in zip(offsets,(emission,motion,colors)):
   expected=bytes.fromhex(expected);actual=bytes(uc.mem_read(obj+offset,len(expected)))
   assert actual==expected, (name,preset,hex(offset),[(i,a,b) for i,(a,b) in enumerate(zip(actual,expected)) if a!=b][:8])
   checks+=1
  if event and not original and not generic and preset in (2,3):
   expected=bytes.fromhex(re.search(r'ReadParticles\("([a-f0-9]+)"',line)[1]);actual=bytes(uc.mem_read(obj+0x8000,len(expected)))
   assert actual==expected,(name,preset,'seeds',[(i,a,b) for i,(a,b) in enumerate(zip(actual,expected)) if a!=b][:8])
   checks+=len(expected)//64
  print(name,'event' if event else 'MagicPrison',preset,'native parameters/colors/seeds byte-identical')
print('Passed',checks,'independent native comparisons')
