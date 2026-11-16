# MazdaLink

A dedicated Windows terminal application for the **2008 Mazda 2 DE, ZY-VE**, using the exact ECU calibration/software identity **`ZY87EC000ZYV5050`**. Other identities, including incomplete `ZY87EC000` replies, are refused before security unlock or ROM reading. It is not VIN-locked: another car with exactly the same ECU software can use it.

[![Checks](https://github.com/brxndon19/MazdaLink/actions/workflows/checks-dotnet.yml/badge.svg)](https://github.com/brxndon19/MazdaLink/actions/workflows/checks-dotnet.yml)

Download **MazdaLink.exe** from [Releases](https://github.com/brxndon19/MazdaLink/releases). Double-click it for the menu; no .NET installation is needed. The app targets Windows 10/11 with the manufacturer's registered **32-bit J2534 04.04 Mongoose-Plus ISO/CAN3 driver**. Hardware support remains experimental and has not been qualified on your physical ECU/adapter by this project.

## What it does

- Identify your exact ECU software; no other-vehicle or protocol overrides are provided.
- Read the complete 1 MiB ROM twice using the observed read session and 512-byte limit. It checks the software identity in ECU memory before starting the full capture.
- Save matching raw captures, an immutable `original.rom`, manifest and completed read journal under `Documents\MazdaLink\backups`. Keep the complete folder.
- Export an exact `.rom` copy to your chosen local filename. Existing files are never overwritten.
- Prepare an externally edited ROM against the complete original backup. Only calibration-region changes are accepted; all 19 checksum corrections are mandatory and verified before and after disk write. Inputs stay intact.
- Validate a modified ROM's identity, permitted changes and checksums.

**ECU flashing and recovery are unavailable.** The menu explains this and refuses before opening the adapter. A correct checksum does not establish a safe tune or an ECU programming procedure. There is no Gen 1 programmer, generic editor, logger, definition import, or other-car workflow in the shipped executable. Editing maps currently requires an external editor; no unverified map labels are imported from draft research.

## Start

1. Install your Mongoose driver, connect the adapter, switch ignition ON and keep the engine OFF.
2. Double-click `MazdaLink.exe`, choose **Identify ECU**, then **Read ECU and save backup**.
3. Choose **Export backup as .rom** if you want a separate working copy. Keep the original backup folder intact.
4. After external editing, choose **Prepare an externally edited ROM**, then **Validate modified ROM**. Flashing remains unavailable.

Run `MazdaLink.exe help` for commands or `MazdaLink.exe drivers` to list registered drivers. If several are installed, the menu asks which one to use. Command-line operations accept `--driver "exact registered name"` at the end.

## Source and validation

The current product lives in `dotnet/src/MazdaLink*`, with tests in `dotnet/tests/MazdaLink.Tests`. It has no project reference to SpeedLink assemblies. Historical SpeedLink source and definitions remain for provenance, GPL compliance and retained regression/integrity tests; they are not compiled into the new application. The old desktop UI workflow is available manually as **Legacy SpeedLink desktop checks** and is not the product release pipeline.

Use the exact SDK in `dotnet/global.json`. From `dotnet`:

```powershell
dotnet restore MazdaLink.slnx --locked-mode
dotnet test tests/MazdaLink.Tests -c Release --no-restore
dotnet publish src/MazdaLink -c Release --no-restore -o publish-mazdalink
```

Release checks retain the full existing core regression and all-profile ROM-save integrity gates, add dedicated target/transport/menu/backup/checksum tests, and test the exact standalone EXE before packaging. Synthetic tests are separate from hardware qualification. Releases contain only `MazdaLink.exe`, exact source, manifest and SHA-256 sums. Published assets are never replaced.

GPL-3.0-or-later. Selected low-level J2534, checksum and security routines are adapted from SpeedLink; original credits remain in [ATTRIBUTIONS.md](ATTRIBUTIONS.md) and [LICENSE](LICENSE). `MazdaLink.exe license` displays the embedded notices and licence. ROM binaries, vendor drivers and private research are not distributed.
