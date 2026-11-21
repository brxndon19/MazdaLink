/* SPDX-License-Identifier: GPL-3.0-or-later
 * Application-independent recovery preparation. NO erase/program operation.
 * Low FWE required throughout. No PD8 or pin-mux/direction write, FKEY5A,
 * operation entry at +16, ROM service call during the RAM-only hold, or TCNT/
 * reset/watchdog-mode reconfiguration. Only native TCSR A53D overflow service.
 * PB15 is already GPIO/output. Its high-byte latch is explicitly normalized
 * once from the existing pins/output latches; input latch history is unknowable.
 * Thereafter use an owned RAM shadow, preserving every other output bit and
 * leaving non-output physical pins unchanged. PB15 phase advances for service;
 * do NOT force an extra edge on exit. Normal power cycle resets input latches.
 * Callback uses only R0..R3: R8..R15, GBR and stack are untouched.
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
 mov.l .LcallbackEarly,r3
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
 mov.l .Ltarget,r1
 mov.l .LexpectedTarget,r2
 mov #4,r4
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
 mov.l .Lcounter,r1
 mov.w @r1,r0
 extu.w r0,r0
 mov #100,r2
 cmp/hs r2,r0
 bf .LcounterLow
 mov #13,r13
 bra .Lfinish
 nop
.LcounterLow:
 mov.w @(2,r1),r0
 mov.w @r1,r2
 xor r2,r0
 extu.w r0,r0
 mov.l .Lffff,r2
 xor r2,r0
 REQUIRE_ZERO 13
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
 mov.l .LwdtEarly,r1
 mov.b @r1,r0
 and #127,r0
 xor #61,r0
 REQUIRE_ZERO 14
 mov.l .Lmarker,r1
 mov.b @r1,r0
 extu.b r0,r0
 xor #119,r0
 REQUIRE_ZERO 14
 mov.l .Lpbdir,r1
 mov.b @r1,r0
 and #128,r0
 xor #128,r0
 REQUIRE_ZERO 14
 add #2,r1
 mov.b @r1,r0
 and #192,r0
 REQUIRE_ZERO 14
 mov.l .LpbdataEarly,r1
 mov.b @r1,r0
 mov r0,r2
 mov.l .LpbpinEarly,r1
 mov.b @r1,r0
 xor r2,r0
 and #128,r0
 REQUIRE_ZERO 14
 mov.l .LstateEarly,r1
 mov.b r2,@r1
 mov.l .LpbdataEarly,r1
 mov.b r2,@r1
 mov #1,r12
 bra .LafterEarlyPool
 nop
.balign 4
.Lresult: .long result
.Lfccs: .long 0xffffe800
.Lclass: .long 0x0ee4
.Ltarget: .long 0x6200
.LexpectedTarget: .long expected_target
.Lcounter: .long 0x7000
.Lffff: .long 0xffff
.Lramer: .long 0xffffec26
.Lcan1: .long 0xffffd004
.Lcan2: .long 0xffffd804
.Ltiming: .long 0xffffe320
.LtimingMask: .long 0xfffff733
.Lmux: .long 0xfffff743
.Ldirection: .long 0xfffff740
.Lpin: .long 0xfffff784
.LwdtEarly: .long 0xffffec10
.Lmarker: .long 0xffffbfad
.Lpbdir: .long 0xfffff730
.LpbdataEarly: .long 0xfffff738
.LpbpinEarly: .long 0xfffff782
.LstateEarly: .long state
.LcallbackEarly: .long ram_service
.LafterEarlyPool:
 mov.l .Lcallback,r3
 jsr @r3
 nop
 mov.l .Lstate,r1
 mov.b @(2,r1),r0
 REQUIRE_ZERO 16
 PROTECTED 2
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
 REQUIRE_ZERO 7
 mov.b @(6,r10),r0
 xor #2,r0
 REQUIRE_ZERO 7
 PROTECTED 7
 mov #125,r4
 shll2 r4
 shll2 r4
 shll2 r4
 mov.l .Lcallback,r5
 mov.l .LprogramInit,r3
 jsr @r3
 nop
 mov.b r0,@(3,r14)
 REQUIRE_ZERO 8
 PROTECTED 8
 mov.l .Lstate,r1
 mov.b @(2,r1),r0
 REQUIRE_ZERO 16
 mov r12,r0
 or #2,r0
 mov r0,r12
 mov.l .Lcallback,r3
 jsr @r3
 nop
 mov.l .Lstate,r1
 mov.b @(2,r1),r0
 REQUIRE_ZERO 16
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
 REQUIRE_ZERO 9
 mov.b @(6,r10),r0
 xor #4,r0
 REQUIRE_ZERO 9
 PROTECTED 9
 mov #125,r4
 shll2 r4
 shll2 r4
 shll2 r4
 mov.l .Lcallback,r5
 mov.l .LeraseInit,r3
 jsr @r3
 nop
 mov.b r0,@(3,r14)
 REQUIRE_ZERO 10
 PROTECTED 10
 mov.l .Lstate,r1
 mov.b @(2,r1),r0
 REQUIRE_ZERO 16
 mov r12,r0
 or #4,r0
 mov r0,r12
 /* Direct RAM-only callback rehearsal, not a silicon operation or callback
  * observed during flash busy. Fixed 64 calls; each separated by a pure loop. */
 mov.l .Lstate,r1
 mov #0,r0
 mov.b r0,@(1,r1)
 mov #64,r7
