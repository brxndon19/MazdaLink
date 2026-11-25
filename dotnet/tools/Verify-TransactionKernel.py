#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Execute the assembled SH-2 transaction binaries against synthetic memory.
Silicon routines are ABI models, not emulation of flash pulse timing or hardware
recovery. No private ROM, driver, ECU or external kernel is used.
"""
import binascii, hashlib, importlib.util, pathlib, re, struct
P=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('builder',P/'Build-TransactionKernel.py')
builder=importlib.util.module_from_spec(spec);spec.loader.exec_module(builder)
MASK=0xffffffff; BASE=0xffff8000; F=0xffffe800; STATE=0xffffa0c0

def sx(v,b):return (v-(1<<b) if v&(1<<(b-1)) else v)&MASK
class Machine:
 def __init__(self,image,symbols,full=False):
  self.code=image;self.symbols=symbols;self.full=full
  self.mem=dict(enumerate(image,BASE));self.rom=bytearray((i*11+7)&255 for i in range(0x100000))
  self.rom[0x6200:0x6204]=b'ZYC3';self.rom[0x7000:0x7004]=bytes.fromhex('0001fffe')
  self.original=bytes(self.rom);self.mem.update(dict.fromkeys(range(STATE,STATE+48),0))
  self.mem[0xffffbfad]=119;self.mem[0xffffbfa8]=0
  self.io={F+i:0 for i in range(8)}
  for a,values in [(0xffffec26,b'\0\0'),(0xffffecb0,b'\0\0'),(0xffffd004,bytes.fromhex('e3200001')),(0xffffd804,bytes.fromhex('e3200001')),(0xfffff740,bytes(8)),(0xfffff730,bytes.fromhex('900020'))]:self.io.update(enumerate(values,a))
  self.io[0xffffec10]=61;self.pb=0xb7;self.pd=0x3e;self.cycles=0;self.overflow=262144
  self.r=[0]*16;self.r[15]=0xffffbfa0;self.sr=0;self.vbr=0x1000;self.pr=0;self.pc=BASE
  self.frames=[];self.writes=[];self.operations=[];self.fault={};self.busy=False;self.callbacks=0;self.copies=[]
 def read(self,a,size=1):
  if size!=1:return sum(self.read(a+i)<<(8*(size-1-i)) for i in range(size))
  if 0<=a<0x100000:
   assert not self.busy,'User/boot MAT read while busy';return self.rom[a]
  if a==F:return self.io[F]|(128 if self.io[0xfffff740]&1 and self.pd&1 else 0)
  if a==0xfffff746:return (self.pd&1) if self.io[0xfffff740]&1 else 0
  if a==0xfffff784:return (self.pd&1) if self.io[0xfffff740]&1 else 0
  if a in (0xfffff738,0xfffff782):return self.pb
  if a==0xffffec10:return self.io[a]|(128 if self.cycles>=self.overflow else 0)
  if a in self.mem:return self.mem[a]
  assert a in self.io, f'Unknown read {a:08x} at {self.pc:08x}'
  return self.io[a]
 def write(self,a,v,size=1):
  if size==2 and a==0xffffec10:
   assert v&65535==0xa53d and self.read(a)&128
   self.overflow=self.cycles+262144;self.writes.append((a,v&65535,2));return
  if size!=1:
   for i in range(size):self.write(a+i,v>>(8*(size-1-i)))
   return
  v&=255;self.writes.append((a,v,1))
  if 0xffff6000<=a<0xffff6400 or 0xffffa000<=a<0xffffa106 or 0xffffbda0<=a<0xffffbfa0 or a in (0xffff1000,0xffff2000):self.mem[a]=v;return
  if a==0xfffff738:self.pb=v;return
  if a==0xfffff746:assert self.full and v in (0x3e,0x3f);self.pd=v;return
  if a==0xfffff740:assert self.full and v in (0,1);self.io[a]=v;return
  assert a in (F,F+1,F+2,F+4,F+6),f'Forbidden write {a:08x}'
  if a==F+4:assert v in ((0,0xa5,0x5a) if self.full else (0,0xa5))
  if a==F:
   assert v==1 and self.io[F+4]==0xa5 and self.vbr==0 and self.sr&0xf0==0xf0
   assert all(self.read(self.pc+2+i*2,2)==9 for i in range(8)),'Missing SCO NOPs'
   sel=(self.io[F+1],self.io[F+2],self.io[F+6]);assert sel in ((1,0,2),(0,1,4))
   base=0xffff1000 if sel[0] else 0xffff2000;self.mem[base]=self.fault.get('dpfr',0);self.copies.append(base);return
  self.io[a]=v
 def t(self,v):self.sr=(self.sr&~1)|bool(v)
 def native(self,target):
  if target==0xee4:
   self.r[0]=self.fault.get('class',1)
   for i in range(1,8):self.r[i]=0xdead0000+i
  elif target==0x4c2:
   assert not self.busy and self.r[5]==6
   kind=self.r[4]&255;assert kind in (0xd0,0xf0,0xc0)
   self.frames.append(bytes([0x7a,kind|6])+bytes(self.read(self.r[6]+i) for i in range(6)))
   self.cycles+=20000
   for i in range(8):self.r[i]=0xdead1000+i
  elif target in (0xffff1020,0xffff2020):
   assert target-32 in self.copies and self.r[4]==8000 and self.r[5]==self.symbols['transaction_service']
   assert self.read(F)==0 and self.io[F+4]==0
   self.r[0]=self.fault.get('init_result',0)
  elif target in (0xffff1010,0xffff2010):
   assert self.full and self.read(F)==128 and self.io[F+4]==0x5a and self.sr&0xf0==0xf0 and self.vbr==0xffff6000
   for i in range(256):assert self.read(self.vbr+i*4,4)==self.symbols['transaction_nmi' if i==11 else 'transaction_trap']
   saved=self.r.copy();oldpr=self.pr;returnpc=self.pc;self.busy=True
   # Run the real RAM callback while user/boot MAT reads are forbidden.
   self.cycles+=300000;self.pc=self.symbols['transaction_service'];self.pr=0xfffffff0
   self.run(0xfffffff0,10000)
   assert self.r[8:]==saved[8:];self.callbacks+=1;self.r=saved;self.pr=oldpr;self.pc=returnpc
   if self.fault.get('nmi'):
    saved_sr=self.sr;self.r[15]-=4;self.write(self.r[15],self.sr,4);self.r[15]-=4;self.write(self.r[15],0xfffffff0,4)
    self.pc=self.symbols['transaction_nmi'];self.run(0xfffffff0,10000);assert self.sr==saved_sr and self.r[8:]==saved[8:]
    self.pc=returnpc
   self.busy=False
   if target==0xffff2010:
    assert self.r[4]==14;self.rom[0xc0000:0xe0000]=b'\xff'*0x20000;self.operations.append(('erase',14))
   else:
    assert self.r[4]==0xffffa000;address=self.r[5];assert 0xc0000<=address<=0xdff80 and address%128==0
    assert ('program',address) not in self.operations[self.operations.index(('erase',14))+1:],'Duplicate programming in erase generation'
    data=bytes(self.read(0xffffa000+i) for i in range(128))
    for i,b in enumerate(data):self.rom[address+i]&=b
    if self.fault.get('readback'):self.rom[address]^=1
    self.operations.append(('program',address))
   self.r[0]=self.fault.get('operation_result',0)
  else:return False
  self.pc=self.pr;return True
 def instruction(self,slot=False):
  op=self.read(self.pc,2);n=(op>>8)&15;m=(op>>4)&15;pc=self.pc;self.cycles+=2
  dest=None;delayed=False
  if op==9:pass
  elif op&0xf0ff==0x0029:self.r[n]=self.sr&1
  elif op&0xf000==0xe000:self.r[n]=sx(op&255,8)
  elif op&0xf000==0xd000:self.r[n]=self.read(((pc+4)&~3)+(op&255)*4,4)
  elif op&0xf000==0x9000:self.r[n]=sx(self.read(pc+4+(op&255)*2,2),16)
  elif op&0xf000==0x7000:self.r[n]=(self.r[n]+sx(op&255,8))&MASK
  elif op&0xf000==0x5000:self.r[n]=self.read(self.r[m]+(op&15)*4,4)
  elif op&0xf000==0x1000:self.write(self.r[n]+(op&15)*4,self.r[m],4)
  elif op&0xf00f in (0x6000,0x6001,0x6002,0x6004,0x6005,0x6006):
   size=1<<((op&15)%4);v=self.read(self.r[m],size);self.r[n]=sx(v,size*8) if size<4 else v
   if op&4 and n!=m:self.r[m]=(self.r[m]+size)&MASK
  elif op&0xf00f in (0x2000,0x2001,0x2002,0x2004,0x2005,0x2006):
   size=1<<((op&15)%4)
   if op&4:self.r[n]=(self.r[n]-size)&MASK
   self.write(self.r[n],self.r[m],size)
  elif op&0xf00f==0x6003:self.r[n]=self.r[m]
  elif op&0xff00 in (0x8400,0x8500):
   size=1 if op&0xff00==0x8400 else 2;self.r[0]=sx(self.read(self.r[m]+(op&15)*size,size),size*8)
  elif op&0xff00 in (0x8000,0x8100):
   size=1 if op&0xff00==0x8000 else 2;self.write(self.r[m]+(op&15)*size,self.r[0],size)
  elif op&0xf00f in (0x000c,0x000d,0x000e):
   size=1<<((op&15)-12);v=self.read((self.r[m]+self.r[0])&MASK,size);self.r[n]=sx(v,size*8) if size<4 else v
  elif op&0xf00f in (0x0004,0x0005,0x0006):self.write((self.r[n]+self.r[0])&MASK,self.r[m],1<<((op&15)-4))
  elif op&0xf00f==0x600c:self.r[n]=self.r[m]&255
  elif op&0xf00f==0x600d:self.r[n]=self.r[m]&65535
  elif op&0xf00f==0x600e:self.r[n]=sx(self.r[m]&255,8)
  elif op&0xf00f==0x600f:self.r[n]=sx(self.r[m]&65535,16)
  elif op&0xf00f==0x6007:self.r[n]=~self.r[m]&MASK
  elif op&0xf00f==0x600a:
   v=self.r[m]+(self.sr&1);self.r[n]=-v&MASK;self.t(v!=0)
  elif op&0xf00f==0x600b:self.r[n]=-self.r[m]&MASK
  elif op&0xf00f==0x6008:self.r[n]=(self.r[m]&0xffff0000)|((self.r[m]&255)<<8)|((self.r[m]>>8)&255)
  elif op&0xf00f==0x6009:self.r[n]=((self.r[m]<<16)|(self.r[m]>>16))&MASK
  elif op&0xf00f==0x300c:self.r[n]=(self.r[n]+self.r[m])&MASK
  elif op&0xf00f==0x3008:self.r[n]=(self.r[n]-self.r[m])&MASK
  elif op&0xf00f==0x3000:self.t(self.r[n]==self.r[m])
  elif op&0xf00f==0x3002:self.t(self.r[n]>=self.r[m])
  elif op&0xf00f==0x3006:self.t(self.r[n]>self.r[m])
  elif op&0xf00f==0x3003:self.t((self.r[n]^0x80000000)>=(self.r[m]^0x80000000))
  elif op&0xf00f==0x3007:self.t((self.r[n]^0x80000000)>(self.r[m]^0x80000000))
  elif op&0xff00==0x8800:self.t(self.r[0]==sx(op&255,8))
  elif op&0xf00f==0x2008:self.t((self.r[n]&self.r[m])==0)
  elif op&0xf00f==0x2009:self.r[n]&=self.r[m]
  elif op&0xf00f==0x200a:self.r[n]^=self.r[m]
  elif op&0xf00f==0x200b:self.r[n]|=self.r[m]
  elif op&0xff00==0xc800:self.t((self.r[0]&(op&255))==0)
  elif op&0xff00==0xc900:self.r[0]&=op&255
  elif op&0xff00==0xca00:self.r[0]^=op&255
  elif op&0xff00==0xcb00:self.r[0]|=op&255
  elif op&0xf0ff==0x4011:self.t(self.r[n]<0x80000000)
  elif op&0xf0ff==0x4015:self.t(0<self.r[n]<0x80000000)
  elif op&0xf0ff==0x4010:self.r[n]=(self.r[n]-1)&MASK;self.t(self.r[n]==0)
  elif op&0xf0ff in (0x4000,0x4020):self.t(self.r[n]>>31);self.r[n]=(self.r[n]<<1)&MASK
  elif op&0xf0ff==0x4001:self.t(self.r[n]&1);self.r[n]>>=1
  elif op&0xf0ff in (0x4008,0x4018,0x4028):self.r[n]=(self.r[n]<<{0x08:2,0x18:8,0x28:16}[op&255])&MASK
  elif op&0xf0ff in (0x4009,0x4019,0x4029):self.r[n]>>={0x09:2,0x19:8,0x29:16}[op&255]
  elif op&0xf0ff==0x0002:self.r[n]=self.sr
  elif op&0xf0ff==0x0022:self.r[n]=self.vbr
  elif op&0xf0ff==0x000a:self.r[n]=0
  elif op&0xf0ff==0x002a:self.r[n]=self.pr
  elif op&0xf0ff==0x400e:self.sr=self.r[n]
  elif op&0xf0ff==0x402e:self.vbr=self.r[n]
  elif op&0xf0ff==0x4022:self.r[n]-=4;self.write(self.r[n],self.pr,4)
  elif op&0xf0ff==0x4026:self.pr=self.read(self.r[n],4);self.r[n]+=4
  elif op&0xf0ff==0x402a:self.pr=self.r[n]
  elif op&0xff00 in (0x8900,0x8b00,0x8d00,0x8f00):
   taken=bool(self.sr&1)==(op&0xff00 in (0x8900,0x8d00));delayed=bool(op&0x400)
   if taken:dest=(pc+4+sx(op&255,8)*2)&MASK
   elif delayed:dest=pc+4
  elif op&0xf000 in (0xa000,0xb000):
   dest=(pc+4+sx(op&4095,12)*2)&MASK;delayed=True
   if op&0xf000==0xb000:self.pr=pc+4
  elif op&0xf0ff in (0x400b,0x402b):
   dest=self.r[n];delayed=True
   if op&0xf0ff==0x400b:self.pr=pc+4
  elif op==0x002b:
   dest=self.read(self.r[15],4);self.sr=self.read(self.r[15]+4,4);self.r[15]+=8;delayed=True
  elif op==0x000b:dest=self.pr;delayed=True
  else:raise AssertionError(f'Unreviewed SH-2 instruction {op:04x} at {pc:08x}')
  assert not slot or (dest is None and not delayed),'Control transfer in delay slot'
  if delayed:
   self.pc=pc+2;self.instruction(True);self.pc=dest
  else:self.pc=dest if dest is not None else pc+2
 def run(self,stop=0x40,bound=3000000):
  for _ in range(bound):
   if self.pc==stop:return
   if self.native(self.pc):continue
   self.instruction()
  raise AssertionError('Instruction bound exceeded')
 def command(self,op,seq,index=0,data=None,cookie=0x12345678,crc_override=None):
  if data is not None:self.mem.update(enumerate(data,0xffffa000))
  desc=struct.pack('>BBHIHHI',op,0,seq,cookie,index,0,(crc_override if crc_override is not None else binascii.crc32(data)) if data is not None else 0)
  self.mem.update(enumerate(desc,0xffffa080));self.frames=[];self.pc=BASE;sp=self.r[15];sr=self.sr;vbr=self.vbr
  self.run();assert self.r[15]==sp and self.sr==sr and self.vbr==vbr and self.r[4]==0x924
  dat=b''.join(f[4:] for f in self.frames if f[1]==0xd6);meta=b''.join(f[4:] for f in self.frames if f[1]==0xf6)
  assert len(meta)==32 and self.frames[-1][:4]==b'\x7a\xc6MT'
  magic,cookie2,seq2,op2,status,fpfr,crc,flags,nxt,last=struct.unpack('>IIHBBIIIII',meta)
  assert (magic,cookie2,seq2,op2,last>>16)==(0x4d4c5431,cookie,seq,op,index)
  assert self.frames[-1][4:]==struct.pack('>BBH',op,status,seq)
  if status==0:assert crc==(binascii.crc32(dat) if dat else 0)
  return status,fpfr,flags,nxt,dat

def main():
 builds=builder.build_all();text=(builder.ROOT/'src/MazdaLink.Core/NativeTransactionPolicy.cs').read_text()
 embedded=bytes.fromhex(re.search(r'Convert.FromHexString\("([0-9A-F]+)"\)',text)[1])
 assert embedded==builds[0][0] and hashlib.sha256(embedded).hexdigest() in text
 cases=0
 for full in (False,True):
  image,symbols=builds[int(full)];m=Machine(image,symbols,full)
  assert m.command(0,1)[:4]==(0,0,1,0);cases+=1
  seq=2
  for block in (0,1536,2047):
   reply=m.command(0x11,seq,block);assert reply[0]==0 and reply[4]==bytes(m.rom[block*512:(block+1)*512]);seq+=1;cases+=1
  for unit in (0,1023):
   data=m.original[0xc0000+unit*128:0xc0080+unit*128];reply=m.command(0x12,seq,unit,data);assert reply[0]==0 and reply[4]==data;seq+=1;cases+=1
  if full:
   assert m.command(0x21,seq,14)[:4]==(0,0,3,0);seq+=1;cases+=1
   for unit in (0,1):
    data=m.original[0xc0000+unit*128:0xc0080+unit*128]
    assert m.command(0x22,seq,unit,data)[:4]==(0,0,3,unit+1);seq+=1;cases+=1
   assert m.callbacks==3 and m.rom[:0xc0000]==m.original[:0xc0000] and m.rom[0xe0000:]==m.original[0xe0000:]
   operations=len(m.operations);assert m.command(0x22,seq,0,m.original[0xc0000:0xc0080])[0]==15;cases+=1
   assert m.command(0x22,seq,2,m.original[0xc0100:0xc0180])[0]!=0 and len(m.operations)==operations;cases+=1
  else:
   writes=len(m.writes);assert m.command(0x21,seq,14)[0]==24;seq+=1;cases+=1
   assert not m.operations and not any(a in (0xfffff740,0xfffff746) or (a==F+4 and v==0x5a) for a,v,_ in m.writes[writes:])
 for fault in ({'class':2},{'dpfr':1},{'init_result':0x100}):
  m=Machine(*builds[0],False);m.fault=fault;assert m.command(0,1)[0]!=0 and not m.operations;cases+=1
 for full in (False,True):
  for op,seq,index,cookie in ((0,2,0,0x12345678),(0x11,1,0,0x12345678),(0x11,3,0,0x12345678),(0x11,2,0,1),(0x11,2,2048,0x12345678)):
   m=Machine(*builds[int(full)],full);assert m.command(0,1)[0]==0
   assert m.command(op,seq,index,cookie=cookie)[0]!=0 and not m.operations;cases+=1
  m=Machine(*builds[int(full)],full);assert m.command(0,1)[0]==0
  assert m.command(0x12,2,0,b'X'*128,crc_override=1)[0]==16 and not m.operations;cases+=1
 # Rejected preflights must not service an unowned PB shadow, even with OVF set.
 m=Machine(*builds[0]);m.fault={'class':2};m.cycles=300000;m.mem[STATE+43]=1
 assert m.command(0,1)[0]!=0 and not any(a==0xfffff738 for a,_,_ in m.writes);cases+=1
 for address,value in [(F,16),(F+4,0x5a),(F+5,0xaa),(F+1,1),(F+2,1),(F+6,2),(0xffffec26,1),(0xffffd004,0),(0xffffd007,2),(0xffffd804,0),(0xffffd807,2),(0xfffff740,1),(0xfffff742,1),(0xfffff744,1),(0xfffff730,0),(0xfffff732,0xc0),(0xffffec10,0x3c)]:
  m=Machine(*builds[0]);m.io[address]=value;m.cycles=300000;before=m.pb
  assert m.command(0,1)[0]!=0 and not m.copies and not m.operations and m.pb==before
  assert not any(a==0xfffff738 for a,_,_ in m.writes);cases+=1
 for address,value in ((0xffffbfad,0),(0xffffbfa8,1)):
  m=Machine(*builds[0]);m.mem[address]=value;m.cycles=300000
  assert m.command(0,1)[0]!=0 and not m.copies and not any(a==0xfffff738 for a,_,_ in m.writes);cases+=1
 for kind in ('dma','vector'):
  m=Machine(*builds[1],True);assert m.command(0,1)[0]==0
  if kind=='dma':m.io[0xffffecb1]=1
  else:m.mem[0xffff6000]^=1
  assert m.command(0x21,2,14)[0]==12 and not m.operations and m.read(F)==0;cases+=1
 for fault in ({'operation_result':0x100},{'readback':True},{'nmi':True}):
  m=Machine(*builds[1],True);assert m.command(0,1)[0]==0;assert m.command(0x21,2,14)[0]==0
  m.fault=fault;reply=m.command(0x22,3,0,m.original[0xc0000:0xc0080]);assert reply[0]!=0 and reply[2]&4
  assert m.read(F)==0 and m.io[F+4]==0 and m.io[0xfffff740]==0;cases+=1
 print(f'PASS: {cases} synthetic SH-2 transaction cases; both exact binaries, ordered CRC32 streams, fixed EB14 operations, RAM-only busy callback, full FPFR, taint and cleanup. Offline silicon ABI model; no hardware qualification.')
if __name__=='__main__':main()
