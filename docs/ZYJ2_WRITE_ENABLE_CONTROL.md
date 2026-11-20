# ZYJ2 detached spare: fixed write-enable control experiment

Menu **13**, or `MazdaLink.exe write-enable-check "path\to\original.rom"`,
tests one candidate PD8/FWE transition and its restoration. It is experimental,
limited to the intact, physically detached ZYJ2EA000ZYV5050 / ZYC3 spare and its
own complete verified backup. Keep the good ECU disconnected. Never use it in a
vehicle or treat it as a recovery writer.

## Sequence and limits

1. Select menu 13, supply the existing backup's `original.rom`, and type
   **ENABLE ZYJ2**. Normal calibration/RPM/calibration preflight must pass.
2. Follow **POWER OFF**, the 60-second ECU-only off wait, **START**, and reconnect
   both ECU positive feeds at **ENTRY WINDOW** within its 20 seconds. Keep
   Mongoose USB/OBD 12 V, grounds and CAN connected throughout.
3. The app freshly compares all 28,928 preserved boot bytes against this backup.
   Any mismatch stops before upload. The previous menu-12 result is not reused.
4. It uploads only the built-in 752-byte RAM program at `0xFFFF8000`, verifies
   every byte, and permits one execution. External code, addresses and retries
   are unavailable. Upload and verification precede any GPIO change.
5. The program checks the intact calibration ID, SH7058S class, valid native
   counter/complement below 100, low FWE/no FLER, normal mapping, zero key/selection
   and RAM-emulation state, both live CAN timings, zero port-D direction and mux
   registers, and PD8 input low. A failed gate makes no port-D write.
6. With interrupts masked, it writes PDDR's high byte `3F`, then PDIOR's high
   byte `01`. After one fixed 4,096-iteration settling loop it checks FCCS=`80`,
   PD8 output direction, its output-latch bit and its actual pin level. There are
   no calls or timer writes while the output is enabled.
7. On every ordinary exit after the first GPIO write, it writes PDDR high=`3E`
   before releasing PDIOR high=`00`. It checks the low output latch while the pin
   is still an output, then direction, input pin, mux and controller protection.
   An uncertain result stops host traffic before another monitor request.
8. A passing result is followed by monitor identification and the independent
   **POWER OFF** / 60-second wait / **POWER ON** / 10-second startup / normal
   diagnostic restart. Save `events.jsonl` under
   `Documents\MazdaLink\write-enable-control-tests`.

The settling duration is instruction-count bounded, not a measured electrical
pulse width. The SH7058S CPU/peripheral clock ratio and both masked CAN BCR1/BCR0
values establish the fixed clock assumption, as in the protected initialization
experiment. The existing watchdog helper runs before the pulse and after a
confirmed restoration; no watchdog is disabled. Power loss, a processor fault,
driver failure or broken hardware can prevent restoration. Remove both ECU
positive feeds on an incomplete/uncertain result; never retry a pulse automatically.

## Review of stored latch ownership

Primary source: Renesas SH7058S/SH7059 hardware manual **REJ09B0177-0500**, sections
5, 17.4.4, 22.3.8, 23.5.2/table 23.8, 23.5.3 and 24.6.1.

An input-configured PDDR read returns the **pin**, not the stored output latch.
PDPR is a read-only pin register. Neither provides an input latch snapshot, so
blind whole-word PDDR read-modify-write cannot promise restoration. This program
does not use that method, call the resident enable/disable routines, change pin
mux, or drive unrelated pins temporarily to inspect their latches.

The accepted boot fingerprints cover the reset vectors, initializer and monitor:

| Reviewed path | Port-D implications |
| --- | --- |
| Reset vectors at `0`/`8`, entry `0xC0C`, calls `0xC20` then `0xC54` | The port initializer runs before the entry detector. Its PD writes set PDIOR=`0000`, PDDR=`3EFF`, PDCRH/PDCRL=`0000`, then PDIOR=`0000`. |
| `0x65C` → entry detector `0x7E0`, pattern checker `0x89E` → trampoline `0x40` → monitor `0x924` | The successful early-entry branch reaches the monitor before the normal application. CAN setup/receive, timer and watchdog helpers on this route do not write port D. |
| Fixed monitor traffic: pointer handler `0xA34`, upload handler `0xA7A`/copy helper, one-byte checksum handler `0xC2`/checksum `0x152`, identification `0xF8` | This script confines upload to its own RAM image; other commands read ROM or update monitor RAM/CAN state. No port-D write occurs before the owned program. |
| Watchdog `0x484`, transmit `0x4C2`, resident class `0xEE4` | Watchdog service may toggle **PB15**, not port D. CAN output does not write port D. The hardware-class helper temporarily accesses H-UDI and restores SYSCR2. |
| Resident programming candidate `0x14AE`/`0x14DA` | It sets/clears PD8 around programming-driver work, supporting PD8 as the board candidate. It is not called: its input-data RMW and counter guard's side-effecting rejection path are unsuitable here. |

Thus the selected early cold route owns a known stored latch baseline. Only the
high data byte is written (`3E` → `3F` → `3E`); PD9–PD13 keep their known stored
ones and the low byte is untouched. With the full direction register checked as
zero, `00` → `01` → `00` in its high byte changes only PD8's direction. Mux is
never written. Unrelated pin levels are not substituted for stored latches.

This argument depends on genuine cold entry through the fingerprinted route.
Normal application preflight prevents intentionally continuing an old monitor
session; the operator must still perform the full ECU power cycle. The host's
timer and OBD supply snapshot cannot measure internal ECU voltage or prove reset.
The fixed program is not offered on the no-application recovery-binding path.

## Evidence and qualification

Schema-9 local evidence retains the fresh binding result, immutable image hash,
control result, transport counters, monitor return and normal restart separately.
It contains no raw ROM/frame payload. Successful status is **0**, during FCCS
**80**, after FCCS **00**, and observations **1F**:

| Bit | Observation |
| --- | --- |
| 0 | GPIO change started. |
| 1 | Enabled output direction, stored PD8 high and actual PD8 high checks passed. |
| 2 | Restored stored PD8 low checked while still an output. |
| 3 | Full input direction restored. |
| 4 | Released input pin low. |

Status 7 means the enabled observation failed. Status 8 means restoration could
not be confirmed; preserve the result and remove ECU power. A result is retained
even if later monitor return or normal restart fails. Completion requires all
stages, not just an FWE observation.

The independent GNU rebuild/instruction model checks success and failed guards,
unrelated input-level variation, missing FWE wiring, stuck latch/direction/FWE,
fixed write ordering, interrupt restoration and permitted calls. It is a software
model, not electrical or hardware qualification. The new experiment remains
physically unqualified until separately demonstrated on the spare.

No flash-controller register is written; no key, algorithm download, erase/program
entry, ROM write or ECU reset command is present. A pass establishes only a
write-enable control observation on this board. Stock erase/write/readback,
recovery with an actually unavailable application, interrupted-write recovery,
modified-image integrity and the good ECU remain separate development gates.
