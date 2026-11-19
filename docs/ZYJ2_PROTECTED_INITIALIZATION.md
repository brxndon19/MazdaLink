# ZYJ2 spare: protected flash-algorithm initialization

This separate experiment loads the SH7058S chip's own algorithms into RAM and
calls only their initialization entries. It does not erase/program ROM, enable
FWE, change PD8, upload external code or test interrupted-write recovery. Use the
detached ZYJ2EA000ZYV5050 spare and keep the good ECU disconnected.

Menu **11**, or `MazdaLink.exe protected-flash-init "path\to\original.rom"`,
requires a complete verified MazdaLink backup with the examined monitor/loader
fingerprints, normal matching live identity, stopped engine and **INIT ZYJ2**
confirmation. It uses its own backend capability. Menus 9 and 10 cannot select
this controller-writing program.

Keep Mongoose USB, OBD 12 V, grounds and CAN connected throughout. Switch only
both ECU positive feeds. Follow the established prompts: **POWER OFF**, timed
60-second wait, **START**, reconnect during the 20-second **ENTRY WINDOW**. After
the experiment, follow the second **POWER OFF** / 60-second wait / **POWER ON** /
10-second startup sequence for an independent normal diagnostic check.

Save `events.jsonl` from `Documents\MazdaLink\protected-initialization-tests`.
Any failure stops progression without retry. A result marker, monitor return and
normal restart are separate observations; none qualifies an actual flash write.
A timeout does not imply that controller initialization completed. Follow the
application's power-cycle/cleanup instructions before reconnecting normally.

## Fixed program and preconditions

The owned program is **936 bytes** at `0xFFFF8000`; source:
[`zyj2-protected-init.S`](../dotnet/firmware/zyj2-protected-init.S).
SHA-256: `85ccb46064aa483d64f53b5666017b57b2624ea015fcae0299ef5f336587925b`.
All 936 bytes must be read back exactly before one execution. No image/address
argument is accepted. Its 2,032 immutable transport steps have a 60-second host
deadline and a two-second bound per expected reply. Receive polling permits a
fixed maximum of 256 frames per reply to drain the 156 unacknowledged upload
packets' adapter echoes; it never clears queued ECU upload errors.

Before controller writes, the program requires:

- Resident hardware class 1 (SH7058S), plus an internal read of the complete
  `ZYJ2EA000ZYV5050` software ID after cold entry. A different ECU cannot pass
  merely by sharing the monitor reply.
- FCCS zero: FWE low, no FLER and no pending SCO; normal user MAT and FKEY zero.
- RAMER zero, and FPCS, FECS and FTDAR in their initial zero state.
- Both HCAN controllers' implemented BCR1 fields=`0xE320` (mask `0xF733`)
  and BCR0 divisor=`0x01` (mask `0x00FF`). Reserved read bits are ignored.
- PD8 selected as GPIO, input direction and observed pin low.

At the established 500 kbit/s link, those timing registers specify 20 quanta and
a divisor of two, establishing a 20 MHz peripheral clock. The SH7058S documented
CPU/peripheral ratio is four, so initialization uses FPEFEQ **8000** (80 MHz times
100). It does not assume the clock merely from a silicon maximum or crystal label.

The PD8 data latch is not read or changed. PDDR reads the pin level when the pin
is an input, so the earlier snapshot is not a recoverable original latch value.
This experiment does not infer a board-level PD8/FWE connection.

## Controller and RAM contract

| Item | Fixed behavior |
| --- | --- |
| Programming algorithm | FPCS=1, FECS=0, FTDAR=2; download to `0xFFFF1000–0xFFFF1BFF`. |
| Erasing algorithm | FPCS=0, FECS=1, FTDAR=4; download to `0xFFFF2000–0xFFFF2BFF`. |
| Download | Seed DPFR with FF; FKEY=A5, VBR=0, SCO=1 and eight immediate NOPs; then FKEY=0. Require DPFR=0 and exact FTDAR readback. |
| Initialization | Call only `0xFFFF1020` and `0xFFFF2020` (base+32), with R4=8000 and R5=0 (no user branch). Require each full 32-bit return to be zero. |
| Forbidden | FKEY=5A, calls to base+16, GPIO writes, FMATS writes, RAMER writes, erase/program operations or ECU reset. |
| Stack | Existing monitor stack grows down from `0xFFFFBFA0`; reserve the documented maximum 128-byte initialization stack below the caller. Algorithms, owned code/result and monitor FIFO do not overlap it. |
| Cleanup | Clear FKEY, FPCS, FECS and FTDAR to their checked original zero values; require controller/mapping/key/selection checks to pass; restore saved VBR and SR. |

The source masks ordinary interrupts while the experiment runs and preserves
other SR bits. The chip preserves the general registers except R0 at its
initialization entries. The existing fingerprinted watchdog helper is serviced
between phases, when normal MAT/protection has been checked. Its existing
watchdog output can be serviced; PD8 is not touched. No ROM helper is called
while SCO is executing or from within a chip initialization call. Watchdogs are
not disabled and no erase/program busy callback is implemented.

The SH7058S manual explicitly permits algorithm download and initialization
with FWE low. Actual erase/program remains hardware-protected. The owned code
also omits the programming key and operation entries. Protection is checked at
phase boundaries; this is not a continuous voltage/pin measurement. If any
protection or cleanup check fails, status cannot become success.

## Evidence and limitations

The distinct six-byte result is `MI`, status, programming DPFR, erasing DPFR and
the **low byte** of the last initialization return. The low byte is diagnostic;
the MCU program tests the full return before deciding status. Status zero requires
both complete downloads, both full-width zero initialization returns and cleanup
checks. Earlier failures leave unreached result fields FF. Status 11 means
cleanup/protection could not be confirmed and takes precedence over an earlier
failure. Schema-7 local evidence retains this bounded result and transport stages,
without frames, algorithm payloads or security keys. The decoded result is
persisted at its marker stage even if the following monitor-return check fails;
that result alone cannot complete the experiment.

CI rebuilds the owned image with GNU SuperH binutils and runs an independent
restricted SH-2 model across 56 success/fault cases, including high-bit-only
initialization failures and protection/cleanup faults. That model abstracts the
chip routine bodies at their documented ABI; it does not prove their physical
behavior. Transport/native tests separately enforce capability isolation, every
byte's readback, echo handling, cancellation, marker/monitor replies and no retry.

Reproduce the owned-code checks without hardware:

```sh
python3 dotnet/tools/Verify-ProtectedInitialization.py
```

The complete experiment remains physically unqualified until tested on the spare.
Even a pass does not qualify write-enable control, stock restore, block readback,
interrupted-write recovery, modified-ROM checksums or the good ECU. Those remain
separate gates before vehicle tuning.

Reference: Renesas [SH7058S/SH7059 hardware manual, revision 5](https://www.renesas.com/en/document/mah/sh-2e-sh7059-f-ztattm-sh7058s-f-ztattm-hardware-manual),
sections 5, 17.4.4, 24.4.3, 24.5.2, 24.6.1 and 24.8. The shared loader and monitor
fingerprints bind the reviewed resident helper ABI to the accepted backup.
