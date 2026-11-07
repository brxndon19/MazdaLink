# SpeedLink — C#/.NET

ROM reading, offline editing, data logging, and Gen 1 flashing and recovery for Mazdaspeed and Mazda MPS, built with .NET 10 and WPF for Windows.

Connect a Vgate vLinker FS USB or Tactrix Openport 2.0 and select it in the adapter list. For vLinker, also select its COM port. Switch ignition on with the engine stopped, then choose **Connect & identify** followed by **Read & verify backup**.

A definition is not required to dump a ROM. Two matching full 1 MiB reads are required before a verified backup is sealed. Original captures, manifest and durable journal are retained in the `data` folder beside the executable. Keep the whole backup folder.

**Editor** opens all ten bundled layouts for offline editing, including the
experimental Gen 2 L3CGEE000 profile.
**ROM checks → Inspect ROM…** opens known or unknown complete 1 MiB images for
inspection. Known layouts have guarded checks and separate checksum-corrected-copy
export; unknown layouts have no guessed checksum. Vehicle match and factory
provenance remain unverified.

Install the manufacturer's Windows driver and close other diagnostic apps before connecting. SpeedLink uses FTDI D2XX for vLinker and SAE J2534 ISO 15765 for Tactrix. The self-contained x86 build loads Tactrix's 32-bit driver directly and runs on 64-bit Windows. No driver is bundled.

The vLinker temporarily uses 2000000 baud during reads and restores 115200 on a clean disconnect. After reconnecting USB, start with 115200. If the app was interrupted while the adapter stayed connected, select 2000000 to reconnect. Baud selection is explicit; the port is never probed automatically. Tactrix uses its J2534 driver and needs no serial baud selection. Both adapters use 500 kbit/s CAN.

The standalone programming smoke test uses generated ECU replies. The public
programming workflow accepts only the nine bundled Gen 1 layouts.
[Flashing guide](../docs/FLASHING.md)

Mongoose-Plus ISO/CAN3 is experimental for reading, logging, and Gen 1 flashing
and recovery. Select its registered 32-bit J2534 04.04 driver. A user reported a
successful dump. Logging and programming still need physical qualification.
See the [adapter testing guide](../docs/ADAPTERS.md).

The **Logger** workspace provides 33–38 parameters for each of the nine Gen 1
profiles and 20 for L3CGEE000 Gen 2. It shows measured host sample rates and
supports selectable units, up to six synchronized plots and CSV recording.
Load the ECU's current matching ROM backup for MAP calibration.
Live ECU calibration identity must match. See the [logger guide](../docs/LOGGER.md).
ROM reads require an engine-stopped connection; logging uses the normal application
session and can run with the engine running. Read-only query and recording
workflows passed on one L3M6EG000 bench ECU. Running-engine rates and physical
sensor accuracy remain to be checked.

## Offline ROM editing

Open **Editor**. The ten bundled tuning
definitions provide grid editing, 2D curves, rotatable 3D surfaces, comparison,
selection arithmetic, clipboard, interpolation and undo/redo. Single values use
a dedicated value/state card; small grids and previews stay proportional to the
data. Fit map, the draggable preview divider and floating panels control the
workspace. Map info or F1 opens contextual help, with links to relevant
[tuning guide](../docs/TUNING_HELP.md) topics. Saves create new verified copies.
Gen 1 exports correct and verify the checksum. L3CGEE000 exports validate the
guarded program and markers and correct the native code sum when needed.
Original files are preserved.
Program-area signatures reject incompatible patched layouts while allowing
changes to defined tuning controls. See the [editor guide](../docs/EDITOR.md).

## Build and test

Use the .NET SDK pinned in `global.json` on Windows. From this folder:

```powershell
dotnet restore SpeedLink.slnx --locked-mode
dotnet test SpeedLink.slnx -c Release --no-restore
./tools/Test-EditorUi.ps1
./tools/Publish-Windows.ps1 -Output publish
```

The release is a self-contained **SpeedLink.exe**; users do not need to install .NET. The runtime may extract its native libraries into a temporary directory. Backups stay beside the EXE.

The [logger catalog](../definitions/logging/All_ROM_Coverage.md) includes each
ROM's parameters, exclusions, native test vectors and pending ECU checks.
`SpeedLink.Core.LoggerDefinition` provides decoding and calibration binding;
`LiveLogger` handles sequential identifier polling, dependencies, rejection status
and sample freshness. `LoggerRecording` saves CSV, raw replies and session metadata.

Run both offline desktop workflows for every profile with `./tools/Test-LoggerUi.ps1` after
building the solution. These use fixtures and do not access adapters or an ECU.

## Validation

The [C# results](docs/Validation.md) and [hardware validation scope](../docs/VALIDATION.md)
distinguish offline tests, physical adapters connected to a simulated ECU and
tests on a powered Mazda ECU. Use an isolated, terminated bus without a vehicle
or ECU attached when running an ECU simulator through physical adapters.

## License

GPL-3.0-or-later. See the repository LICENSE and this folder’s ATTRIBUTIONS.md. Runtime and dependency notices are bundled under Help & licenses. No ROM images or vendor drivers are included.

## Diagnostic support reports

Capture starts before driver open and uses a bounded background writer in
`data/diagnostics`. Connection provides a session picker, readable report preview,
ZIP export and a link to the problem-report form. See the
[reporting guide](../docs/TROUBLESHOOTING.md). `Test-SupportUi.ps1` exercises saved
failure selection and export with the native UI and standalone build. Core tests
cover security exchanges, redaction, payload exclusion, errors, storage failure,
retention and unchanged request counts.
