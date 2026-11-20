# MazdaLink requirements

## Product scope

- The current product is the independent terminal application in `dotnet/src/MazdaLink*`.
  Ship only `MazdaLink.exe`, never the legacy desktop or reader binaries.
- Raw reads and exports have no calibration whitelist. Require a valid reported
  calibration, recheck it before unlock, and retain its actual value in backup metadata.
  Do not impose ZY87 header/checksum-layout assumptions on raw captures.
- Checksum preparation/validation remains limited to independently established policies:
  currently only `ZY87EC000ZYV5050`. Never relabel an unknown backup or apply a known
  correction policy merely because read procedures or software suffixes look similar.
- Keep the fixed bounded raw-read protocol, engine-stopped gate and matching dual reads.
  No automatic protocol probing, key retry or write/reset override is permitted.
- The separately selected bench monitor check is limited to ZYJ2EA000ZYV5050,
  a complete verified backup with the examined boot-code fingerprints, and a
  detached spare confirmed by the operator. Its raw extended-CAN channel may send
  only the fixed bounded entry pulse and monitor-identification request. It must
  stop periodic traffic on every exit and check normal restart independently.
  No memory address, upload, execute, erase, programming or ECU reset command may
  be added to this identification path. A monitor response is not flash-recovery
  qualification; hardware acceptance is still unqualified.
- Measure the bench-entry deadline with monotonic elapsed time. No disk/UI or
  supply/configuration diagnostic call may run while entry traffic is active;
  only bounded receive polling is permitted there. Bench evidence must contain
  bounded metadata/counters, never raw frame payloads. TX echoes do not prove
  ECU acceptance, and adapter supply snapshots do not prove ECU power cycling.
- MazdaLink has no enabled flash/recovery implementation. A flash command must refuse
  before driver open; do not infer a Mazda 2 writer from generic UDS or Gen 1 code.
- The separately selected ZYJ2 fixed RAM self-test has its own backend capability
  and `RAM ZYJ2` confirmation. Permit only the immutable built-in 120-byte program
  at 0xFFFF8000, its bounded upload and exact per-byte readback script, one execution,
  then monitor identification and normal restart. Never accept external code,
  addresses, repeated execution, skipped replies or a weak whole-image checksum
  in place of exact byte verification. The program uses the fingerprinted resident
  hardware-class query (which temporarily enables H-UDI status access and restores
  SYSCR2), reads FCCS/FMATS and PD8 mode/direction/data/pin bits and writes its own RAM result and the existing CAN path.
  Do not infer a single SH7058/SH7058S/SH7059 erase map from the shared boot code.
  No flash-controller or FWE-setting write,
  on-chip algorithm download, erase or ROM programming is included. Physical RAM
  execution acceptance remains unqualified until separately demonstrated.
  The 64-byte alpha.7 class/status probe and normal restart passed on one spare;
  the 120-byte read-only PD8 snapshot subsequently passed execution, monitor return
  and normal restart on the same spare, reporting GPIO/input/low.
  Do not change GPIO configuration or infer its board-level FWE connection from
  a snapshot. Require a separately reviewed control-and-restore test before any
  hardware write-enable change.
- The separate protected initialization capability requires `INIT ZYJ2`, exact
  target/backup gates and only its immutable 936-byte owned program. It may copy
  the SH7058S chip's on-chip programming/erasing algorithms into the two fixed
  non-overlapping RAM ranges and call only base+32 initialization entries after
  exact per-byte readback. Require class1, an internal ZYJ2 software-ID check after cold entry, low FWE/no FLER, normal MAT, zero
  RAMER/key/selections, matching live timing on both CAN controllers, and PD8
  GPIO/input/low before controller changes. No PD8/configuration or mapping write,
  FKEY5A, operation entry at base+16, external code/address, erase or ROM programming
  is permitted. Restore controller selection/key values and VBR/SR; record results,
  monitor return and normal restart separately. Native watchdog service between
  phases does not authorize disabling watchdogs or calling ROM during flash busy.
  Keep this capability inaccessible to the identification and read-only RAM paths.
  One alpha.9 spare log passed protected algorithm download/initialization,
  protection/cleanup checks, monitor return and normal restart. This does not
  qualify a writer, broader hardware compatibility or interrupted-write recovery. Preserve its independent assembly/model
  CI check, transport integrity gates and release metadata hash/scope binding.
