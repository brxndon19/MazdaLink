# ZYJ2 recovery preparation and unchanged-backup restore plan

Menu **14** / `recovery-rehearsal <original.rom>` is a separate experimental
path for the physically detached ZYJ2 spare. Menu **15** /
`original-restore-plan <original.rom>` prints an offline recipe and opens no
hardware. Neither implements erase or ROM programming.

The alpha.11 spare log passed PD8/FWE transition, restoration, monitor return and
normal restart. That establishes one board observation; it does not qualify
flash operations, other boards or interrupted-write recovery.

## Why this step precedes an erase

Renesas **REJ09B0177-0500**, sections 13.2, 24.4.3, 24.5.2 and 24.8, requires
flash-busy execution, constants, vectors and callbacks to avoid both flash MATs.
The resident watchdog helper is ROM code and cannot be called during an erase.
The on-chip algorithm's user branch must preserve R8–R15 and GBR, return with
RTS, avoid changing flash-control registers and never recursively invoke an
algorithm. Programming/erasing callbacks may be separated by approximately 1/5 ms
at 80 MHz. The new RAM leaf routine prepares that service contract.

This rehearsal **directly invokes** the RAM callback. It registers that address
with both downloaded algorithms, but calls only their initialization entries.
It does not observe a silicon-generated callback during actual flash busy.

## Bench procedure

1. Keep the good ECU disconnected. Use only the intact detached ZYJ2 spare,
   its own verified original backup, and the existing bench harness.
2. Select **14**, supply `original.rom`, and confirm **REHEARSE ZYJ2**. Normal
   application calibration/RPM diagnostics are deliberately absent before entry.
   Never use this command in a vehicle.
3. Follow **POWER OFF**, the 60-second ECU-only off wait, **START**, and reconnect
   both ECU positive feeds at **ENTRY WINDOW** within its 20 seconds. Leave
   Mongoose USB/OBD 12 V, grounds and CAN connected.
4. The host freshly matches all 28,928 preserved boot bytes. It then uploads only
   the immutable **1,404-byte** program at `0xFFFF8000` and reads back every byte
   before permitting one execution. No external code/address or retry is accepted.
5. The program initializes both on-chip algorithms at the existing fixed RAM
   locations with a nonzero fixed RAM callback address, then directly calls that
   callback 64 times with fixed instruction-count delays. A successful result
   requires at least eight observed/serviceable timer overflows.
6. After controller cleanup and monitor return, follow the independent
   **POWER OFF** / 60-second wait / **POWER ON** / 10-second startup / normal
   diagnostic restart. Save `events.jsonl` from
   `Documents\MazdaLink\recovery-rehearsals`.

The intact application is retained for this test. A missing final application
diagnostic cannot negate a prior rehearsal result, but cannot be recorded as a
normal restart. This is not recovery with an actually erased application.

## Fixed controller and RAM contract

Host entry/binding never reads the live `0xC0091` software ID. The owned program
also has no application-ID dependency: it checks SH7058S class, the preserved
`ZYC3` marker, the native counter/complement below 100, low FWE/no FLER, normal
MAT, zero RAM emulation/key/selection, known live timing on both CAN controllers,
PD8 GPIO/input/low, and the existing watchdog/output configuration.

Both MCU algorithms occupy `0xFFFF1000..1BFF` and `0xFFFF2000..2BFF` as before.
Only their **base+32 initialization entries** are called, with R4=8000 and R5
pointing to the owned RAM leaf callback. Download uses the reviewed A5/SCO/eight-
NOP sequence. Full-width initialization results are checked before retaining a
diagnostic low byte. Controller key/selection, VBR and SR restoration are checked
independently of normal restart. There is **no FKEY=5A or base+16 operation call**.
No PD8, mux, direction, RAM-emulation or flash-mapping register is written.

The kernel and literal pools end below `0xFFFF9000`; its result/state live inside
that image. A future 128-byte staging buffer at `0xFFFF9000` would not overlap
them or the downloaded algorithms. The existing `0xFFFFBFA0` stack leaves the
manual's required 128-byte algorithm stack area below it, separate from the
monitor FIFO. This map is preparation, not an enabled streaming programmer.

## Existing timer and PB15 service

