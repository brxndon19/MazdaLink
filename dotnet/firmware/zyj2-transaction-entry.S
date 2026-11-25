/* SPDX-License-Identifier: GPL-3.0-or-later */
.section .text.entry,"ax"
.balign 4
.global ram_selftest
ram_selftest:
 stc sr,r8
 stc vbr,r9
 mov r8,r0
 or #240,r0
 ldc r0,sr
 mov.l .Lmain,r3
 jsr @r3
 nop
 ldc r9,vbr
 ldc r8,sr
 mov.w .Lmonitor,r4
 mov.w .Ltrampoline,r3
 jmp @r3
 nop
.balign 4
.Lmain: .long transaction_main
.Lmonitor: .word 0x924
.Ltrampoline: .word 0x40

/* RAM-only NMI: latch failure, preserve registers and let the silicon routine
 * finish. The wrapper must reject success after any such exception. */
.section .text.handlers,"ax"
.global transaction_nmi
transaction_nmi:
 mov.l r0,@-r15
 mov.l r1,@-r15
 mov.l .Lfault,r1
 mov.l @r1,r0
 or #1,r0
 mov.l r0,@r1
 mov.l @r15+,r1
 mov.l @r15+,r0
 rte
 nop
/* An unrecoverable synchronous exception cannot safely return to its faulting
 * instruction or pretend to clean up a live flash pulse. Stay in RAM, keep the
 * existing watchdog serviced and await operator power removal. Host deadline
 * records an unknown operation; there is no automatic reset or resend. */
.global transaction_trap
transaction_trap:
 mov.l .Lfault,r1
 mov #2,r0
 mov.l r0,@r1
.Lhold:
 mov.l .Lservice,r3
 jsr @r3
 nop
 bra .Lhold
 nop
.balign 4
.Lfault: .long 0xffffa0e4
.Lservice: .long transaction_service

/* Leaf callback adapted from the byte-verified alpha.12 owned routine.
 * Uses only R0..R3; stack, R8..R15 and GBR are untouched. */
.section .text.service,"ax"
.global transaction_service
transaction_service:
 mov.l .Lstate,r1
 mov.b @(3,r1),r0
 cmp/eq #1,r0
 bf .Lreturn
 mov.b @(2,r1),r0
 tst r0,r0
 bf .Lreturn
 mov.l .Lwdt,r1
 mov.b @r1,r0
 mov r0,r3
 and #127,r0
 xor #61,r0
 tst r0,r0
 bf .Labort
 mov r3,r0
 tst #128,r0
 bt .Lreturn
 mov.w .Lkey,r2
 mov.w r2,@r1
 mov.l .Lstate,r1
 mov.b @r1,r0
 xor #128,r0
 mov.b r0,@r1
 mov.l .Ldata,r2
 mov.b r0,@r2
 mov r0,r3
 mov #64,r0
.Lsettle:
 dt r0
 bf .Lsettle
 mov.b @r2,r0
 xor r3,r0
 and #128,r0
 tst r0,r0
 bf .Lbad
 mov.l .Lpin,r2
 mov.b @r2,r0
 xor r3,r0
 and #128,r0
 tst r0,r0
 bt .Lcount
.Lbad:
 mov #1,r0
 mov.b r0,@(2,r1)
.Lcount:
 mov.b @(1,r1),r0
 add #1,r0
 mov.b r0,@(1,r1)
.Lreturn:
 rts
 nop
.Labort:
 mov.l .Lstate,r1
 mov #1,r0
 mov.b r0,@(2,r1)
 rts
 nop
.balign 4
.Lstate: .long 0xffffa0e8
.Lwdt: .long 0xffffec10
.Ldata: .long 0xfffff738
.Lpin: .long 0xfffff782
.Lkey: .word 0xa53d

/* The silicon callback may clobber R0..R7, but preserves R8..R15/GBR.
 * Count only invocations during the actual chip operation, and independently
 * count observed timer services. No ROM call or controller access is added. */
.if TRANSACTION_FLASH
.section .text.flashservice,"ax"
.balign 4
.global transaction_flash_service
transaction_flash_service:
 mov.l .Lbusy,r0
 mov.l @r0,r5
 mov.l .Lservices,r0
 mov.b @r0,r4
 sts.l pr,@-r15
 mov.l .Lcommon,r3
 jsr @r3
 nop
 tst r5,r5
 bt .Lflashreturn
 mov.l .Lcounts,r0
 mov.l @r0,r1
 add #1,r1
 mov.l r1,@r0
 mov.l .Lservices,r0
 mov.b @r0,r1
 cmp/eq r4,r1
 bt .Lflashreturn
 mov.l .Lcounts,r0
 mov.l @(4,r0),r1
 add #1,r1
 mov.l r1,@(4,r0)
.Lflashreturn:
 lds.l @r15+,pr
 rts
 nop
.balign 4
.Lbusy: .long 0xffffa0e0
.Lservices: .long 0xffffa0e9
.Lcounts: .long 0xffffa110
.Lcommon: .long transaction_service
.endif
.section .note.GNU-stack,"",@progbits
