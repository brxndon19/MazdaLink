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
HASH = '3d6d56b24d9e2629418beda03b40cc436d168cd47bf33754c759a7c6a00f1623'
BASE, RESULT = 0xFFFF8000, 0xffff856c
F = 0xFFFFE800
MASK = 0xFFFFFFFF


def build():
    text = (ROOT / 'src/MazdaLink.Core/Zyj2RecoveryRehearsal.cs').read_text()
    literal = text.split('byte[] image = Convert.FromHexString(', 1)[1].split(');', 1)[0]
    embedded = bytes.fromhex(''.join(re.findall(r'"([0-9A-F]+)"', literal)))
    with tempfile.TemporaryDirectory(prefix='MazdaLink-protected-init-') as folder:
        p = pathlib.Path(folder)
        subprocess.run(['sh-elf-as', '-big', '-isa=sh2', '-o', str(p/'p.o'), str(ROOT/'firmware/zyj2-recovery-rehearsal.S')], check=True)
        subprocess.run(['sh-elf-ld', '-T', str(ROOT/'firmware/zyj2-ram-selftest.ld'), '-o', str(p/'p.elf'), str(p/'p.o')], check=True)
        subprocess.run(['sh-elf-objcopy', '-O', 'binary', str(p/'p.elf'), str(p/'p.bin')], check=True)
        image = (p/'p.bin').read_bytes()
    assert image == embedded and len(image) == 1404
    assert hashlib.sha256(image).hexdigest() == HASH
    assert image[RESULT-BASE:RESULT-BASE+6] == b'MR\xff\xff\0\0'
    return image