The examined class-1 boot uses TCSR control `3D`: enabled interval mode, system
clock/1024. At 80 MHz its overflow interval is approximately **3.3 ms**. The
callback checks that mode on every invocation. It writes only keyed word
`A53D` to clear an observed overflow; it never changes mode, disables the timer,
writes TCNT, or changes reset control. The retained boot marker must be `77` and
PB15 must already be GPIO/output. No pin direction or mux is changed.

Input-configured port-data reads return pins, not stored latches. Before service,
the program explicitly normalizes the **port-B high-byte non-output latches** to
the currently observed pin values once, while preserving existing output bits.
The previous input-latch values are unknowable and are **not claimed restored**.
It then owns a RAM shadow and toggles only PB15 in that shadow. Repeated pin
read-modify-write is not used. Other physical pins and configured output bits
remain unchanged; the low data byte is untouched. Complete the final power cycle
so the ordinary cold initializer reestablishes its latch state.

Each callback checks PB15's stored output bit and actual pin. A failed mode/pin
check latches an error and prevents later callback output writes. Main execution
stops at the next check. PB15's watchdog phase is allowed to advance; no extra
edge is forced merely to restore its starting phase. On monitor return the native
watchdog service resumes. The callback uses only R0–R3, touches no stack or GBR,
calls no ROM helper, and fetches all owned instructions/constants from RAM.

The 64 delays are bounded pure DT/BF loops. Their instruction count is fixed;
they are not a measured electrical timing guarantee or a simulation of flash
busy on the hardware. CPU frequency remains guarded by class and live CAN
timing, following the earlier initialization experiment.

## Result and evidence

Schema-10 evidence records binding, immutable image hash, result, counters,
monitor return and normal restart separately. No raw ROM/frame payload is logged.
Pass requires status **0**, initialization low byte **00**, service-overflow
count **8–64**, and observations **1F**:

| Bit | Meaning |
| --- | --- |
| 0 | Explicit high-byte latch ownership/normalization started. |
| 1 | Programming algorithm download/initialization passed. |
| 2 | Erasing algorithm download/initialization passed. |
| 3 | The direct RAM-only service count/pin checks passed. |
| 4 | Final PB15 output/shadow/pin/configuration checks passed. |

Controller cleanup also must pass for status zero. Any unsuccessful or
contradictory result is retained and stops before another monitor request.
Remove ECU positive power as instructed; do not automatically retry.

The independent GNU rebuild/instruction model has no application memory mapped.
It checks callback register/stack preservation, prohibited flash reads/calls,
timer keys, output shadow behavior, failed guards, stuck pin, timer-mode change,
initialization failures and cleanup. A pure delay-loop summary is checked against
the exact DT/BF instructions. MCU algorithm bodies are abstracted at their
documented ABI. These are software checks, not hardware flash qualification.

## Offline restore recipe and remaining implementation

Menu 15 accepts only the selected complete verified original backup. Its plan
factory rejects any intended bytes differing from that original; it does not
authenticate an OEM-stock image or qualify modified-image checksums.

The fixed candidate scope is **EB14: `0xC0000..0xDFFFF`, 128 KiB**, in **1,024
aligned 128-byte units**. Every other block, including the early monitor, counter
and EB15 startup/checksum data, must be preserved. The plan requires exact
preserved-region checks before erase, complete erased-block verification,
per-unit exact readback and complete intended-image verification. A single
readback checker is not a replacement for obtaining matching full-image reads.

The test-only flash model exercises partial/completed erase interruption and
partial/completed programming at every unit boundary. An ambiguous command is
never resent: recovery starts with fresh binding, then **re-erases the complete
selected block** and writes each unit once in the new erase generation. A missing
application ID does not prevent the model's preserved-region binding. A preserved
region mismatch or readback failure blocks further operations.

The app has no native erase/program/readback transaction backend. Before the first
destructive bench write, implement and review RAM-native staging/readback, fixed
address/index/payload binding, durable host intent and failure records, flash-busy
vector/interrupt behavior, FWE stability throughout each operation, full-width
operation status handling and application-independent preserved-region reads.
Actual erased-application entry and interrupted-operation recovery remain
unqualified. Modified ROMs and the good ECU remain outside this experiment.
