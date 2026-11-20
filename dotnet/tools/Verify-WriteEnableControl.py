#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Rebuild owned SH-2 code; independently execute a restricted instruction model.
No driver, customer image, external kernel or ECU access. MCU routine bodies are
abstracted at their documented entry/ABI, so this is not hardware qualification.
"""
import hashlib
import pathlib
import re
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
HASH = '5d6a904f85c225e12f29d171fcc1c1a88fc1bd27ac55ee5a35083b73909326c9'
BASE, RESULT = 0xFFFF8000, 0xffff82d8
F = 0xFFFFE800
MASK = 0xFFFFFFFF


def build():
    text = (ROOT / 'src/MazdaLink.Core/Zyj2WriteEnableControl.cs').read_text()
    literal = text.split('byte[] image = Convert.FromHexString(', 1)[1].split(');', 1)[0]
    embedded = bytes.fromhex(''.join(re.findall(r'"([0-9A-F]+)"', literal)))
    with tempfile.TemporaryDirectory(prefix='MazdaLink-protected-init-') as folder:
        p = pathlib.Path(folder)
        subprocess.run(['sh-elf-as', '-big', '-isa=sh2', '-o', str(p/'p.o'), str(ROOT/'firmware/zyj2-write-enable.S')], check=True)
        subprocess.run(['sh-elf-ld', '-T', str(ROOT/'firmware/zyj2-ram-selftest.ld'), '-o', str(p/'p.elf'), str(p/'p.o')], check=True)
        subprocess.run(['sh-elf-objcopy', '-O', 'binary', str(p/'p.elf'), str(p/'p.bin')], check=True)
        image = (p/'p.bin').read_bytes()
    assert image == embedded and len(image) == 752
    assert hashlib.sha256(image).hexdigest() == HASH
    assert image[RESULT-BASE:RESULT-BASE+6] == b'MW\xff\xff\xff\x00'
    return image


def execute(image,fault=None,original_sr=0xF1,original_vbr=0x1000):
    fault=fault or {}
    mem=dict(enumerate(image,BASE));mem.update(enumerate(b'ZYJ2EA000ZYV5050',0xC0091))
    mem.update(enumerate(bytes.fromhex('0001FFFE'),0x7000))
    if fault.get('identity'):mem[0xC0091]^=1
    if 'counter' in fault:mem.update(enumerate(fault['counter'],0x7000))
    mmio={F+i:0 for i in (0,1,2,4,5,6)}
    mmio.update({0xFFFFEC26:0,0xFFFFEC27:0,0xFFFFD004:0xE3,0xFFFFD005:0x20,0xFFFFD006:0,0xFFFFD007:1,
                 0xFFFFD804:0xE3,0xFFFFD805:0x20,0xFFFFD806:0,0xFFFFD807:1,
                 0xFFFFF741:0,0xFFFFF742:0,0xFFFFF743:0,0xFFFFF744:0,0xFFFFF745:0})
    mmio.update(fault.get('registers',{}))
    latch,latch_low,direction=0x3E,0xFF,fault.get('direction',0)
    external=fault.get('external_pins',0xFE)&255
    writes,calls,result=[],[],None
    r=[0]*16;r[15]=0xFFFFBFA0;sr,vbr,pc=original_sr,original_vbr,BASE
    changed=False
    def pin():
        if direction&1:return (external&254)|(latch&1)
        return external
    def read(a):
        if a in mem:return mem[a]
        if a==0xFFFFF740:return direction
        if a==0xFFFFF746:return ((external&254)|(latch&1)) if direction&1 else external
        if a==0xFFFFF784:return pin()
        if a==F:
            enabled=bool(direction&latch&1) and not fault.get('not_wired')
            if changed and not direction&1 and fault.get('fwe_stuck_after'):enabled=True
            return mmio[F]|(128 if enabled else 0)
        assert a in mmio,f'Unreviewed memory read {a:08X}'
        return mmio[a]
    def word(a):return (read(a)<<8)|read(a+1)
    def sx(v,bits):return (v-(1<<bits) if v&(1<<(bits-1)) else v)&MASK
    def set_t(value):
        nonlocal sr
        sr=(sr&~1)|bool(value)
    def write(a,value):
        nonlocal latch,direction,changed
        value&=255;writes.append((a,value))
        if RESULT+2<=a<RESULT+6:mem[a]=value;return
        assert (sr&0xF0)==0xF0
        assert a in (0xFFFFF746,0xFFFFF740),f'Forbidden MMIO write {a:08X}'
        if a==0xFFFFF746:
            assert value in (0x3E,0x3F)
            changed=True
            if fault.get('latch_stuck') and value==0x3E:latch=0x3F
            else:latch=value
        else:
            assert value in (0,1)
            if not(fault.get('direction_stuck') and value==0):direction=value
    for count in range(20000):
        op = word(pc); n, m = (op>>8)&15, (op>>4)&15
        advance = 2
        if op == 9: pass
        elif op & 0xF000 == 0xD000:
            a = ((pc+4)&~3)+(op&255)*4
            r[n] = (word(a)<<16) | word(a+2)
        elif op & 0xF000 == 0xE000: r[n] = sx(op&255,8)
        elif op & 0xF000 == 0x7000: r[n] = (r[n]+sx(op&255,8))&MASK
        elif op & 0xF00F == 0x6000: r[n] = sx(read(r[m]),8)
        elif op & 0xF00F == 0x6001: r[n] = sx(word(r[m]),16)
        elif op & 0xF00F == 0x600C: r[n] = r[m]&255
        elif op & 0xF00F == 0x600D: r[n] = r[m]&0xFFFF
        elif op & 0xF00F == 0x3002: set_t(r[n]>=r[m])
        elif op & 0xF000 == 0x8000 and op & 0xF00 == 0x500:
            r[0]=sx(word(r[(op>>4)&15]+(op&15)*2),16)
        elif op & 0xF00F == 0x6003: r[n] = r[m]
        elif op & 0xFF00 == 0x8400: r[0] = sx(read(r[(op>>4)&15]+(op&15)),8)
        elif op & 0xFF00 == 0x8000: write(r[(op>>4)&15]+(op&15),r[0])
        elif op & 0xF00F == 0x2000: write(r[n],r[m])
        elif op & 0xF00F == 0x3000: set_t(r[n]==r[m])
        elif op & 0xF0FF == 0x4010:
            r[n] = (r[n]-1)&MASK; set_t(r[n]==0)
        elif op & 0xF00F == 0x2008: set_t((r[n]&r[m])==0)
        elif op & 0xF00F == 0x2009: r[n] &= r[m]
        elif op & 0xF00F == 0x200A: r[n] ^= r[m]
        elif op & 0xF00F == 0x200B: r[n] |= r[m]
        elif op & 0xFF00 == 0xC900: r[0] &= op&255
        elif op & 0xFF00 == 0xCA00: r[0] ^= op&255
        elif op & 0xFF00 == 0xCB00: r[0] |= op&255
        elif op & 0xF0FF == 0x4008: r[n] = (r[n]<<2)&MASK
        elif op & 0xF0FF == 0x0002: r[n] = sr
        elif op & 0xF0FF == 0x0022: r[n] = vbr
        elif op & 0xF0FF == 0x400E: sr = r[n]
        elif op & 0xF0FF == 0x402E: vbr = r[n]
        elif op & 0xFF00 == 0x8900:
            if sr&1: advance = 4+sx(op&255,8)*2
        elif op & 0xFF00 == 0x8B00:
            if not sr&1: advance = 4+sx(op&255,8)*2
        elif op & 0xF000 == 0xA000:
            assert word(pc+2)==9
            offset = op&0xFFF; offset = offset-4096 if offset&2048 else offset
            advance = 4+offset*2
        elif op & 0xF0FF in (0x400B,0x402B):
            assert word(pc+2)==9
            target=r[n];calls.append(target)
            if target==0xEE4:
                r[0]=fault.get('class',1);r[6]=0xDEADBEEF
            elif target==0x484:
                assert not direction & 1 and not pin() & 1
                # Reviewed resident helper touches watchdog/PB15, never port D.
            elif target==0x4C2:
                assert sr==original_sr and vbr==original_vbr
                assert r[4]&255==0xC0 and r[5]==6 and r[6]==RESULT
                result=bytes(read(RESULT+i) for i in range(6))
            elif target==0x40:
                assert r[4]==0x924 and result is not None and result[:2]==b'MW'
                assert sr==original_sr and vbr==original_vbr
                assert all(RESULT+2<=a<RESULT+6 or a in (0xFFFFF746,0xFFFFF740) for a,_ in writes)
                assert latch_low==0xFF
                for a in range(BASE,BASE+len(image)):
                    if not RESULT+2<=a<RESULT+6: assert mem[a]==image[a-BASE]
                return result,writes,latch,direction,calls
            else:raise AssertionError(f'Forbidden call {target:08X}')
            advance=4
        else:raise AssertionError(f'Unreviewed instruction {op:04X} at {pc:08X}')
        pc=(pc+advance)&MASK
    raise AssertionError('Instruction bound exceeded')

def main():
    image=build();cases=[]
    for pins in (0,0x22,0xFE):
        for sr in (0,1,0xF0,0xF1):
            for vbr in (0,0x1000):cases.append(({'external_pins':pins},0,sr,vbr))
    for cls in (0,2,255):cases.append(({'class':cls},1,0xF1,0x1000))
    cases.append(({'identity':True},2,0xF1,0x1000))
    for counter in (bytes.fromhex('0064FF9B'),bytes.fromhex('FFFFFFFF'),bytes.fromhex('00010000'),bytes.fromhex('80007FFF')):
        cases.append(({'counter':counter},3,0xF1,0x1000))
    for a,val,status in [(F,128,4),(F,16,4),(F+1,1,4),(F+2,1,4),(F+4,0x5A,4),(F+5,0xAA,4),(F+6,2,4),
        (0xFFFFEC26,1,4),(0xFFFFEC27,1,4),(0xFFFFD004,0,5),(0xFFFFD007,2,5),(0xFFFFD804,0,5),(0xFFFFD807,2,5),
        (0xFFFFF741,1,6),(0xFFFFF742,1,6),(0xFFFFF743,1,6),(0xFFFFF744,1,6),(0xFFFFF745,1,6)]:
        cases.append(({'registers':{a:val}},status,0xF1,0x1000))
    cases.extend([({'direction':2},6,0xF1,0x1000),({'external_pins':1},6,0xF1,0x1000),
        ({'not_wired':True},7,0xF1,0x1000),({'latch_stuck':True},8,0xF1,0x1000),
        ({'direction_stuck':True},8,0xF1,0x1000),({'fwe_stuck_after':True},8,0xF1,0x1000)])
    for fault,status,sr,vbr in cases:
        result,writes,latch,direction,calls=execute(image,fault,sr,vbr)
        assert result[2]==status,(fault,result,status)
        gpio=[(a,v) for a,v in writes if a in (0xFFFFF746,0xFFFFF740)]
        if 1<=status<=6:assert not gpio
        if status==0 or status>=7:assert gpio==[(0xFFFFF746,63),(0xFFFFF740,1),(0xFFFFF746,62),(0xFFFFF740,0)]
        if status==0:assert result==b'MW\0\x80\0\x1F' and latch==62 and direction==0
        if status==7:assert result[5]==29 and latch==62 and direction==0
        assert all(t in (0xEE4,0x484,0x4C2,0x40) for t in calls)
    print(f'PASS: exact {len(image)}-byte write-enable rebuild; {len(cases)} independent SH-2 success/fault cases; only four fixed GPIO byte writes, cold latches restored with varying input pins; no flash-controller write or algorithm call; SR/VBR/return checks. sha256:{HASH}')

if __name__=='__main__':main()
