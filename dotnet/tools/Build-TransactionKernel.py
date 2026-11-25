#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Pinned C compiler, then strict SH-2 assembler: no SH-3/4 opcode is accepted.
The compiler configuration exposes an integer SH-4 backend; the owned source
uses the SH-2 subset and both assembler and instruction model enforce that.
Full flash candidate is an offline model artifact, never the embedded variant.
"""
import hashlib
import json
import os
import pathlib
import subprocess
import tempfile
import urllib.request

ROOT=pathlib.Path(__file__).resolve().parents[1]
PACKAGES=[
 ('cpp-14-sh4-linux-gnu','ae88661eb9ebd3aa7ea682124ae76d4e510a0af58566da23b91462ef3d4fa0c7'),
 ('gcc-14-sh4-linux-gnu','d0efa86b07031b6557342e4ea45b4a22981f373f5ba086e8d3470916b169a608'),
 ('gcc-14-sh4-linux-gnu-base','f8ab9276d89799235cf0b9109aedd10a1c7b3a8237df0372f22a22799fe73d35')]
FLAGS=['-m4-nofpu','-mb','-Os','-ffreestanding','-fno-builtin','-fno-pic','-fno-pie',
 '-fno-stack-protector','-fno-tree-loop-distribute-patterns','-fno-jump-tables',
 '-fno-optimize-sibling-calls','-fomit-frame-pointer','-fno-asynchronous-unwind-tables','-fno-unwind-tables']

def compiler(folder):
    override=os.environ.get('MAZDALINK_SH_CC')
    if override:
        cc=pathlib.Path(override).resolve()
    else:
        for name,digest in PACKAGES:
            url='https://deb.debian.org/debian/pool/main/g/gcc-14-cross-ports/'+name+'_14.2.0-19cross1_amd64.deb'
            with urllib.request.urlopen(url,timeout=120) as response:data=response.read()
            assert hashlib.sha256(data).hexdigest()==digest,'Pinned compiler package hash mismatch'
            archive=folder/(name+'.deb');archive.write_bytes(data)
            subprocess.run(['dpkg-deb','-x',str(archive),str(folder)],check=True)
        cc=folder/'usr/bin/sh4-linux-gnu-gcc-14'
    version=subprocess.check_output([str(cc),'--version'],text=True).splitlines()[0]
    assert '(Debian 14.2.0-19) 14.2.0' in version,version
    prefix=cc.parent.parent/'lib/gcc-cross/sh4-linux-gnu/14'
    return [str(cc),'-B'+str(prefix)+'/']

def build_all():
    with tempfile.TemporaryDirectory(prefix='MazdaLink-transactions-') as name:
        folder=pathlib.Path(name);cc=compiler(folder)
        subprocess.run(['sh-elf-as','-big','-isa=sh2','-o',str(folder/'entry.o'),str(ROOT/'firmware/zyj2-transaction-entry.S')],check=True)
        result={}
        for full in (0,1):
            assembly=folder/f'kernel-{full}.S';obj=folder/f'kernel-{full}.o';elf=folder/f'kernel-{full}.elf';binary=folder/f'kernel-{full}.bin'
            subprocess.run([*cc,*FLAGS,f'-DENABLE_FLASH={full}','-S',str(ROOT/'firmware/zyj2-transaction.c'),'-o',str(assembly)],check=True)
            subprocess.run(['sh-elf-as','-big','-isa=sh2','-o',str(obj),str(assembly)],check=True)
            subprocess.run(['sh-elf-ld','-T',str(ROOT/'firmware/zyj2-transaction.ld'),'-o',str(elf),str(folder/'entry.o'),str(obj)],check=True)
            assert not subprocess.check_output(['sh-elf-nm','-u',str(elf)],text=True).strip(),'Unexpected external runtime dependency'
            subprocess.run(['sh-elf-objcopy','-O','binary',str(elf),str(binary)],check=True)
            symbols={}
            for line in subprocess.check_output(['sh-elf-nm','-n',str(elf)],text=True).splitlines():
                parts=line.split()
                if len(parts)==3:symbols[parts[2]]=int(parts[0],16)
            data=binary.read_bytes();assert 0<len(data)<=8192
            result[full]=(data,symbols)
        return result

if __name__=='__main__':
    for full,(data,symbols) in build_all().items():
        print(json.dumps({'variant':'offline-flash-candidate' if full else 'read-stage-only','bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'symbols':symbols}))
