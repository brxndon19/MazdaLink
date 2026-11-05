# Dump a ROM and help add support

You can contribute a ROM even when SpeedLink has no definition for it.
The reader saves two matching, complete reads before calling a backup verified.
Editing and logging support are separate from reading support.

**Start here:** [Download SpeedLink](https://github.com/mrphyslaw/SpeedLink/releases)
→ connect → identify → read twice → verify →
[open a ROM support request](https://github.com/mrphyslaw/SpeedLink/issues/new?template=rom-support.yml).

This workflow is for Mazda MZR DISI ECUs using a compatible Gen 1 or Gen 2 1 MiB read protocol.
An unknown calibration is welcome; a different ECU family or ROM size may need
a different reader. If you intend to flash a supported Gen 1 ECU, keep the
complete backup folder and follow the [flashing guide](FLASHING.md).

## 1. Get ready

You need a Windows PC and either:

- **Vgate vLinker FS USB**, with its manufacturer's Windows driver installed.
- **Tactrix Openport 2.0**, with the Tactrix Windows driver installed.
- **Mongoose-Plus ISO/CAN3 (experimental)**, with its 32-bit J2534-1 04.04
  manufacturer driver. Hardware validation is pending; see [testing instructions](ADAPTERS.md).

Download **SpeedLink.exe** from Releases into a writable folder. You do not
need an installer, a separate .NET installation, or a ROM definition.

Close other tuning and diagnostic applications so they release the adapter.
Use a charged battery and a stable power supply for the vehicle and PC.
Park the vehicle, connect the adapter to its OBD port and the PC, then turn the
ignition **on with the engine stopped**. SpeedLink checks for zero RPM before
starting a read.

## 2. Connect and identify

Open SpeedLink and choose **Connection** in the left navigation.

| Adapter | Select in SpeedLink |
| --- | --- |
| vLinker FS | **Vgate vLinker FS**, its COM port, and initial baud **115200** |
| Openport 2.0 | **Tactrix Openport 2.0** and the installed Openport driver; no COM port or baud is needed |
| Mongoose-Plus ISO/CAN3 | **Mongoose-Plus ISO/CAN3 (experimental)** and its registered 04.04 driver; no COM port or baud is needed |

Select **Connect & identify**. Note the calibration ID and SpeedLink version.
If identification fails, stop here and use the support form's **Need help**
option. Include the exact error with your VIN and local username removed.

The vLinker driver temporarily changes its USB transfer settings during a
read. Leave the app's normal settings in place; you do not need to experiment
with baud rates. See [connection troubleshooting](USAGE.md) if reconnecting fails.

## 3. Read and verify

Known ROMs select their protocol automatically. For an unknown Gen 2 ECU, choose
**Gen 2 read protocol (experimental)** in the adapter settings. Other unknown ROMs default
to Gen 1; confirm the ECU family before reading. See the [Gen 2 guide](GEN2.md).

Select **Read & verify backup** once. Leave the ignition on, engine stopped,
and both ends of the adapter connected while SpeedLink completes **pass 1/2**
and **pass 2/2**. Do not run a second diagnostic application during the read.

The progress panel shows the current pass, bytes read in that pass and the
overall percentage across both reads. Elapsed time continues during each reply;
the remaining-time estimate appears after transfer speed can be measured and
can change as the read proceeds. Verification stays below 100% until the backup
and completion records are saved. A stopped or failed read remains marked
**not verified**, even if both passes reached their final byte.

![Reading pass two with overall progress, bytes, elapsed time and estimated time remaining](reading/images/progress.png)

Success says **Backup verified · two matching reads** and shows 100%:

![Verified backup result after two complete matching reads](reading/images/verified.png)

A stopped or failed read keeps its last progress and clearly says **not verified**:

![Two reads that differ are marked interrupted and not verified, below 100%](reading/images/interrupted.png)

These are screenshots of the actual app using synthetic offline test data.
They do not show a vehicle test or measured ECU read times.

Success says **Backup verified · two matching reads**. The connection log also
shows **Verified backup saved:** followed by the path to `rom.bin`.

SpeedLink saves operations beneath `data/hardware/` beside the EXE. Use
**Backups → Open backup folder** to find them. Keep the **entire operation
folder** as your private backup, including `rom.bin`, `backup.json`, both read
captures, and the journal. A complete ROM is **1,048,576 bytes**.

Do not use **Save checksum-corrected copy** or the editor on a contribution
dump. We want the exact bytes read from the ECU, including its original checksum.
Checksum correction on editor exports is a separate operation.

## 4. Check the result

Open **ROM checks → Open verified backup…** and select that operation's
`rom.bin`. SpeedLink revalidates the captures, manifest and journal.
Record the ROM's **SHA-256** shown in ROM checks.

| Result | What to do |
| --- | --- |
| Two matching reads, known checks pass | Ready to contribute the unedited `rom.bin` |
| Two matching reads, unknown ROM/checksum unavailable | Also ready to contribute; this is useful for adding support |
| Two matching reads, a known-ROM check fails | Keep the exact dump and report which check failed; do not repair it for submission |
| Read stopped, errored, or the two passes differ | Keep the operation folder and request help; it is **not** a verified complete dump |

Matching reads establish repeatability. They do not establish that a tune is
factory stock. Tell us whether the ECU was stock, tuned, or of unknown history.

## 5. Prepare a contribution ZIP

Keep your full backup private. Create a **new, separate folder** containing:

1. A copy of the unedited `rom.bin`.
2. A plain text `ROM_INFO.txt` with the details below.

```text
Vehicle/model:
Model year:
Market/country:
Transmission:
ECU label/part number (if available):
Calibration ID shown by SpeedLink:
Factory stock, tuned, or unknown history:
SpeedLink version:
Adapter model:
Read result: two complete matching passes / need help
ROM size in bytes:
ROM SHA-256:
Known-ROM checks: pass / unknown / exact failed checks
```

In Windows File Explorer, select that new folder and choose **Compress to ZIP**
(or **Send to → Compressed (zipped) folder**). Give it a recognizable name such
as `Mazdaspeed6_2007_CALIBRATION.zip`. Do not include your whole `data` folder.

**Review before uploading:** the GitHub repository and its attachments are
public. `backup.json`, journals, raw captures, and screenshots can contain a VIN
or local paths. Leave those out of the public ZIP. A full ROM can also contain
vehicle-specific information; only upload it if you are comfortable sharing it.
If you need a private route, open the request without a ROM and ask the
maintainer how to provide it. Do not alter ROM bytes to redact a contribution.

## 6. Open the guided request

[**Open a ROM support request →**](https://github.com/mrphyslaw/SpeedLink/issues/new?template=rom-support.yml)

Choose **Verified ROM dump** or **Need help getting a dump**, fill in the
vehicle and adapter details, and attach your ZIP when available. Do not attach
an edited ROM or rename a partial capture to `rom.bin`.

GitHub supports ZIP attachments; see its
[file attachment guide](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/attaching-files).
You do not need to fork the repository or create a pull request.

## If a read fails

Keep the error text and operation folder. SpeedLink stops on rejected or
malformed responses and does not retry automatically. Disconnect in the app
and reconnect/identify before attempting a fresh read. If the adapter was
unplugged, reconnect its USB connection and start with the normal settings.
Use [General usage](USAGE.md) for the vLinker baud-recovery case.

Use **Connection → Export support report…** to save the failed session. Attach
its ZIP and report the step that failed, exact error, adapter, version, and
whether any bytes were read. You can open the same guided request without an attachment.
We cannot diagnose an incomplete dump as if it were complete firmware.

## What happens after submission

The maintainer will check completeness and hashes, compare against existing
ROMs, identify the calibration/program layout, and assess definition and logger
support. An uploaded ROM does not automatically become an editable profile.
New addresses, scaling, axes, code guards and checksums need verification, and
ECU testing remains separately tracked.

Maintainer intake checklist:

- Confirm 1 MiB size, SHA-256 and reported two-pass result; check for duplicates.
- Record vehicle/ECU identity and stock/tuned/unknown provenance.
- Identify boot/program/checksum layout before enabling corrections or editing.
- Verify map addresses, axes, scaling, shared bits and protected regions.
- Verify logger identifiers/decoding separately from map compatibility.
- Run offline encoding/export/guard checks and track pending ECU acceptance.
- Link the resulting support change back to the request.

## Reporting a problem

Diagnostic capture starts automatically with **Connect & identify**, including
failed driver opens. On **Connection**, choose **Export support report…**, select
the session with the problem and save its ZIP. This also works after restarting
without reconnecting. Follow the [reporting guide](TROUBLESHOOTING.md) and attach
the ZIP to the [problem-report form](https://github.com/mrphyslaw/SpeedLink/issues/new?template=bug-report.yml).
The report includes requests, timing, errors and factory security access; personal
information is redacted and ROM bytes remain in your backup files. Review the ZIP
before sharing.
