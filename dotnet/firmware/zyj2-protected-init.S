/* SPDX-License-Identifier: GPL-3.0-or-later
 * Owned SH7058S protected initialization experiment; no erase/program entry.
 * Requires low FWE, no FLER, normal MAT, no RAM emulation, known CAN timing,
 * PD8 GPIO/input/low and untouched controller configuration before writing.
 * Only FKEY=00/A5, FPCS=0/1, FECS=0/1, FTDAR=0/2/4 and FCCS.SCO=1
 * are written. Never writes GPIO, FMATS, RAMER or FKEY=5A.
 * MCU-supplied routines occupy FFFF1000..1BFF and FFFF2000..2BFF.
 * Calls only their initialization entries (+32), never operation entries (+16).
 * Persistent registers R8..R14 are preserved by the reviewed resident helpers
 * and by the documented MCU initialization ABI. Restore VBR/SR before return.
 */
.section .text,"ax"
.balign 4
.global ram_selftest
.macro REQUIRE_ZERO code
 mov #\code,r13
 tst r0,r0
 bt .Lok\@
 bra .Lfinish
 nop
.Lok\@:
.endm
.macro PROTECTED code
 mov.b @r10,r0
 REQUIRE_ZERO \code
 mov.b @(5,r10),r0
 REQUIRE_ZERO \code
 mov.b @(4,r10),r0
 REQUIRE_ZERO \code
.endm
.macro WATCHDOG
 mov.l .Lwatchdog,r3
 jsr @r3
 nop
.endm
.macro DOWNLOAD
 mov #0,r0
 ldc r0,vbr
 mov #-91,r0
 mov.b r0,@(4,r10)
 mov #1,r0
 mov.b r0,@r10
 .rept 8
 nop
 .endr
 mov #0,r0
 mov.b r0,@(4,r10)
.endm
ram_selftest:
 stc sr,r8
 stc vbr,r9
 mov r8,r0
 or #240,r0
 ldc r0,sr
 mov #0,r12
 mov.l .Lresult,r14
 mov.l .Lfccs,r10
 mov.l .Lclass,r3
 jsr @r3
 nop
 xor #1,r0
 REQUIRE_ZERO 1
 PROTECTED 2
 mov.l .LsoftwareId,r1
 mov.l .LexpectedId,r2
 mov #16,r4
.LidLoop:
 mov.b @r1,r0
 mov.b @r2,r3
 cmp/eq r3,r0
 bt .LidNext
 mov #12,r13
 bra .Lfinish
 nop
