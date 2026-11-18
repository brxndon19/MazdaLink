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
  and `RAM ZYJ2` confirmation. Permit only the immutable built-in 56-byte program
  at 0xFFFF8000, its bounded upload and exact per-byte readback script, one execution,
  then monitor identification and normal restart. Never accept external code,
  addresses, repeated execution, skipped replies or a weak whole-image checksum
  in place of exact byte verification. The program reads FCCS/FMATS and writes
  only its own RAM result and the existing CAN path. No flash-controller write,
  on-chip algorithm download, erase or ROM programming is included. Physical RAM
  execution acceptance remains unqualified until separately demonstrated.
- Use the observed longer-off bench sequence: normal preflight, operator-confirmed
  ECU-only power removal, a monotonic 60-second host wait outside entry traffic,
  then open the adapter with ECU off and reconnect during the bounded entry window.
  The timer and adapter voltage do not measure internal ECU power or prove a
  minimum cold-start delay. Preserve the identification-only backend's two-payload gate.
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