- The separate read-only recovery binding capability requires `BIND ZYJ2` on a
  physically detached spare and its verified backup. It intentionally performs no
  normal application identity/RPM preflight before entry. Do not weaken the other
  workflows' preflight gates or expose an automatic fallback. Read only the fixed
  preserved ROM range0..70FF, comparing every byte and hashing observed replies;
  allow only pointer selection, one-byte checksum reads and final monitor identity.
  Never allow uploads, execution, controller/GPIO writes, external ranges, command
  retry or conversion to a RAM capability. Its matched firmware region is not a
  unique ECU identity. Record binding, final monitor response and post-cycle normal
  diagnostics independently; a match does not qualify interrupted-write recovery.
  Actual recovery with an erased calibration ID and write-enable control remain
  separate preconditions before any destructive stock write. Preserve the fixed
  three-minute budget, per-reply bounds, capability tests and release scope binding.
- The separate fixed write-enable control experiment requires `ENABLE ZYJ2`,
  intact normal identity/RPM preflight, cold entry and a fresh exact live binding
  before its immutable 752-byte program is uploaded and byte-verified once.
  Existing read-only and initialization capabilities cannot acquire it. Check the
  hardware class, intact ID, counter/complement, controller protection, timing and
  full port-D cold mux/direction before any GPIO write. The examined cold route
  owns PDDR=3EFF; never substitute input pin reads for stored latch values.
  Permit only PDDR high 3F/3E and PDIOR high 01/00, with interrupts masked, no ROM
  call while enabled, fixed settling count, and checked restoration. Never write
  mux or flash-control registers, download algorithms, call erase/program entries
  or accept external addresses/code. Uncertain restoration retains its result and
  stops before further monitor traffic; remove ECU positive power, no retry.
  Preserve the independent assembly/fault model and release image/scope binding.
  A passing GPIO/FWE pulse is not a writer or interrupted-write recovery test.
  The alpha.10 read-only binding, monitor return and normal restart passed on one
  spare; that does not prove reset electrically or qualify this GPIO experiment.
- Use the observed longer-off bench sequence: normal preflight, operator-confirmed
  ECU-only power removal, a monotonic 60-second host wait outside entry traffic,
  then open the adapter with ECU off and reconnect during the bounded entry window.
  The timer and adapter voltage do not measure internal ECU power or prove a
  minimum cold-start delay. Preserve the identification-only backend's two-payload gate.
- Normal restart uses a separately confirmed ECU-only 60-second off wait, POWER ON
  confirmation and 10-second startup wait before one normal diagnostic check.
  No entry traffic or RAM execution runs during this restart sequence. A timeout
  does not negate earlier RAM evidence or qualify recovery; record the stages separately.
- Preserve original GPL credits. Legacy sources/definitions and their core integrity
  regression matrix remain archived in place, but are not application dependencies.

## Reliability and releases

- Publishing an application fix includes a matching standalone EXE release unless
  source-only delivery is explicitly requested. Complete the version-tag workflow
  and verify the public download before reporting publication complete.
- Keep crash reports local and bounded; never include memory dumps or ROM payloads.
  Fatal UI errors must stop the app rather than resume in an unknown state.
- Release the same standalone executable that passed the release checks. Bind it
  to its exact source commit, version and hashes; never replace a published asset.
- Preserve mandatory integrity gates and retained synthetic CI failure evidence.
  Do not upload generated ROM fixtures or private/customer files as CI artifacts.
- Keep dependency/runtime updates reviewable. Maintain exact SDK/runtime pins,
  refreshed locks and protected checks; no automatic merge or publication.

## Mandatory ROM-save integrity

- Every ROM exported from the editor must have a valid checksum before its final filename is
  published. Automatically recalculate and write every independently established
  correction word on every save, including saves without table edits.
- There must be no user option, imported-definition setting or alternate export
  path that bypasses checksum correction and verification. ROM exports must use
  `RomEditor.SaveNew`; verify both the prepared bytes and the durable file readback.
- Use the independently verified integrity policy for each ROM. Gen 1 profiles
  have an established additive correction word. L3CGEE000 has an independently
  established correction word at 0x03FFC0. Its six MAP coefficients lie inside
  the native code sum. Correct only the established word after checking the
  guarded program layout, native routines and markers. Ordinary calibration
  tables above 0x0C0000 lie outside the sum. Additional Gen 2 calibration
  integrity remains unestablished.
- Keep originals and existing exports intact. Failure must never publish an
  unchecked ROM or leave a successful-save state.
- Run `RomSaveIntegrityTests` for every bundled editor definition, plus the full
  regression suite, whenever editing or integrity behavior changes. New editable
  ROM profiles require an independently established integrity policy and inclusion
  in that test matrix. Tests must run without private ROM files or hardware.
- Raw ECU backups must preserve the captured bytes exactly. Apply checksum
  correction to editor exports; retain immutable original read captures.

Legacy ECU programming remains limited to its bundled Gen 1 profiles and separate
archived workflow; none of that code is shipped in MazdaLink. Keep the reader and logger allowlists read-only and keep
Gen 2 programming disabled until independently qualified. Do not publish ROM
binaries, private research, customer data, proprietary material or local evidence
paths.
