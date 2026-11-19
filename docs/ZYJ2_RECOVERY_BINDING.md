# ZYJ2 detached spare: read-only recovery boot binding

Menu **12**, or `MazdaLink.exe recovery-binding-check "path\to\original.rom"`,
checks a prerequisite for a later recovery writer: reaching the early monitor and
binding its preserved firmware to the selected backup **without normal application
identification, an RPM query, or the live calibration ID before binding**.

Use only the **detached ZYJ2EA000ZYV5050 spare** and its own complete verified
MazdaLink backup. Keep the good ECU disconnected. **Never use this probe in a
vehicle**: normal diagnostic/RPM preflight is deliberately absent. Confirm the
physical detached-spare setup with **BIND ZYJ2**. Other bench commands retain their
normal identity/engine-stopped gates; this is a separate read-only capability.

The application is still intact for this test. Omitting the application preflight
checks the entry/binding code path; it does not reproduce an interrupted flash.

## Bench sequence

1. Choose menu 12 and supply the existing complete backup's `original.rom`.
2. Confirm **BIND ZYJ2**. Disconnect only both ECU positive feeds and type
   **POWER OFF**. Keep Mongoose OBD 12 V, USB, grounds and CAN connected.
3. Keep the ECU off during the timed 60-second wait. Type **START** while it is
   still off; reconnect both ECU positive feeds at **ENTRY WINDOW** within its
   20 seconds, and leave them powered.
4. Wait for the byte comparison and final monitor reply. Progress is printed
   every 4,096 verified bytes. No normal application diagnostic connection is
   opened during entry or binding.
5. For the independent normal restart on this intact spare, follow the second
   **POWER OFF** / timed 60-second wait / **POWER ON** / 10-second startup wait.
   Only then does the app open normal diagnostics and check calibration/RPM.
6. Keep `events.jsonl` under `Documents\MazdaLink\recovery-binding-tests`.

A mismatch, cancellation, malformed reply, driver failure or deadline stops the
probe without retry. Follow the app's power-cycle/cleanup instructions before
normal diagnostics. Do not infer interrupted-write recovery from a binding pass.

## Exact read-only contract

The complete original backup must pass the existing ZYJ2/ZYC3 header, saved
identity and examined monitor/resident-loader fingerprint gates. The plan then
captures an immutable copy of **0x000000–0x0070FF**, 28,928 bytes. This region
includes the reset/monitor/resident loader, preserved identification fields and
counter data. It excludes the application calibration ID at `0x0C0091`.

The separate native backend accepts only an index into this fixed plan:

| Operation | Fixed behavior |
| --- | --- |
| Entry | Existing bounded extended-CAN monitor-entry pulse and one identity query. |
| Address selection | `7A 9C`, fixed address `0..0x70FF`; require its native acknowledgement. This selects only the monitor's volatile read pointer. |
| Byte verification | `7A B4`, exclusive end `address+1`; require `7A B1` and that byte's exact expected value. A one-byte checksum is the byte itself, including zero and FF. |
| Final identity | One fixed monitor identity request, only after every preserved byte matched. |
| Forbidden | External address/range/kernel input, upload, execute, controller/GPIO writes, erase, ROM programming, ECU reset, command retry or switching to a RAM capability. |

There are **57,857 indexed steps** after initial entry identification. Each must
complete its matching reply before the next can be sent. The native backend
permanently faults on a mismatched byte or sequence. The host also compares every
reply and hashes the observed bytes with SHA-256. It requires equality with the
backup snapshot hash before recording a match. No weak whole-region checksum
substitutes for exact byte comparison.

The host uses a monotonic three-minute binding budget, checked before each
request and receive poll, plus a two-second single-reply limit and a 64-frame
receive bound per reply. Driver call latency can add to those boundaries. This
budget starts after the entry window; operator power-cycle waits do not consume
it. Existing native monitor/checksum watchdog service remains in use.

No RAM program is uploaded and no flash-controller initialization runs in this
capability. Menus 9, 10 and 11 cannot acquire the binding capability, and the
binding wrapper cannot acquire their RAM upload/execution interfaces.

## Evidence and limits

Schema-8 local evidence distinguishes the intended profile from live application
identity: `targetProfile` comes from the selected backup. It explicitly records
that normal diagnostics were not required before entry. It records bounded stage
and transport counters, expected/observed preserved-region hashes and independent
normal-restart results. It stores no frame payloads or ROM bytes. A full match is
retained even if the subsequent monitor-return or normal-restart check fails;
those later stages cannot be marked passed by the earlier result.

This establishes a firmware-region match, **not a unique serial number or VIN**.
It rejects differing preserved bytes, including the differing ZY87/ZJ38 versus
ZYJ2/ZYC3 fields in the examined backups. An ECU with identical preserved firmware
may share the same hash. The operator must still select the correct physical
spare and its own original backup. A different original counter/boot state is a
mismatch; the probe does not silently weaken the comparison.

This new read-only path remains physically unqualified until tested on the spare.
A later writer must preserve the bound region, reject the wrong image/target,
and qualify recovery when normal application diagnostics and the calibration ID
are actually absent. The existing protected initialization experiment still
requires an intact ZYJ2 ID; it is not a recovery writer. On a damaged application,
this binding component may match while its final normal-restart diagnostic check
cannot pass. No restoration is implemented here.

Write-enable control/restoration, stock erase/write/readback, actual interrupted-
write recovery, modified-image validation and the good ECU remain separate gates.
