# Getting started with SpeedLink

Download **SpeedLink.exe** from [Releases](https://github.com/mrphyslaw/SpeedLink/releases)
and put it in a writable folder. Double-click it to start; no installer or
separate .NET installation is needed. Keep the `data` folder beside the EXE
when updating the app: it holds backups, recordings, imported definitions and
settings. ROM files and adapter drivers are not included.

The permanent top menu contains **File**, **Edit**, **View**, **Tools** and **Help**.
Use **File → Open ROM…** from any workspace; **File → Save ROM copy…** exports
the current editor document with its mandatory integrity checks. Editor commands
are disabled when their required ROM or map is unavailable. Open a saved log with
**File → Open recording…**. Find definition management in **Tools** and guides,
problem reporting, support export, crash reports and About in **Help**.

Choose a workspace in the labeled left navigation. **☰ Navigation** collapses
its labels; **View → Navigation labels** controls the same setting. Your explicit
choice is remembered. Hover over an icon for its name and purpose. Use
**Editor** to edit a file, **Connection** to read an ECU, **Logger** to record
live data, **Recordings** to review logs, **ROM checks** to inspect a file, or
**Backups** to find saved operations. **Flash & Recover** under **Connection**
opens programming for the nine bundled Gen 1 layouts. A verified Gen 1 read also
offers **Open in Flash & Recover**. Use **Help → Gen 1 flashing
guide** for the write and recovery steps. Gen 2 flashing is planned for an upcoming
release. [Flashing guide](FLASHING.md)

## Edit a ROM without an adapter

1. Choose **File → Open ROM…** and select a complete supported ROM.
2. Find a map in the browser. **Map info** or F1 explains its units and purpose.
3. For a single value, type in the field and choose **Apply** or Enter;
   **Cancel** or Escape discards typing. For a table, double-click a cell, enter
   a value and press Enter. Switches use named choices.
4. Compare with the original or last save and use Undo/Redo as needed.
5. Choose **Save copy…** and pick a new filename. Every export applies its mandatory
   ROM-specific checksum/integrity checks. The original and existing files are preserved.

Drag the table/preview divider to resize it. **Fit map** restores automatic
sizing; Ctrl+B hides or restores the browser. Editing requires one of the ten
supported stock program layouts. Static axes are read-only; extended/patched
layouts are unsupported.

Use **Help → Editor guide** for the complete included guide, or see the
[editor guide](EDITOR.md) and [screenshots](editor/README.md) on GitHub.

## Read and back up an ECU

1. Install your adapter manufacturer's Windows driver. Close other diagnostic
   applications so they release the adapter.
2. Connect the adapter to the PC and vehicle. Turn the ignition on with the
   engine stopped, then open **Connection**.
3. Select **Vgate vLinker FS** and its COM port, or **Tactrix Openport 2.0**
   and its installed J2534 driver. See the adapter notes below.
4. Choose **Connect & identify**. Known ROMs select their read protocol automatically.
   For an unknown Gen 2 calibration, select **Gen 2 read protocol (experimental)**
   in the adapter settings. Choose **Read & verify backup** and leave the ignition
   on and adapter connected through both passes.
5. Wait for **Backup verified · two matching reads**. Find the operation under
   **Backups → Open backup folder** and keep the entire folder.

An ECU may report a short chassis number instead of a conventional VIN.
SpeedLink labels recognized replies as **Chassis ID**, removes trailing space/null
padding, and still reads the calibration. It keeps that ID separate from VINs in
backup metadata and checks it again before reading. A chassis number does not
identify a ROM layout: unknown calibrations can be dumped using the compatible
read protocol, while editing and logging require their own matching definitions.

During the backup, Connection shows the current pass, bytes read, overall
progress across both reads and elapsed time. Remaining time is estimated from
the measured transfer speed and can change during a read. Verification stays
below 100% until the backup and its completion records have been saved. Stop
or a failure leaves the last progress visible and marked **not verified**.

The reader covers the compatible Mazda MZR DISI 1 MiB family. A definition,
recognized calibration or valid checksum is not required to dump an unknown
ROM. Engine RPM must be confirmed as zero. Two complete reads must match before
`rom.bin` and `backup.json` are sealed. This proves repeatability, not factory
provenance or compatibility with another vehicle.

**Stop** finishes the current exchange and keeps the partial capture and journal.
A rejected/malformed response or transport error stops the operation without
automatic retries. An incomplete capture is not a verified backup. Reconnect
and identify before starting a new read. The [ROM-dump guide](DUMP_A_ROM.md)
walks through verification and contributing a file for support.

## Log live data and review recordings

Connect and identify, then choose **Logger → Load current ECU ROM…**. Use the
full backup currently installed in the ECU; its calibration must match. Choose
**Essentials** or individual parameters and units, leave **Record CSV** checked,
then choose **Start logging**. **Stop** finishes the recording and keeps a healthy
connection ready for another Start. The current ROM, selections, units and plots
stay selected. Disconnect and reconnect when switching between ROM reading and
logging, or after a connection error.

The logger supports nine Gen 1 profiles with 33–38 parameters each, plus the
experimental L3CGEE000 Gen 2 profile with 20. Logging can run with the engine
running; ROM reads require it stopped. The **Hz** column
shows measured host sample rates. Leave **Polling** on **Max** for the fastest
completed-reply polling; numeric limits are optional. Physical readings and
achievable running-engine rates still require vehicle validation. Read-only start/stop and query acceptance
on one L3M6EG000 bench ECU passed through vLinker FS and Tactrix. See the [logger guide](LOGGER.md) for units, plots and
unavailable data, plus [measured bench performance](LOGGER_PERFORMANCE.md).

Choose **Recordings → Open recording…** to review a SpeedLink `log.csv` without
an adapter. Logs live under `data/logs` beside the EXE. Keep the complete folder,
including raw replies and session details, for troubleshooting.

## Inspect a ROM or backup

**ROM checks → Open verified backup…** revalidates a backup's captures, manifest
and journal. **Inspect ROM…** accepts any complete 1 MiB image. Known layouts
get identification, boot, checksum-layout and checksum checks; unknown layouts
show their size and SHA-256 without a guessed checksum.

**Save checksum-corrected copy…** is available only for a recognized guarded
layout. It creates a new local file and never programs an ECU. Preserve exact
unedited dumps when contributing a ROM. Backups and logs can contain a VIN or
local paths; review reports before sharing them.

## Adapter notes

- **vLinker FS:** use initial baud **115200**. The adapter temporarily uses
  2,000,000 baud during reads and normally returns to 115200 on disconnect.
  After a read transport error or interruption while USB stays connected, select
  2000000 to reconnect when
  that temporary setting remains active. After reconnecting USB, start at
  115200. The app uses the installed FTDI driver with 1 ms latency and restores
  the prior latency setting when closing the connection.
- **Tactrix:** select the installed J2534 driver. No COM port or baud selection
  is needed. Registered Openport-compatible entries remain available.
- **Mongoose-Plus ISO/CAN3 (experimental):** choose its experimental adapter
  option and registered 32-bit 04.04 driver. A user reported a successful dump.
  Logging and Gen 1 flashing and recovery are also available for experimental
  testing. See the [adapter guide](ADAPTERS.md) for qualification status.

## Find help and definition updates

Choose **Help**, then select Getting started, Editor or Logger. In the editor,
**Map info** / F1 explains the active map; **Help → Editor guide** opens the full
editing instructions.

**Definitions → Import…** installs editor XML or logger JSON updates for
supported layouts. Save open work, then reopen the ROM to use an editor update;
reload the current ECU ROM for a logger update. Imported copies live under
`data/definitions`; bundled copies remain available. See
[definition import](DEFINITIONS.md) for accepted files and restoring a bundled copy.

## Reporting a problem

Diagnostic capture starts automatically with **Connect & identify**, including
failed driver opens. On **Connection**, choose **Export support report…**, select
the session with the problem and save its ZIP. This also works after restarting
without reconnecting. Follow the [reporting guide](TROUBLESHOOTING.md) and attach
the ZIP to the [problem-report form](https://github.com/mrphyslaw/SpeedLink/issues/new?template=bug-report.yml).
The report includes requests, timing, errors and factory security access; personal
information is redacted and ROM bytes remain in your backup files. Review the ZIP
before sharing.

## Experimental Gen 2

L3CGEE000 has a 448-control editor and 20-parameter stock logger. Known ROMs
choose their reader automatically. For an unknown compatible Gen 2 ECU, select
**Gen 2 read protocol (experimental)** in Connection. Both read passes must match.
Gen 2 saves validate the supported code layout and markers and correct the
native code checksum when needed. Choose **Help → Gen 2 (experimental)**
for scope, exclusions and pending real ECU validation.

## L3M6EG000

L3M6EG000 / L3M6EG000L3T60A1 includes 863 editor controls and a 33-parameter
stock-ROM logger. Open your complete 1 MiB backup; its filename does not select
the definition. Every editor save corrects and verifies the native program sum.
The vehicle-specific identification record remains unchanged.

The logger makes 30 sequential requests per full scan. Desired fuel rail
pressure, fuel injection amount, ambient temperature, catalyst temperature and
neutral switch are unavailable through this checked stock interface. They are
not substituted with zeros. Load the current matching backup for MAP calibration.
Live ECU query acceptance, physical readings and rates still need testing.
