# MazdaLink

A Windows terminal application for Mazda 2 DE raw ECU backups and verified ROM-copy preparation.

**Raw reads have no calibration whitelist.** A reported ID such as `ZYJ2EA000ZYV5050` no longer fails merely because it differs from ZY87. MazdaLink attempts its existing fixed read-only protocol (CAN 500 kbit/s, 7E0/7E8, session 10 03, security 27 03/04, read 23 24, 512-byte blocks, 1 MiB image). Compatibility is not established for every ECU: it stops if the ECU refuses the procedure, a reply is malformed, or the two reads differ. It does not probe other protocols or retry security keys.

**Checksum preparation/validation still supports only `ZY87EC000ZYV5050`.** Reading compatibility or a matching `ZYV5050` suffix does not establish another calibration's checksum or flash procedure.

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

Keep the Mongoose's **OBD 12 V supply and USB connection powered throughout**. Wait for the ENTRY WINDOW message, then switch off only the spare ECU's positive supply feeds for at least five seconds, reconnect them during the 20-second window, and leave them on. Keep ECU/OBD grounds and CAN wiring connected; do not lift the ECU ground to power-cycle it. After identification, follow the second ECU-only power-cycle prompt so MazdaLink can check normal diagnostics again. Driver failures in the communication path, malformed replies, cancellation or missing restart stop the experiment. Do not change ECUs, adapters or CAN wiring during the check. No alternative bitrate or CAN pin configuration is probed.

The isolated bench channel sends only a bounded fixed entry pulse and one identification request. It implements no kernel upload, memory write, execute, erase, ROM programming or ECU reset. A successful check is **not** recovery qualification after interrupted flashing. If the check stops after entry, power-cycle the spare before normal diagnostics. Bounded local evidence is saved under `Documents\MazdaLink\bench-checks`; it contains stages and the backup hash, never ROM payloads or security keys. The hardware workflow remains experimental and awaits physical acceptance on the spare and Mongoose adapter.

If driver cleanup cannot be confirmed, MazdaLink exits. Disconnect both USB and ECU battery power before reconnecting and restarting the application so stale adapter traffic cannot continue into another attempt.

Since alpha.4 the entry deadline uses monotonic elapsed time, rather than accumulating rounded sleeps. Driver start/stop calls can add latency; their measured durations and the observed entry duration are recorded separately. Schema-2 `events.jsonl` evidence includes application and adapter versions, optional adapter-supply readings before/after entry, native return codes, receive counts and bounded per-second traffic counts. The adapter is asked to provide local TX echoes; an echo is not proof that the ECU accepted a request, and a lack of echoes alone does not prove no transmission. Unsupported optional adapter diagnostics are recorded as unavailable. Supply snapshots measure the adapter feed at those moments, not ECU voltage throughout the power cycle. No raw frame payloads are logged. Identification still requires the exact reply to the single post-entry query.

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