def execute(image, fault=None, original_sr=0xF1, original_vbr=0x1000):
    fault = fault or {}
    mem = dict(enumerate(image, BASE))
    # No application memory is mapped: any C0091 read fails immediately.
    mem.update(enumerate(b'ZYC3',0x6200));mem.update(enumerate(bytes.fromhex('0001FFFE'),0x7000));mem[0xFFFFBFAD]=0x77
    if fault.get('identity'):mem[0x6200]^=1
    if 'counter' in fault:mem.update(enumerate(fault['counter'],0x7000))
    if fault.get('marker'):mem[0xFFFFBFAD]=0
    mmio = {F+i:0 for i in [0,1,2,4,5,6]}
    mmio.update({0xFFFFEC26:0, 0xFFFFEC27:0, 0xFFFFD004:0xE3, 0xFFFFD005:0x20,
                 0xFFFFD006:0, 0xFFFFD007:1, 0xFFFFD804:0xE3, 0xFFFFD805:0x20, 0xFFFFD806:0, 0xFFFFD807:1, 0xFFFFF743:0xFE, 0xFFFFF740:0xFE, 0xFFFFF784:0xFE})
    mmio.update(fault.get('registers', {}))
    mmio.update({0xFFFFEC10:0x3D,0xFFFFF730:0x90,0xFFFFF732:0x20})
    mmio.update(fault.get('registers',{}))
    initial = mmio.copy()
    latch=fault.get('latch',0x90);external=fault.get('external',0x27);pb_writes=0
    cycles=0;next_overflow=262144;callback_frames=[];pr=0;gbr=0xDEAD0000;ram_only_calls=0
    state=RESULT+8;callback=0xFFFF84CC
    r = [0]*16; r[15] = 0xFFFFBFA0
    sr, vbr, pc = original_sr, original_vbr, BASE
    writes, copies, calls, result = [], [], [], None

    def read(a):
        if callback_frames:assert not 0<=a<0x100000, 'Callback attempted a user-MAT read'
        if a==0xFFFFEC10:
            overflow=cycles>=next_overflow and not fault.get('never_overflows')
            return mmio[a] | (128 if overflow else 0)
        if a==0xFFFFF738:return (latch&0x90)|(external&0x6F)
        if a==0xFFFFF782:
            return ((latch&0x90)|(external&0x6F)) ^ (128 if fault.get('pin_stuck') and pb_writes>1 else 0)
        if a in mem: return mem[a]
        assert a in mmio, f'Unreviewed memory read {a:08X}'
        return mmio[a]
    def word(a): return (read(a)<<8) | read(a+1)
    def sx(v, bits): return (v-(1<<bits) if v & (1<<(bits-1)) else v) & MASK
    def set_t(value):
        nonlocal sr
        sr = (sr & ~1) | bool(value)
    def write(a, value):
        nonlocal latch,pb_writes
        value &= 255
        writes.append((a,value))
        if RESULT+2 <= a < RESULT+6 or state<=a<state+3:
            mem[a] = value; return
        if a in (0xFFFF1000,0xFFFF2000):
            assert value == 255
            mem[a] = value; return
        if a==0xFFFFF738:
            assert (value&0x10)==(fault.get('latch',0x90)&0x10), 'Unrelated PB12 output changed'
            if pb_writes==0:assert value==read(a), 'Normalization must not change an output phase'
            else:assert value==(latch^128), 'Only shadow PB15 may toggle after normalization'
            pb_writes+=1;latch=value;return
        assert a in (F,F+1,F+2,F+4,F+6), f'Unreviewed MMIO write {a:08X}'
        if a == F+4: assert value in (0,0xA5), 'Programming key is forbidden'
        if a in (F+1,F+2): assert value in (0,1)
        if a == F+6: assert value in (0,2,4)
        if a != F:
            mmio[a] = value
            if fault.get('stuck_selection') and a == F+1 and value == 0 and len(copies)==2: mmio[a]=1
            return
        assert value == 1 and mmio[F] == 0 and mmio[F+4] == 0xA5
        assert vbr == 0 and (sr & 0xF0) == 0xF0
        assert mmio[F+5] == 0 and mmio[0xFFFFEC26] == mmio[0xFFFFEC27] == 0
        assert [word(pc+2+i*2) for i in range(8)] == [9]*8, 'Eight immediate NOPs required'
        selection = (mmio[F+1], mmio[F+2], mmio[F+6])
        assert selection in ((1,0,2),(0,1,4))
        kind = 'program' if selection[0] else 'erase'
        base = 0xFFFF1000 if kind == 'program' else 0xFFFF2000
        assert mem[base] == 255
        copies.append(kind)
        if not fault.get(kind+'_missing'):
            for x in range(3072): mem[base+x] = 0xA5
            mem[base] = fault.get(kind+'_dpfr',0)
        if fault.get(kind+'_tdar'): mmio[F+6] |= 128
        if fault.get(kind+'_copy_protection'): mmio[F] = 0x80

    def write_word(a,value):
        nonlocal next_overflow
        assert a==0xFFFFEC10 and value&0xFFFF==0xA53D and read(a)&128
        assert mmio[a]&127==0x3D and (sr&0xF0)==0xF0
        writes.append((a,value&0xFFFF))
        while next_overflow<=cycles:next_overflow+=262144

    def enter_callback(return_pc,init_return=None):
        nonlocal pc,pr,ram_only_calls
        callback_frames.append((r[8:16].copy(),gbr,return_pc,init_return))
        ram_only_calls+=1;pr=return_pc;pc=callback
    for count in range(40000):
        cycles+=2
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
        elif op & 0xF00F == 0x600C:r[n]=r[m]&255
        elif op & 0xF00F == 0x600D:r[n]=r[m]&65535
        elif op & 0xF00F == 0x3002:set_t(r[n]>=r[m])
        elif op & 0xFF00 == 0x8500:r[0]=sx(word(r[(op>>4)&15]+(op&15)*2),16)
        elif op & 0xF00F == 0x2001:write_word(r[n],r[m])
        elif op & 0xF000 == 0x9000:r[n]=sx(word(pc+4+(op&255)*2),16)
        elif op & 0xF00F == 0x6003: r[n] = r[m]
        elif op & 0xFF00 == 0x8400: r[0] = sx(read(r[(op>>4)&15]+(op&15)),8)
        elif op & 0xFF00 == 0x8000: write(r[(op>>4)&15]+(op&15),r[0])
        elif op & 0xF00F == 0x2000: write(r[n],r[m])
        elif op & 0xF00F == 0x3000: set_t(r[n]==r[m])
        elif op & 0xF0FF == 0x4010:
            # Exact pure DT/BF loop summary: no memory accesses or calls inside.
            if n==4 and r[n]==100000:
                branch=word(pc+2);assert branch&0xFF00==0x8B00
                assert (pc+6+sx(branch&255,8)*2)&MASK==pc
                cycles+=400000;r[n]=0;set_t(True);pc+=4;continue
            r[n] = (r[n]-1)&MASK; set_t(r[n]==0)
        elif op & 0xF00F == 0x2008: set_t((r[n]&r[m])==0)
        elif op & 0xF00F == 0x2009: r[n] &= r[m]
        elif op & 0xF00F == 0x200A: r[n] ^= r[m]
        elif op & 0xF00F == 0x200B: r[n] |= r[m]
        elif op & 0xFF00 == 0xC800:set_t((r[0]&(op&255))==0)
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
        elif op & 0xF000==0xB000:
            assert word(pc+2)==9
            target=(pc+4+sx(op&4095,12)*2)&MASK;assert target==callback
            enter_callback(pc+4);continue
        elif op==0x000B:
            assert word(pc+2)==9 and callback_frames
            saved,saved_gbr,target,init_return=callback_frames.pop()
            assert r[8:16]==saved and gbr==saved_gbr and pr==target,'Callback ABI violated'
            if init_return is not None:r[0]=init_return
            pc=target;continue
        elif op & 0xF0FF in (0x400B,0x402B):
            assert word(pc+2)==9
            target=r[n];calls.append(target)
            if callback_frames:assert target>=BASE,'ROM call from callback'
            if target==callback:
                enter_callback(pc+4);continue
            elif target==0xEE4:r[0]=fault.get('class',1);r[6]=0xDEADBEEF
            elif target in (0xFFFF1020,0xFFFF2020):
                kind='program' if target==0xFFFF1020 else 'erase'
                assert kind in copies and mem[target-32]==0
                assert r[4]==8000 and r[5]==callback
                assert (sr&0xF0)==0xF0 and mmio[F]==mmio[F+4]==mmio[F+5]==0
                init_result=fault.get(kind+'_init',0)
                if fault.get(kind+'_init_protection'):mmio[F]=0x80
                if fault.get('stuck_selection') and kind=='erase':pass
                if fault.get('timer_mode_changes') and kind=='erase':mmio[0xFFFFEC10]=0x3C
                if fault.get('simulate_silicon_callback'):
                    enter_callback(pc+4,init_result);continue
                r[0]=init_result
            elif target==0x4C2:
                assert sr==original_sr and vbr==original_vbr
                assert r[4]&255==0xC0 and r[5]==6 and r[6]==RESULT
                result=bytes(read(RESULT+i) for i in range(6))
            elif target==0x40:
                assert r[4]==0x924 and result is not None and result[:2]==b'MR'
                assert sr==original_sr and vbr==original_vbr
                assert not callback_frames
                for a in range(BASE,BASE+len(image)):
                    if not(RESULT+2<=a<RESULT+6 or state<=a<state+3):assert mem[a]==image[a-BASE]
                return result,copies,calls,writes,mmio,initial,pb_writes,ram_only_calls
            else:raise AssertionError(f'Forbidden call {target:08X}, including operation entry or ROM watchdog')
            advance=4
        else:raise AssertionError(f'Unreviewed instruction {op:04X} at {pc:08X}')
        pc=(pc+advance)&MASK
    raise AssertionError('Instruction bound exceeded')

