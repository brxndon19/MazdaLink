/* SPDX-License-Identifier: GPL-3.0-or-later
 * MazdaLink's own fixed SH-2 RAM test, for the fingerprinted ZYJ2 monitor only.
 * Reads two status registers; writes only its own two RAM result bytes.
 * It never writes FKEY/FCCS/FPCS/FECS/FMATS or changes MAT selection.
 * ROM 0x04c2 sends the six-byte result through the existing monitor CAN path.
 * ROM 0x0040 resets the monitor stack and jumps back to monitor 0x0924.
 */
    .section .text,"ax"
    .balign 4
    .global ram_selftest
ram_selftest:
    mov.l .Lresult,r6
    mov.l .Lfccs,r1
    mov.b @r1,r0
    mov.b r0,@(4,r6)
    add #5,r1
    mov.b @r1,r0
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
.Lresult:     .long result
.Lfccs:       .long 0xffffe800
.Lsend:       .word 0x04c2
.Lmonitor:    .word 0x0924
.Ltrampoline: .word 0x0040
    .word 0x0009
result:
    .ascii "MLR1"
    .byte 0,0
    .balign 4,0