.LidNext:
 add #1,r1
 add #1,r2
 dt r4
 bf .LidLoop
 mov.l .Lramer,r1
 mov.w @r1,r0
 REQUIRE_ZERO 3
 mov.l .Lcan1,r1
 mov.w @r1,r0
 mov.l .Ltiming,r2
 mov.l .LtimingMask,r3
 and r3,r0
 xor r2,r0
 REQUIRE_ZERO 4
 add #2,r1
 mov.w @r1,r0
 and #255,r0
 xor #1,r0
 REQUIRE_ZERO 4
 mov.l .Lcan2,r1
 mov.w @r1,r0
 and r3,r0
 xor r2,r0
 REQUIRE_ZERO 4
 add #2,r1
 mov.w @r1,r0
 and #255,r0
 xor #1,r0
 REQUIRE_ZERO 4
 mov.l .Lmux,r1
 mov.b @r1,r0
 and #1,r0
 REQUIRE_ZERO 5
 mov.l .Ldirection,r1
 mov.b @r1,r0
 and #1,r0
 REQUIRE_ZERO 5
 mov.l .Lpin,r1
 mov.b @r1,r0
 and #1,r0
 REQUIRE_ZERO 5
 mov.b @(1,r10),r0
 REQUIRE_ZERO 6
 mov.b @(2,r10),r0
 REQUIRE_ZERO 6
 mov.b @(6,r10),r0
 REQUIRE_ZERO 6
 WATCHDOG
 PROTECTED 2
 mov #1,r12
 mov #1,r0
 mov.b r0,@(1,r10)
 mov #0,r0
 mov.b r0,@(2,r10)
 mov #2,r0
 mov.b r0,@(6,r10)
 mov.l .Lprogram,r11
 mov #-1,r0
 mov.b r0,@r11
 DOWNLOAD
 mov.b @r11,r0
 mov.b r0,@(3,r14)
 REQUIRE_ZERO 7
 mov.b @(6,r10),r0
 xor #2,r0
 REQUIRE_ZERO 7
 PROTECTED 7
 mov #125,r4
 shll2 r4
 shll2 r4
 shll2 r4
 mov #0,r5
 mov.l .LprogramInit,r3
 jsr @r3
 nop
 mov.b r0,@(5,r14)
 REQUIRE_ZERO 8
 PROTECTED 8
 WATCHDOG
 PROTECTED 9
 mov #0,r0
 mov.b r0,@(1,r10)
 mov #1,r0
 mov.b r0,@(2,r10)
 mov #4,r0
 mov.b r0,@(6,r10)
 mov.l .Lerase,r11
 mov #-1,r0
 mov.b r0,@r11
 DOWNLOAD
 mov.b @r11,r0
 mov.b r0,@(4,r14)
 REQUIRE_ZERO 9
 mov.b @(6,r10),r0
 xor #4,r0
 REQUIRE_ZERO 9
 PROTECTED 9
 mov #125,r4
 shll2 r4
 shll2 r4
 shll2 r4
 mov #0,r5
 mov.l .LeraseInit,r3
 jsr @r3
 nop
 mov.b r0,@(5,r14)
 REQUIRE_ZERO 10
 PROTECTED 10
 WATCHDOG
 PROTECTED 10
 mov #0,r13
.Lfinish:
 tst r12,r12
 bt .Lrestore
 mov #0,r0
 mov.b r0,@(4,r10)
 mov.b r0,@(1,r10)
 mov.b r0,@(2,r10)
 mov.b r0,@(6,r10)
 mov.b @r10,r2
 mov.b @(1,r10),r0
 or r0,r2
 mov.b @(2,r10),r0
 or r0,r2
 mov.b @(4,r10),r0
 or r0,r2
 mov.b @(5,r10),r0
 or r0,r2
 mov.b @(6,r10),r0
 or r0,r2
 tst r2,r2
 bt .Lrestore
 mov #11,r13
.Lrestore:
 ldc r9,vbr
 ldc r8,sr
 mov r13,r0
 mov.b r0,@(2,r14)
 mov r14,r6
 mov #-64,r4
 mov #6,r5
 mov.l .Lsend,r3
 jsr @r3
 nop
 mov.l .Lmonitor,r4
 mov.l .Ltrampoline,r3
 jmp @r3
 nop
.balign 4
.Lresult: .long result
.Lfccs: .long 0xffffe800
.Lclass: .long 0x0ee4
.LsoftwareId: .long 0x000c0091
.LexpectedId: .long expected_id
.Lramer: .long 0xffffec26
.Lcan1: .long 0xffffd004
.Lcan2: .long 0xffffd804
.Ltiming: .long 0xffffe320
.LtimingMask: .long 0xfffff733
.Lmux: .long 0xfffff743
.Ldirection: .long 0xfffff740
.Lpin: .long 0xfffff784
.Lwatchdog: .long 0x0484
.Lprogram: .long 0xffff1000
.LprogramInit: .long 0xffff1020
.Lerase: .long 0xffff2000
.LeraseInit: .long 0xffff2020
.Lsend: .long 0x04c2
.Lmonitor: .long 0x0924
.Ltrampoline: .long 0x0040
result:
.ascii "MI"
.byte 0xff,0xff,0xff,0xff
.balign 4,0
expected_id:
.ascii "ZYJ2EA000ZYV5050"
.balign 4,0
