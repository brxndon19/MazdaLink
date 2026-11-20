/* SPDX-License-Identifier: GPL-3.0-or-later
 * Fixed detached-spare experiment. No flash-controller register is written.
 * Requires the examined cold monitor route and exact live preserved-ROM match.
 * That route writes PDDR=3EFF with PDIOR=0; monitor traffic/class/checksum and
 * watchdog helpers do not write port D. Input PDDR reads are NOT latch reads.
 * Write only PDDR's high byte (3F then 3E), preserving its known cold latches
 * and the untouched low byte. Never derive unrelated latches from input pins.
 * PDIOR must be exactly zero; only its high byte is changed (01 then 00).
 * No mux, key, selection, SCO, mapping, algorithm or erase/program operation.
 * Interrupts masked throughout; no ROM call while the output is enabled.
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
.macro CONTROLLER code
 mov.b @r10,r0
 REQUIRE_ZERO \code
 .irp offset,1,2,4,5,6
 mov.b @(\offset,r10),r0
 REQUIRE_ZERO \code
 .endr
.endm
ram_selftest:
 stc sr,r8
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
 mov.l .LsoftwareId,r1
 mov.l .LexpectedId,r2
 mov #16,r4
.LidLoop:
 mov.b @r1,r0
 mov.b @r2,r3
 cmp/eq r3,r0
 bt .LidNext
 mov #2,r13
 bra .Lfinish
 nop
.LidNext:
 add #1,r1
 add #1,r2
 dt r4
 bf .LidLoop
 /* Native counter guard: valid value/complement, value below 100.
  * Do not call the native guard's side-effecting rejection path. */
 mov.l .Lcounter,r1
 mov.w @r1,r0
 extu.w r0,r0
 mov #100,r2
 cmp/hs r2,r0
 bf .LcounterLow
 mov #3,r13
 bra .Lfinish
 nop
.LcounterLow:
 mov.w @(2,r1),r0
 mov.w @r1,r2
 xor r2,r0
 extu.w r0,r0
 mov.l .Lffff,r2
 xor r2,r0
 REQUIRE_ZERO 3
 CONTROLLER 4
 mov.l .Lramer,r1
 mov.w @r1,r0
 REQUIRE_ZERO 4
 .irp can,.Lcan1,.Lcan2
 mov.l \can,r1
 mov.w @r1,r0
 mov.l .Ltiming,r2
 mov.l .LtimingMask,r3
 and r3,r0
 xor r2,r0
 REQUIRE_ZERO 5
 add #2,r1
 mov.w @r1,r0
 and #255,r0
 xor #1,r0
 REQUIRE_ZERO 5
 .endr
 /* Require exact cold direction/mux state, not just PD8 bits. */
 mov.l .Ldirection,r11
 mov.w @r11,r0
 REQUIRE_ZERO 6
 mov.w @(2,r11),r0
 REQUIRE_ZERO 6
 mov.w @(4,r11),r0
 REQUIRE_ZERO 6
 mov.l .Lpin,r1
 mov.b @r1,r0
 and #1,r0
 REQUIRE_ZERO 6
 mov.l .Lwatchdog,r3
 jsr @r3
 nop
 CONTROLLER 4
 /* Cold latch baseline established by the reviewed reset/monitor route and
  * live fingerprint binding. Never read-modify-write input PDDR. */
 mov #1,r12
 mov #63,r0
 mov.b r0,@(6,r11)
 mov #1,r0
 mov.b r0,@r11
 /* Fixed 4096-iteration settling loop; no calls, interrupts or timer writes.
  * Live CAN timings + SH7058S ratio establish 80 MHz before this point. */
 mov.l .Lsettle,r4
.LsettleLoop:
 dt r4
 bf .LsettleLoop
 mov.b @r10,r0
 mov.b r0,@(3,r14)
 extu.b r0,r0
 xor #128,r0
 REQUIRE_ZERO 7
 mov.w @r11,r0
 mov.l .Lpd8,r2
 xor r2,r0
 REQUIRE_ZERO 7
 mov.b @(6,r11),r0
 and #1,r0
 xor #1,r0
 REQUIRE_ZERO 7
 mov.l .Lpin,r1
 mov.b @r1,r0
 and #1,r0
 xor #1,r0
 REQUIRE_ZERO 7
 mov #3,r12
 mov #0,r13
.Lfinish:
 tst r12,r12
 bt .Lreport
 /* Always lower the stored output first; check it while still an output. */
 mov #62,r0
 mov.b r0,@(6,r11)
 mov.b @(6,r11),r0
 and #1,r0
 tst r0,r0
 bf .LlatchFailed
 mov r12,r0
 or #4,r0
 mov r0,r12
.LlatchFailed:
 mov #0,r0
 mov.b r0,@r11
 mov.w @r11,r0
 tst r0,r0
 bf .LdirectionFailed
 mov r12,r0
 or #8,r0
 mov r0,r12
.LdirectionFailed:
 mov.l .Lpin,r1
 mov.b @r1,r0
 and #1,r0
 tst r0,r0
 bf .LpinFailed
 mov r12,r0
 or #16,r0
 mov r0,r12
.LpinFailed:
 mov.b @r10,r0
 mov.b r0,@(4,r14)
 tst r0,r0
 bf .LrestoreFailed
 .irp offset,1,2,4,5,6
 mov.b @(\offset,r10),r0
 tst r0,r0
 bf .LrestoreFailed
 .endr
 mov.w @(2,r11),r0
 tst r0,r0
 bf .LrestoreFailed
 mov.w @(4,r11),r0
 tst r0,r0
 bf .LrestoreFailed
 mov r12,r0
 and #28,r0
 xor #28,r0
 tst r0,r0
 bf .LrestoreFailed
 bra .Lrestored
 nop
.LrestoreFailed:
 mov #8,r13
 bra .Lreport
 nop
.Lrestored:
 /* Call existing watchdog helper only after lowering/releasing PD8. */
 mov.l .Lwatchdog,r3
 jsr @r3
 nop
.Lreport:
 mov r13,r0
 mov.b r0,@(2,r14)
 mov r12,r0
 mov.b r0,@(5,r14)
 ldc r8,sr
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
.Lcounter: .long 0x7000
.Lffff: .long 0xffff
.Lramer: .long 0xffffec26
.Lcan1: .long 0xffffd004
.Lcan2: .long 0xffffd804
.Ltiming: .long 0xffffe320
.LtimingMask: .long 0xfffff733
.Ldirection: .long 0xfffff740
.Lpin: .long 0xfffff784
.Lwatchdog: .long 0x484
.Lsettle: .long 4096
.Lpd8: .long 0x100
.Lsend: .long 0x4c2
.Lmonitor: .long 0x924
.Ltrampoline: .long 0x40
result:
.ascii "MW"
.byte 0xff,0xff,0xff,0
.balign 4,0
expected_id:
.ascii "ZYJ2EA000ZYV5050"
.balign 4,0
