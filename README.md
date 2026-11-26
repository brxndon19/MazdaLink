# MazdaLink

A Windows terminal application for Mazda 2 DE raw ECU backups and verified ROM-copy preparation.

**Raw reads have no calibration whitelist.** A reported ID such as `ZYJ2EA000ZYV5050` no longer fails merely because it differs from ZY87. MazdaLink attempts its existing fixed read-only protocol (CAN 500 kbit/s, 7E0/7E8, session 10 03, security 27 03/04, read 23 24, 512-byte blocks, 1 MiB image). Compatibility is not established for every ECU: it stops if the ECU refuses the procedure, a reply is malformed, or the two reads differ. It does not probe other protocols or retry security keys.

**Checksum preparation/validation supports exact profiles `ZY87EC000ZYV5050` and `ZYJ2EA000ZYV5050`.** ZYJ2 data edits are restricted to C0100..DFFFF, preserving its header and identity; all 19 checksums are recalculated. Menu 21 reports the EB14/EB15 differences offline. Modified flashing remains unavailable. Menu 22 adds a separately selected original-only EB15 bench qualification; menu 23 is an unqualified original EB15 recovery attempt. Read [the EB15 test requirements](docs/ZYJ2_EB15_ORIGINAL_QUALIFICATION.md) first. After a completed alpha.18 original EB15 test, menus 24/25 provide a separately selected [completed-boundary EB15 cold-recovery qualification](docs/ZYJ2_EB15_CONTROLLED_RECOVERY.md). No active-operation power cut or modified flashing is enabled. See [ZYJ2 checksum preparation](docs/ZYJ2_CHECKSUM_PREPARATION.md). Reading compatibility or a matching `ZYV5050` suffix does not establish another calibration's checksum or flash procedure.

