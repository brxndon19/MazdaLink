# MazdaLink requirements

## Product scope

- The current product is the independent terminal application in `dotnet/src/MazdaLink*`.
  Ship only `MazdaLink.exe`, never the legacy desktop or reader binaries.
- Raw reads and exports have no calibration whitelist. Require a valid reported
  calibration, recheck it before unlock, and retain its actual value in backup metadata.
  Do not impose ZY87 header/checksum-layout assumptions on raw captures.
- Checksum preparation/validation remains limited to independently established policies:
  currently `ZY87EC000ZYV5050` and independently traced `ZYJ2EA000ZYV5050`.
  The ZYJ2 editor permits data changes only in C0100..DFFFF and recalculates all
  19 correction words; retain its entire initial 256-byte header. The offline
  modified-ROM report has no native capability. Modified EB15 writing is limited to the dedicated two-block detached-spare workflow below. Never relabel an unknown backup or apply a known
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
- Generic/modified `flash` must refuse before driver open. The separately selected
  original-only detached-spare qualification and explicitly authorized modified-spare workflow below are the destructive exceptions;
  do not infer a Mazda 2 writer from generic UDS or Gen 1 code.
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
  Actual recovery with an erased calibration ID remains unqualified. The separate
  explicitly requested original-only qualification path may establish the first
  erase/restoration result; never treat it as recovery qualification. Preserve the fixed
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
- The alpha.11 fixed control test passed on one detached spare: observed FWE
  high/low, latch/direction/pin checks, monitor return and normal restart. This is
  not broader compatibility, flash-operation or interrupted-recovery qualification.
- The separate recovery rehearsal requires `REHEARSE ZYJ2`, detached-spare
  confirmation, cold entry and exact live boot binding before one byte-verified
  immutable 1404-byte program. It intentionally omits normal app/RPM preflight
  and live application-ID reads; preserve all other workflows' gates. Require
  class1, preserved ZYC3/counter guards, low FWE and known protection/timing,
  timer mode and PB15 output state. Initialize only the two fixed MCU algorithms
  with its fixed RAM callback; no FKEY5A or operation entry is permitted.
  The callback must be a RAM leaf preserving R8..R15/GBR/stack, with no user-MAT
  fetch, flash-controller write or recursive algorithm call. Service only an
  observed overflow with keyed TCSR A53D; never change timer/reset mode or TCNT.
  Explicitly normalize the PB high-byte non-output latches once, then use a RAM
  shadow and toggle only PB15; preserve other physical outputs and never claim
  unknown input latches were restored. Do not force an extra phase-restoring edge.
  A latched timer/pin error blocks further output changes; any failed kernel
  result stops host traffic before a later monitor request. No PD8/mux/direction
  write is allowed. Retain bounded evidence, independent model and release scope.
  Direct callback rehearsal is not observed silicon callback/flash-busy behavior.
- The unchanged-original EB14 restore plan is offline only, with no native
  destructive transaction capability. Require exact original bytes, full preserved-region
  equality, fixed EB14 bounds and aligned 128-byte units. No modified-image or
  generic address override is allowed. After ambiguous erase/program completion,
  never resend a unit: fresh bind and complete block erase precede a new one-pass
  program generation. Preserve the independent interruption/readback model;
  software recovery simulations do not qualify actual erased-application entry.
- The separate native transaction trial requires `TRANSACTION ZYJ2`, a detached
  spare, exact boot binding and every code/state/command/staging byte verified.
  Embed only the fixed read/staging build; no FKEY5A, PD write or erase/program
  operation is present. Require ordered command-bound CRC32 streams, exact full
  original equality before eight original-only RAM staging units, final monitor
  and independent normal restart. Preserve bounded metadata without payloads.
  Reset callback ownership before each preflight; rejected preflights must not
  service an unowned PB shadow, including with an already pending overflow.
  No capability conversion, external code/address, skipped replies or retries.
- The user-requested original-only qualification is a separate capability with
  RESTORE ORIGINAL ZYJ2 / RECOVER ORIGINAL ZYJ2 confirmations, a complete original
  backup and consistent completed alpha.14 read/staging evidence. The first test
  requires intact normal identity/RPM preflight; recovery deliberately omits it.
  Fresh boot binding and full preserved-region equality are mandatory. Only
  recovery may ignore prior EB14 contents. Never accept edited data, arbitrary
  code/addresses, resume indices or promotion from another capability.
- Restrict that qualification kernel to EB14 and the 1024 original 128-byte units.
  Install/check RAM vectors before FWE enable; preserve full FPFR and RAM-only
  callback ABI/stack. Require silicon-operation callback/timer-service observations,
  checked cleanup, complete blank/preserved readback, exact independent unit
  readbacks, two exact final full reads, monitor and separate normal restart.
  Flush durable intent before each destructive execute request; any uncertain
  completion permanently stops that session. Recovery always re-erases the whole
  block after fresh binding. That EB14 capability must never write EB15 or accept a different checksum policy.
  Actual original-restoration and interrupted-write recovery remain unqualified
  until separately observed. Do not intentionally interrupt the first test or
  expose modified flashing. Keep raw private evidence and ROMs outside Git.
- The supplied alpha.15 hardware evidence passed uninterrupted original EB14
  restoration on one spare, including callback/timer observations, 1024 units,
  both exact full reads, monitor and normal restart. Do not generalize that pass.
- The supplied alpha.16 evidence passed controlled cold recovery from the completed
  unit-zero boundary on that same spare: exact partial image with erased calibration
  ID, fresh pre-erase full check, full original restoration, both exact final reads
  and normal restart. Active-operation physical power loss remains unqualified.
