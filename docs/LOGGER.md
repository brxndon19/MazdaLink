# Stock-ROM logger

For a visual tour, see the [logger overview and screenshots](logger/README.md).

The logger draft covers nine Gen 1 ROMs with 33–38 parameters, plus experimental Gen 2
L3CGEE000 with 20. It uses the
factory application's read-identifier interface; a logging patch is not required.
The ROM encoders and offline conversions have been checked. Real-ECU acceptance,
sensor readings and achievable logging rates still need testing.

Both vLinker FS and Tactrix logger paths have passed isolated bench tests across
all seven profiles, including sustained requests, fault handling, desktop
recording and USB interruption/reconnect checks. These tests use simulated ECU
replies through real adapters. See the [validation results](../dotnet/docs/Validation.md).

Choose **Help → Logger guide** for this guide inside the app. Choose Logger or
Recordings in the labeled left navigation. **☰ Navigation** or
**View → Navigation labels** switches between names and compact icons.

## Before you start

Download **SpeedLink.exe** from [Releases](https://github.com/mrphyslaw/SpeedLink/releases)
and keep it in a writable folder. Install your adapter manufacturer's driver,
select the adapter in SpeedLink, and select its COM port for vLinker FS. For
Tactrix, choose its installed J2534 driver.
Mongoose-Plus ISO/CAN3 uses the experimental adapter option and its registered
J2534 driver. Hardware validation is pending; follow the [testing guide](ADAPTERS.md).

Have a full backup of the ROM currently installed in the ECU ready. If you need
to make one, use **Read & verify backup** with the engine stopped, then disconnect
and reconnect before logging. Use the verified `rom.bin` from that backup.

Read-only query acceptance and three consecutive Start/Stop sessions also passed
on one powered L3M6EG000 bench ECU through each of vLinker FS and Tactrix. This
checks the connection and recording workflow; physical sensor accuracy and
running-engine sampling behavior still require validation.

## Start logging

1. In **Connection**, select your adapter and choose **Connect & identify**.
2. Open **Logger**, then **Load current ECU ROM…**. Select a full backup of the
   image currently installed in the ECU. SpeedLink selects its profile and reads its MAP calibration.
3. Select parameters and display units. **Essentials** is a tuning preset;
   **All** includes every available parameter for that profile. More parameters reduce each parameter's rate.
4. For AFR display, enter your fuel's stoichiometric AFR, then choose AFR on the
   actual or desired mixture row. Leave it blank to log lambda.
5. Leave **Record CSV** checked to save a recording, then select **Start logging**.
   Use **Plot** to select up to six synchronized charts for logged parameters. **Stop** ends the recording
   and leaves a healthy connection ready for **Start logging** again. Your ROM,
   parameters, units and plot choices stay selected; each start creates a separate
   recording. Disconnect and reconnect before reading a ROM. **Open logs** opens
   the recording folder location.
6. After **Stop**, **View recording…** opens that session directly in Recordings.
   The shortcut is disabled during logging so you open the finalized file. A
   live-only session leaves the last saved recording available. Use
   **Recordings → Open recording…** for older files, including after restarting SpeedLink.

Logging can run with the engine running. **ROM backup reads require the engine
stopped**. They use a different diagnostic session. A connection used for a ROM
read must be disconnected before logging. ECU programming is a separate
[Gen 1 workflow](FLASHING.md).

The live calibration identifier must match the logger profile. SpeedLink accepts
the short calibration ID and its known complete ECU identity, such as
`L3P4EM000` and `L3P4EM000L3T6051`. The ROM filename does not affect matching.
Unknown ROMs can
still be read and saved; their logging layout is not guessed. Switching profiles
clears the previous ROM calibration. The loaded image's software marker and its
profile's code fingerprints are checked.
Matching a file and calibration ID does not prove the file is installed in the
vehicle; use the vehicle's current backup, especially with a changed MAP sensor.

## Rates and unavailable data

**Polling → Max** is the default: the next request starts as soon as the
previous reply is complete and the adapter is ready. Numeric limits are optional
when you want less bus traffic or smaller recordings; they limit complete rounds.
The table shows each channel's measured host sample rate and the footer shows
completed scans per second. These rates do not prove that the ECU refreshed its
underlying value at that rate. Screen redraws do not cap polling or recording.
See [measured bench performance](LOGGER_PERFORMANCE.md) and the
[protocol guide](LOGGER_PROTOCOL.md) for scheduling, completion and validation scope.

Only selected parameters and their dependencies are polled. Shared identifiers
are queried once per scan. Calculated boost and injector duty require fresh
inputs no more than 500 ms apart. Data older than five seconds is unavailable.
These initial limits are explicit draft settings, pending ECU validation.

A rejected channel shows its negative-response code instead of a number.
Unsupported identifiers are skipped for the rest of that logging session. Transient
condition rejections remain eligible on the next normal scan. Malformed replies,
timeouts and adapter failures stop the logger without resending the uncertain
request or automatically reconnecting. Stop waits for the bounded exchange in
progress; the window remains responsive.

## Recordings

Each recording creates a new folder under `data/logs` beside the EXE:

- `log.csv`: selected display values, a timestamp and status for each channel,
  plus the scan timestamp and monotonic elapsed time. Unavailable values are blank.
- `responses.jsonl`: raw diagnostic replies, requests, timing and rejection/error
  information, for troubleshooting and later verification.
- `session.json`: app/profile versions, selected units, ECU identity, loaded ROM
  hash, MAP calibration, AFR setting, timing limits and recording status.

The CSV is a sequential scan, **not an atomic ECU snapshot**. Use the per-channel
timestamps when comparing signals. Files flush after each scan. An interrupted
process can leave a partial final line; earlier flushed rows remain available.
A recording left marked `Recording or interrupted` did not close normally.
Keep the whole folder. ECU identity includes the VIN or chassis ID when available; review it
before sharing a log publicly.

Selections, units and plot choices are remembered separately for each profile in
`data/logger-settings.json`, including changes made before closing without logging.
Rate, stoichiometric AFR and CSV choices are also restored on launch. Load the
current ECU ROM again to establish its calibration; preferences do not bind a ROM.
Invalid AFR input leaves the connection available so you can correct it and retry.
After a backup, the Logger explains the required reconnect and provides a
**Go to Connection** button.

During a partial scan, a still-fresh last-known display value is marked **Pending**
with its age. Expired or rejected values are cleared. Recordings retain the actual
per-channel sample statuses and timestamps. Stopped charts show stopped status
immediately; their traces remain available as history.

SpeedLink ships without ROM files or adapter drivers. The logger uses the
supported stock interfaces. Use the [separate Gen 1 workflow](FLASHING.md)
for flashing and recovery.

See the [per-ROM coverage](../definitions/logging/All_ROM_Coverage.md) and
[ECU test checklist](../definitions/logging/All_ROM_ECU_Checklist.csv) for each
parameter's status and remaining checks.

## View a saved recording

Choose **Recordings** in the left navigation, then **Open recording…** and choose a SpeedLink
`log.csv`. This works without an adapter. Select up to six parameters for plots
with a shared time axis. Move the cursor slider or click a plot to inspect the
latest preceding observation; use Zoom to narrow the displayed time range.

Values keep their recorded units. Missing readings and long gaps break the
lines. Cursor readings become unavailable when their sample is too old. Plot
reduction retains narrow peaks and dips. Each channel shows its recorded host
sample rate, unavailable count and sample-age information; these do not establish
the ECU's internal update rate.

Opening a recording does not modify it. Complete earlier rows remain readable
if an interrupted recording ends with an incomplete CSV row; the viewer displays
a warning. The current viewer accepts SpeedLink's timestamp/status CSV format,
with a 128 MiB / one-million-observation limit.

See [protocol research](LOGGER_PROTOCOL.md) for the reviewed scheduling and
transport behavior and its remaining validation limits.

## Quick checks

- **Start logging is disabled:** connect a fresh session, load a supported current
  ECU ROM and select at least one parameter.
- **A ROM/profile mismatch appears:** check that the file is the current full
  backup from the connected ECU and that its calibration is listed in the coverage.
  The message shows the selected ROM profile and the ECU's reported calibration.
  Renaming the file will not change the match; include both IDs in a support report.
- **Updates are slow:** select fewer parameters and watch the measured **Hz**
  column. Use **Polling → Max**; selecting a higher numeric limit cannot make
  the ECU or adapter reply faster.
- **A timeout or USB disconnect stops logging:** reconnect manually, then start a
  new recording. The logger does not retry an uncertain request. Keep any partial
  recording for troubleshooting.
- **No recording was saved:** check **Record CSV** before starting and use
  **Open logs** to find the recording folders beside the EXE.

## Reporting a problem

Diagnostic capture starts automatically with **Connect & identify**, including
failed driver opens. On **Connection**, choose **Export support report…**, select
the session with the problem and save its ZIP. This also works after restarting
without reconnecting. Follow the [reporting guide](TROUBLESHOOTING.md) and attach
the ZIP to the [problem-report form](https://github.com/mrphyslaw/SpeedLink/issues/new?template=bug-report.yml).
The report includes requests, timing, errors and factory security access; personal
information is redacted and ROM bytes remain in your backup files. Review the ZIP
before sharing.

L3P4EM000 adds 35 parameters checked against its own ROM handlers and MAP
calibration bindings. Its live ECU logging, rates and adapter acceptance still
need testing. Desired fuel rail pressure, fuel injection amount and ambient air
temperature are excluded from this profile.

## Experimental Gen 2 logger

L3CGEE000 offers 20 native parameters with its own identifier mapping and full
identity binding. Load its matching ECU backup. Pressure has a 1 kPa resolution
and 255 kPa diagnostic ceiling. Other baseline channels remain unavailable until
established for this ROM. See [Gen 2 support](GEN2.md) for coverage and limits.
Real ECU sensor values and rates are pending.

## L3M6EG000 stock interface

This profile offers 33 parameters over 30 sequential requests. Native encoder,
identifier-copy and conversion checks use synthetic inputs; real ECU logging
acceptance and measured values/rates remain pending. Five baseline parameters
are not offered: desired fuel rail pressure, fuel injection amount, ambient
temperature, catalyst temperature and neutral switch. The stock status encoder
does not populate the neutral bit, so it is unavailable instead of a false zero.