.Lhold:
 mov.l .Ldelay,r4
.LdelayLoop:
 dt r4
 bf .LdelayLoop
 bsr ram_service
 nop
 mov.l .Lstate,r1
 mov.b @(2,r1),r0
 REQUIRE_ZERO 16
 dt r7
 bf .Lhold
 mov.l .Lstate,r1
 mov.b @(1,r1),r0
 mov.b r0,@(4,r14)
 extu.b r0,r0
 mov #8,r2
 cmp/hs r2,r0
 bt .Lserviced
 mov #15,r13
 bra .Lfinish
 nop
.Lserviced:
 mov.b @(2,r1),r0
 REQUIRE_ZERO 16
 mov r12,r0
 or #8,r0
 mov r0,r12
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
 tst r12,r12
 bt .Lreport
 mov.l .Lstate,r1
 mov.b @r1,r2
 mov.l .Lpbdata,r1
 mov.b @r1,r0
 xor r2,r0
 and #128,r0
 tst r0,r0
 bf .LbadPb
 mov.l .Lpbpin,r1
 mov.b @r1,r0
 xor r2,r0
 and #128,r0
 tst r0,r0
 bf .LbadPb
 mov.l .LpbdirFinal,r1
 mov.b @r1,r0
 and #128,r0
 xor #128,r0
 tst r0,r0
 bf .LbadPb
 add #2,r1
 mov.b @r1,r0
 and #192,r0
 tst r0,r0
 bf .LbadPb
 mov r12,r0
 or #16,r0
 mov r0,r12
 bra .Lreport
 nop
.LbadPb:
 mov #16,r13
.Lreport:
 mov r12,r0
 mov.b r0,@(5,r14)
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
/* RAM leaf callback; never reads ROM or calls another helper. */
ram_service:
 mov.l .Lstate,r1
 mov.b @(2,r1),r0
 tst r0,r0
 bf .LserviceReturn
 mov.l .Lwdt,r1
 mov.b @r1,r0
 mov r0,r3
 and #127,r0
 xor #61,r0
 tst r0,r0
 bf .LserviceAbort
 mov r3,r0
 tst #128,r0
 bt .LserviceReturn
 mov.w .Lwdkey,r2
 mov.w r2,@r1
 mov.l .Lstate,r1
 mov.b @r1,r0
 xor #128,r0
 mov.b r0,@r1
 mov.l .Lpbdata,r2
 mov.b r0,@r2
 mov r0,r3
 /* Allow port output/input sampling to settle; RAM-only, fixed small bound. */
 mov #64,r0
.LpinSettle:
 dt r0
 bf .LpinSettle
 mov.b @r2,r0
 xor r3,r0
 and #128,r0
 tst r0,r0
 bf .LserviceBad
 mov.l .Lpbpin,r2
 mov.b @r2,r0
 xor r3,r0
 and #128,r0
 tst r0,r0
 bt .LserviceCount
.LserviceBad:
 mov #1,r0
 mov.b r0,@(2,r1)
.LserviceCount:
 mov.b @(1,r1),r0
 add #1,r0
 mov.b r0,@(1,r1)
.LserviceReturn:
 rts
 nop
.LserviceAbort:
 mov.l .Lstate,r1
 mov #1,r0
 mov.b r0,@(2,r1)
 rts
 nop
.balign 4
.Lprogram: .long 0xffff1000
.LprogramInit: .long 0xffff1020
.Lerase: .long 0xffff2000
.LeraseInit: .long 0xffff2020
.Lcallback: .long ram_service
.Lstate: .long state
.Lwdt: .long 0xffffec10
.Lpbdata: .long 0xfffff738
.Lpbpin: .long 0xfffff782
.LpbdirFinal: .long 0xfffff730
.Ldelay: .long 100000
.Lsend: .long 0x4c2
.Lmonitor: .long 0x924
.Ltrampoline: .long 0x40
.Lwdkey: .word 0xa53d
.balign 4
result:
.ascii "MR"
.byte 0xff,0xff,0,0
.balign 4
state:
.byte 0,0,0,0
expected_target:
.ascii "ZYC3"
.balign 4,0