def main():
    image=build();cases=[]
    for sr in (0,1,0xF0,0xF1):
        for vbr in (0,0x1000):
            for pins,latch in ((0,0x90),(0x6F,0),(0x23,0x10)):
                cases.append(({'external':pins,'latch':latch},0,sr,vbr))
    for cls in (0,2,255):cases.append(({'class':cls},1,0xF1,0x1000))
    for a,val,status in [(F,128,2),(F,16,2),(F+4,0x5A,2),(F+5,0xAA,2),(0xFFFFEC26,1,3),(0xFFFFEC27,1,3),
        (0xFFFFD004,0,4),(0xFFFFD007,2,4),(0xFFFFD804,0,4),(0xFFFFD807,2,4),(0xFFFFF743,255,5),
        (0xFFFFF740,255,5),(0xFFFFF784,255,5),(F+1,1,6),(F+2,1,6),(F+6,2,6),(0xFFFFEC10,0x3C,14),
        (0xFFFFEC10,0x7D,14),(0xFFFFF730,0,14),(0xFFFFF732,0xC0,14)]:cases.append(({'registers':{a:val}},status,0xF1,0x1000))
    cases.extend([({'identity':True},12,0xF1,0x1000),({'counter':bytes.fromhex('0064FF9B')},13,0xF1,0x1000),
        ({'counter':bytes.fromhex('00010000')},13,0xF1,0x1000),({'marker':True},14,0xF1,0x1000),
        ({'never_overflows':True},15,0xF1,0x1000),({'pin_stuck':True},16,0xF1,0x1000),
        ({'timer_mode_changes':True},16,0xF1,0x1000),({'simulate_silicon_callback':True},0,0xF1,0x1000),({'stuck_selection':True},11,0xF1,0x1000)])
    for kind,download,init in [('program',7,8),('erase',9,10)]:
        for val in (1,2,4,255):cases.append(({kind+'_dpfr':val},download,0xF1,0x1000))
        cases.append(({kind+'_missing':True},download,0xF1,0x1000));cases.append(({kind+'_tdar':True},download,0xF1,0x1000))
        for val in (1,2,4,0x100,0x80000000):cases.append(({kind+'_init':val},init,0xF1,0x1000))
        for step in ('copy','init'):cases.append(({kind+'_'+step+'_protection':True},11,0xF1,0x1000))
    for fault,status,sr,vbr in cases:
        result,copies,calls,writes,regs,initial,pb_writes,ram_calls=execute(image,fault,sr,vbr)
        assert result[2]==status,(fault,result,status)
        if status==0:
            assert result[3]==0 and 8<=result[4]<=64 and result[5]==31
            assert copies==['program','erase'] and ram_calls>=64
            assert calls.count(0xFFFF1020)==calls.count(0xFFFF2020)==1
        if status in (1,2,3,4,5,6,12,13,14):assert not copies and not pb_writes
        if status in (7,8):assert 'erase' not in copies
        if status not in (0,11) and copies:assert all(regs[F+i]==0 for i in (0,1,2,4,5,6))
        if fault.get('pin_stuck'):assert pb_writes==2,'Failed pin was retried'
        if fault.get('timer_mode_changes'):assert pb_writes==1,'Changed timer mode was serviced'
        assert all(a not in (0xFFFFF740,0xFFFFF746) for a,_ in writes)
    print(f'PASS: exact {len(image)}-byte recovery rehearsal; {len(cases)} SH-2 success/fault cases; no application memory mapped, RAM callback ABI/flash-read prohibition, keyed timer service, shadow PB15 writes, controller cleanup and SR/VBR return checked. No erase/program entry. sha256:{HASH}')

if __name__=='__main__':main()