[![Checks](https://github.com/brxndon19/MazdaLink/actions/workflows/checks-dotnet.yml/badge.svg)](https://github.com/brxndon19/MazdaLink/actions/workflows/checks-dotnet.yml)

Download **MazdaLink.exe** from [Releases](https://github.com/brxndon19/MazdaLink/releases). Double-click it for the menu; no .NET installation is needed. The app targets Windows 10/11 with the manufacturer's registered **32-bit J2534 04.04 Mongoose-Plus ISO/CAN3 driver**. Hardware support remains experimental and has not been qualified on your physical ECU/adapter by this project.

## What it does

- Display the ECU's reported calibration. Require a valid identity and recheck that it is unchanged before unlocking.
- Attempt the complete 1 MiB ROM twice using the fixed read session and 512-byte limit. Raw captures do not require a ZY87 ID, header, checksum table, or memory identity offset.
- Save matching raw captures, an immutable `original.rom`, manifest and completed read journal recording the actual reported calibration under `Documents\MazdaLink\backups`. Keep the complete folder.
- Export an exact `.rom` copy to your chosen local filename. Existing files are never overwritten.
- For ZY87EC000ZYV5050 only, prepare an externally edited ROM against the complete original backup. Only calibration-region changes are accepted; all 19 checksum corrections are mandatory and verified before and after disk write. Inputs stay intact.
- For ZY87EC000ZYV5050 only, validate a modified ROM's identity, permitted changes and checksums.

**ECU flashing and recovery are unavailable.** The menu explains this and refuses before opening the adapter. A correct checksum does not establish a safe tune or an ECU programming procedure. There is no Gen 1 programmer, generic editor, logger, definition import, or other-car workflow in the shipped executable. Editing maps currently requires an external editor; no unverified map labels are imported from draft research.

## Start

1. Install your Mongoose driver, connect the adapter, switch ignition ON and keep the engine OFF.
2. Double-click `MazdaLink.exe`, choose **Identify ECU**, then **Read ECU and save backup**.
3. Choose **Export backup as .rom** if you want a separate working copy. Keep the original backup folder intact.
4. For the supported ZY87 checksum layout, choose **Prepare an externally edited ROM**, then **Validate modified ROM**. ZYJ2/other calibrations currently support raw read/export only. Keep their bytes unchanged; no checksum correction or flashing procedure is claimed.

Run `MazdaLink.exe help` for commands or `MazdaLink.exe drivers` to list registered drivers. If several are installed, the menu asks which one to use. Command-line operations accept `--driver "exact registered name"` at the end.

## ZYJ2 spare: bench monitor check

Menu **9**, or `MazdaLink.exe bench-recovery-check "path\to\original.rom"`, checks boot-monitor identification and a subsequent normal diagnostic restart on a **detached ZYJ2 spare**. It requires a complete MazdaLink backup folder for `ZYJ2EA000ZYV5050`, the examined monitor/loader code fingerprints, matching ZYC3 fields, a matching live calibration and an engine-stopped check. A standalone `.bin` without its verified backup journal is not sufficient; use **Read ECU and save backup** first.

Keep the Mongoose's **OBD 12 V supply and USB connection powered throughout**. Since alpha.6, after normal identity/RPM preflight, disconnect only both spare ECU positive feeds and type **POWER OFF**. The application waits **60 seconds before** opening the entry window. Keep the ECU off, type **START**, and reconnect both ECU positive feeds promptly when **ENTRY WINDOW** appears. Leave them powered. This follows the successful longer-off spare test; the host timer does not measure ECU voltage or prove the minimum required off time. Keep ECU/OBD grounds and CAN wiring connected; do not lift the ECU ground. After identification, disconnect only both ECU positive feeds again and type **POWER OFF**. Since alpha.7, MazdaLink waits another **60 seconds**; reconnect when prompted, type **POWER ON**, and leave the ECU powered through the **10-second startup wait** before one normal diagnostic check. Driver failures, malformed replies, cancellation or missing restart stop the experiment. Do not change ECUs, adapters or CAN wiring during the check. No alternative bitrate or CAN pin configuration is probed.

The identification-only backend sends only a bounded fixed entry pulse and one identification request. It cannot acquire the separate RAM-test capability. A successful check is **not** recovery qualification after interrupted flashing. If the check stops after entry, power-cycle the spare before normal diagnostics. Bounded local evidence is saved under `Documents\MazdaLink\bench-checks`; it contains stages and the backup hash, never ROM payloads or security keys. One operator-reported monitor entry and normal restart have passed on the detached spare; broader hardware qualification remains unestablished.

If driver cleanup cannot be confirmed, MazdaLink exits. Disconnect both USB and ECU battery power before reconnecting and restarting the application so stale adapter traffic cannot continue into another attempt.

Since alpha.4 the entry deadline uses monotonic elapsed time, rather than accumulating rounded sleeps. Driver start/stop calls can add latency; their measured durations and the observed entry duration are recorded separately. `events.jsonl` evidence includes application and adapter versions, optional adapter-supply readings before/after entry, native return codes, receive counts and bounded per-second traffic counts. Alpha.5 corrects the local TX-echo setting to use `SET_CONFIG` and adds a separate `GET_CONFIG` readback in schema-3 evidence. Alpha.4 accidentally read the setting instead of enabling it, so its zero configuration status did not establish that echoes were enabled. An echo is not proof that the ECU accepted a request, and a lack of echoes alone does not prove no transmission. Unsupported or unfilled optional adapter diagnostics are recorded as unavailable. Supply snapshots measure the adapter feed at those moments, not ECU voltage throughout the power cycle. No raw frame payloads are logged. Identification still requires the exact reply to the single post-entry query.

## ZYJ2 controlled boundary and cold recovery

Alpha.16 adds **menu 19**: after validating a completed alpha.15 restoration folder, erase EB14, program/verify only original unit 0 and verify the complete partial image. Only after checked cleanup and monitor return does it prompt for a cold power cycle. It then requires that exact partial image before fresh whole-block erase and original restoration. **Do not cut power during an active operation.** Menu **20** supports recovery-only continuation from a validated saved boundary. Keep the good ECU disconnected. See [inputs, prompts, limits and evidence](docs/ZYJ2_CONTROLLED_RECOVERY.md).

The supplied alpha.15 evidence passed original restoration and normal restart on one spare. Cold recovery from the planned partial image is the next unqualified hardware test; active-operation power-loss recovery and modified flashing remain unqualified.

## ZYJ2 original-only erase/write qualification

Alpha.15 adds **menu 17** for an experimental original-only EB14 erase/restoration test, and **menu 18** for a separate fresh-bind/full-erase recovery attempt. These **do erase and program flash** on the detached spare. They require its complete original backup, the matching completed alpha.14 menu-16 log and explicit risk/setup confirmation. Keep the good ECU disconnected. Recovery is unqualified; do not intentionally interrupt the first test. Modified files remain blocked. See [the full preparation, sequence and evidence requirements](docs/ZYJ2_ORIGINAL_RESTORE_QUALIFICATION.md).

## ZYJ2 detached spare: native full-ROM read and RAM staging

Alpha.14 adds menu **16**, `native-transaction-check <original.rom>`, with **TRANSACTION ZYJ2** confirmation. After exact boot/code verification it streams the complete ROM with ordered CRC32 replies, compares every byte to your original and stages/echoes eight original 128-byte units in RAM. FWE stays low; the embedded kernel contains no erase/program operation. Complete the separate normal restart and retain the new `events.jsonl`. See [the test sequence and writer progress](docs/ZYJ2_NATIVE_TRANSACTIONS.md).

The alpha.14 read/staging run passed on one spare. Alpha.15 adds a separate original-only hardware qualification path after offline erase/program and durable-intent testing. Actual interrupted-write recovery, original restoration and ZYJ2 modified-image checksum support still need qualification before tuning or flashing the good ECU.

## ZYJ2 spare: fixed volatile RAM self-test

Menu **10**, or `MazdaLink.exe ram-self-test "path\to\original.rom"`, selects a separate experimental capability. It requires the same verified backup/target/engine-stopped gates, **RAM ZYJ2** confirmation and a successful native monitor reply. Follow the longer-off prompts above. It uploads only MazdaLink's built-in 120-byte program to a fixed RAM location, reads back every byte before one execution, checks its distinct response, identifies the monitor again and checks normal restart after the requested power cycle.

The program calls the fingerprinted resident hardware-class query, then reads flash-controller **FWE/FLER** and **MAT** status without writing any flash-control register. The query temporarily enables H-UDI status access and restores SYSCR2, as the native RAM-copy routine already does. The class distinguishes the resident SH705x/SH7058S/SH7059 branches; it does not identify the entire ECU board. These are observations, not authorization to flash. No external code, address override, generic RAM editor, on-chip algorithm download, erase or ROM programming is available. The program also reads PD8 mode, direction, data and input bits to establish the initial state of the stock loader's candidate programming-enable output; it changes no GPIO setting. The alpha.7 spare log passed the complete 64-byte hardware-class test and normal restart, reporting SH7058S class with FWE=0. The new 120-byte snapshot remains physically unqualified. Schema-6 evidence is saved under `Documents\MazdaLink\ram-self-tests`; it contains bounded counters and status fields, never raw frames or RAM payloads. See [the fixed RAM test and writer constraints](docs/ZYJ2_RAM_TEST.md).

## ZYJ2 spare: protected flash-algorithm initialization

Menu **11**, or `MazdaLink.exe protected-flash-init "path\to\original.rom"`, selects a separate **INIT ZYJ2** capability. It verifies every byte of its fixed 936-byte program before execution, checks SH7058S class, controller protection, mapping, live CAN timing and PD8 preconditions, then loads the chip's own two flash algorithms into RAM and calls only their initialization entries. It performs controller selection/SCO writes, keeps FWE low, changes no PD8 setting and omits both the programming key and erase/program entries. It checks cleanup, monitor return and timed normal restart separately. The existing watchdog is serviced between phases; this is not an erase/program kernel.

The alpha.9 spare log passed both protected algorithm downloads/initializations, protection/cleanup checks, monitor return and normal restart. This does not enable flashing or qualify broader hardware compatibility or interrupted-write recovery. Keep its schema-7 `events.jsonl` under `Documents\MazdaLink\protected-initialization-tests`. See [the exact initialization contract and limits](docs/ZYJ2_PROTECTED_INITIALIZATION.md).

## ZYJ2 detached spare: read-only recovery boot binding

Menu **12**, or `MazdaLink.exe recovery-binding-check "path\to\original.rom"`, checks direct monitor entry and a byte-exact match of the preserved boot region against the spare's complete verified backup. **BIND ZYJ2** confirms the detached setup: this probe deliberately omits ordinary application identity/RPM preflight, so **never use it in a vehicle**. It reads only `0x000000–0x0070FF`; during binding it does not read the live calibration ID, upload/execute RAM code or write flash-controller registers. Other commands retain their normal preflight gates.

A full match is a firmware-region binding, not a unique ECU serial identity. Normal diagnostics are checked separately after the final timed power cycle of the intact spare. The new probe remains physically unqualified and does not restore an ECU or qualify interrupted-write recovery. Keep its schema-8 `events.jsonl` under `Documents\MazdaLink\recovery-binding-tests`. See [the exact read-only binding contract and limits](docs/ZYJ2_RECOVERY_BINDING.md).

## Source and validation

The current product lives in `dotnet/src/MazdaLink*`, with tests in `dotnet/tests/MazdaLink.Tests`. It has no project reference to SpeedLink assemblies. Historical SpeedLink source and definitions remain for provenance, GPL compliance and retained regression/integrity tests; they are not compiled into the new application. The old desktop UI workflow is available manually as **Legacy SpeedLink desktop checks** and is not the product release pipeline.

Use the exact SDK in `dotnet/global.json`. From `dotnet`:

```powershell
dotnet restore MazdaLink.slnx --locked-mode
dotnet test tests/MazdaLink.Tests -c Release --no-restore
dotnet publish src/MazdaLink -c Release --no-restore -o publish-mazdalink
```

Release checks retain the full existing core regression and all-profile ROM-save integrity gates, add dedicated compatibility/transport/menu/backup/checksum tests, and test the exact standalone EXE before packaging. Synthetic tests are separate from hardware qualification. Releases contain only `MazdaLink.exe`, exact source, manifest and SHA-256 sums. Published assets are never replaced.

GPL-3.0-or-later. Selected low-level J2534, checksum and security routines are adapted from SpeedLink; original credits remain in [ATTRIBUTIONS.md](ATTRIBUTIONS.md) and [LICENSE](LICENSE). `MazdaLink.exe license` displays the embedded notices and licence. ROM binaries, vendor drivers and private research are not distributed.

### Detached ZYJ2 spare: write-enable control experiment

Alpha.10's exact boot binding, monitor return and normal restart passed on the
single detached spare. Menu **13**, `write-enable-check <original.rom>`, now adds
a separate **ENABLE ZYJ2** experiment. It requires normal identity/RPM preflight,
the full cold-entry power sequence and a fresh live boot match before uploading
and verifying one fixed 752-byte GPIO test. It observes PD8/FWE once and checks
restoration, monitor return and normal restart independently. No flash-controller
register, algorithm download, erase or ROM programming is included. This new
control path remains physically unqualified. Read [the control contract and cold
latch review](docs/ZYJ2_WRITE_ENABLE_CONTROL.md) before using it on the spare.

### Recovery preparation and offline original restore recipe

Menu **14** / `recovery-rehearsal <original.rom>` adds a separately confirmed
**REHEARSE ZYJ2** experiment for the detached spare. It binds preserved firmware
without normal application diagnostics, initializes both MCU algorithms with a
RAM callback, and directly rehearses existing timer/PB15 service from RAM while
FWE stays low. It explicitly normalizes unreadable non-output PB high-byte latches;
the final power cycle remains required. No erase/program entry is present and
actual flash-busy callback behavior remains unqualified.

Menu **15** / `original-restore-plan <original.rom>` is offline only: an unchanged-
backup EB14 recipe using 1,024 aligned 128-byte units and whole-block recovery
rules after an ambiguous operation. It opens no hardware and accepts no edits.
See [the recovery rehearsal and restore contract](docs/ZYJ2_RECOVERY_REHEARSAL.md).
The native erase/program/readback backend is still unavailable.
