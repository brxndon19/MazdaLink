/* SPDX-License-Identifier: GPL-3.0-or-later
 * MazdaLink fixed SH-2 test. Reads programming-output preconditions only.
 * Resident class query restores SYSCR2. No GPIO or flash-control write.
 */
.section .text,"ax"
.balign 4
.global ram_selftest
ram_selftest:
 mov.w .Lchip,r3
 jsr @r3
 nop
 mov.l .Lresult,r6
 mov.b r0,@(2,r6)
 mov.l .Lfccs,r1
 mov.b @r1,r0
 mov.b r0,@(3,r6)
 add #5,r1
 mov.b @r1,r0
 mov.b r0,@(4,r6)
 mov #0,r2
 mov.w .Lmux,r1
 mov.b @r1,r0
 and #1,r0
 or r0,r2
 mov.w .Ldir,r1
 mov.b @r1,r0
 and #1,r0
 shll r0
 or r0,r2
 mov.w .Ldata,r1
 mov.b @r1,r0
 and #1,r0
 shll2 r0
 or r0,r2
 mov.w .Lpin,r1
 mov.b @r1,r0
 and #1,r0
 shll2 r0
 shll r0
 or r0,r2
 mov r2,r0
 mov.b r0,@(5,r6)
 mov #-64,r4
 mov #6,r5
 mov.w .Lsend,r3
 jsr @r3
 nop
 mov.w .Lmonitor,r4
 mov.w .Ltrampoline,r3
 jmp @r3
 nop
.balign 4
.Lresult: .long result
.Lfccs: .long 0xffffe800
.Lsend: .word 0x04c2
.Lmonitor: .word 0x0924
.Ltrampoline: .word 0x0040
.Lchip: .word 0x0ee4
.Lmux: .word 0xf743
.Ldir: .word 0xf740
.Ldata: .word 0xf746
.Lpin: .word 0xf784
result:
.ascii "MP"
.byte 0,0,0,0
.balign 4,0