- Controlled recovery requires the complete passed alpha.15 run folder and both
  original-matching captures plus the full validated journal. Prepare only unit 0;
  verify the exact complete partial image with the calibration ID erased, checked
  cleanup and monitor return before closing the channel and persisting a receipt.
  The preparation journal ends in controlled-stop, never complete. Prompt for
  operator power cycling only at that completed-operation boundary.
- Paired/recovery-only controlled continuation always opens a fresh cold channel,
  binds/installs/verifies state anew, and checks the exact planned partial image
  and every preserved byte before whole-block erase. Never resume a unit index.
  Recovery-only entry requires a valid receipt, partial capture and eight-record
  preparation journal; copy those artifacts to its new run folder. Require dual
  original reads, monitor and separate normal restart. Reject late replies after
  session failure. No active-operation power cut or reset is implemented; this
  test cannot qualify unknown completions or physical loss during a flash pulse.
- The separately selected original-only EB15 qualification requires RESTORE EB15
  ORIGINAL ZYJ2 / RECOVER EB15 ORIGINAL ZYJ2, the complete original backup and the
  completed paired alpha.16 cold-recovery folder. Validate all 174 records, the
  boundary receipt/capture, both old journals and both exact original final reads.
  Require exact ZYJ2 layout and all original checksums. Accept no intended-image,
  external block, code, address or resume argument. Keep generic modified flash blocked.
- EB15 has its own immutable compiled kernel (3012 bytes, SHA-256
  59a08f914f848badd851ee139b416859d2b7ee80873d5ee2b53dbeb848ade4f6).
  Its sole erase block is 15 and programming base is E0000. Preserve the exact old
  EB14 and read/staging binaries. First-test normal identity/RPM and exact full
  original equality precede erase; recovery skips app diagnostics but only EB15
  may differ. Require fresh boot/code/state binding, all blank/preserved bytes,
  all 1024 original units in order, dual final reads, monitor and normal restart.
  Restore the pointer-containing unit 1023 LAST. Flush intent before execute and
  retain full FPFR, RAM callback/timer, vector and FWE cleanup gates. No retry.
- EB15 journals use schema 2 and Block=15 on every record; old EB14 journals retain
  schema 1 with no block field. The offline EB14 device contract must reject an
  EB15 plan. Existing capability channels cannot be promoted to the new image.
  The original-only EB15 recovery attempt always cold-binds and re-erases the
  whole block from unit zero; physical erased-EB15 entry and active-operation
  power-loss recovery remain unqualified. Never intentionally interrupt the first
  EB15 test. No two-block modified writer or good-ECU qualification is implied.
- The supplied alpha.18 hardware evidence passed uninterrupted original EB15
  restoration on the same spare: all 1024 units with the pointer unit last, both
  exact original full reads, complete Block=15 journal, monitor and normal restart.
  This does not qualify erased-EB15 cold recovery or active-operation power loss.
- The separate controlled EB15 workflow requires the passed first alpha.18 EB15
  folder (all 115 records, Block=15 journal and both exact captures), original
  backup and BOUNDARY EB15 RECOVERY ZYJ2 / RESUME EB15 BOUNDARY ZYJ2 confirmation.
  Prepare only original E0000..E007F, verify the complete fixed partial image with
  E0080..FFFFF erased (checksum table and boot-validity pointer included), checked
  cleanup and monitor return, then close before persisting a receipt and prompting.
  Use a distinct EB15 typed result; never claim its EB14 calibration ID was erased.
- EB15 boundary receipts bind block, original/partial/kernel hashes, one unit,
  erased-pointer flag and both prerequisite file hashes; require the eight-record
  schema-2 Block=15 journal ending controlled-stop. Reject legacy EB14 receipts.
  Copy resume artifacts into the new run. Fresh cold binding/code/state and exact
  whole partial/preserved comparison precede recovery erase. Always re-erase and
  restore all original units; never resume an index. A changed live partial state
  stops before erase. Require dual full final reads, monitor and normal restart.
  Keep all MCU images unchanged. No active-operation interruption or two-block
  modified writer is implemented; software simulations are not hardware passes.
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

## Modified detached-spare transaction

- The user explicitly authorized modified BIN/ROM flashing after the alpha.15/16
  EB14 and alpha.18/19 EB15 original-write/completed-unit cold-entry results.
  Reuse that evidence; another original-only bench repetition is not inherently required.
- Only `ZYJ2EA000ZYV5050` / ZYC3 with the examined boot fingerprints is supported.
  Accept a complete verified original backup and a prepared raw 1 MiB BIN/ROM.
  Validate permitted data edits and all 19 checksums; EB15 may change only FF6DC..FF6DF.
  Do not expose good-ZY87 flashing or infer hardware equivalence from suffixes.
- Use a dedicated capability and confirmation: FLASH MODIFIED SPARE ZYJ2.
  EB14 then EB15 execute in the same powered monitor session, with fresh immutable
  kernel/code/state/command/staging verification for each block and the pointer unit last.
  A completed gate and both exact intermediate full reads precede the second block.
  Existing original-only channels must reject modified plans before driver access.
- Freeze the complete current image before erase. An ordinary tune requires valid
  supported calibration/checksums; recovery may accept unknown bytes only in EB14/15.
  Every other byte must match the original, and the unselected block must remain
  exactly equal to the captured image throughout each phase. Require unit readback,
  dual exact full reads, monitor return and separate normal restart.
- RECOVER BOTH ORIGINAL ZYJ2 always cold-binds and re-erases/restores both entire
  blocks to the original from zero. No individual request retry or unit resume.
  The separate durable journal binds transaction, original/target/live intermediate
  hashes and fixed block order; flush each intent before execution.
- Combined modified hardware acceptance, good-ECU compatibility, tuning semantics
  and active-operation power-loss recovery remain unqualified. Do not intentionally
  interrupt an active flash operation or claim recovery is guaranteed.

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
