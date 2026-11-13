# MazdaLink Reader

A small, read-only command-line tool that reads the ROM from a Mazda ECU over a J2534 cable
and saves it as a `.bin` you can open in EcuFlash or another editor.

It uses the same cable code and read workflow as the desktop app. Every request passes
through the read-only filter (`DiagnosticProtocol.CheckReadOnly`), which blocks erase, write
and programming requests before they reach the cable. The reader cannot change your ECU.

## Build

From the `dotnet` folder, on Windows:

```
dotnet publish tools/MazdaLink.Reader -c Release -o publish-reader
```

This produces `publish-reader\MazdaLinkReader.exe` (32-bit, because J2534 cable drivers are 32-bit).

## Before you start

- Install your cable's driver. The default is the Mongoose-Plus ISO/CAN3; for a Tactrix
  Openport add `--adapter tactrix`.
- Ignition ON, engine OFF. Turn off headlights, fan and accessories.
- Use a healthy battery, or connect a battery charger.

## Starting it

Open PowerShell in the folder and type the commands below. You can also double-click
`MazdaLinkReader.exe`: it shows a short menu, asks a couple of questions and waits for Enter
before closing.

## 1. Probe

```
.\MazdaLinkReader.exe probe
```

The probe reads the calibration ID and VIN (the VIN is masked on screen and redacted from the
log), checks the engine is off, unlocks the read session once, and reads 16 bytes from the start
and 16 bytes from the end of a 1 MiB ROM. It does not read the whole ROM.

If it ends with `PROBE PASSED`, turn the ignition off and on again, then run the read.

## If the probe or a read is refused: scan

```
.\MazdaLinkReader.exe scan --gen2
```

Some ECUs unlock fine but refuse the read size or address the reader asks for (`NRC 31`,
"request out of range"). The scan unlocks once, then tries reads of 1 to 1023 bytes at the start
of the ROM and 16-byte reads at ten addresses across 1 MiB. It prints a table of what was
accepted, and the exact `read` command to use next, for example `read --gen2 --block 255`.
It only sends read requests.

## 2. Read

```
.\MazdaLinkReader.exe read
```

The reader reads the whole ROM twice and only saves it if both passes match. When it finishes,
it prints:

- **ROM to edit**: `Documents\MazdaLink\<calibration>_<date>.bin`. Open copies of this in your editor.
- **Original backup**: a read-only folder under `Documents\MazdaLink\backups\` with the matching
  captures, a manifest and the read journal. Keep this safe; it is your stock ROM.
- Whether a bundled definition matches your calibration, and whether the ROM's checksum is valid.

## Options

| Option | Meaning |
|---|---|
| `--adapter mongoose\|tactrix` | Cable to use. Default `mongoose`. |
| `--driver <name or dll>` | Choose a driver when more than one is installed. `drivers` lists them. |
| `--gen2` | Use the Gen 2 read protocol when the calibration isn't recognised. The Mazda 2 `ZY87EC000` ECU uses this. |
| `--block <bytes>` | Read in smaller blocks. Use the size `scan` recommends. |
| `--out <folder>` | Save ROMs and logs somewhere other than `Documents\MazdaLink`. |

## When something fails

Every run writes a log folder (printed at the end) under `logs\`. It records each request and the
ECU's reply. ROM bytes and the VIN are left out. Share it when asking for help.

| Message | What it means |
|---|---|
| `No Mongoose-Plus ISO/CAN3 driver found` | Install the cable's 32-bit J2534 04.04 driver, or check `drivers`. |
| `Engine is running` | Switch the engine off, leave the ignition on. |
| `The ECU refused the security key` | The ECU may use a different key. Turn the ignition off and wait at least 10 minutes before retrying so it doesn't lock access. Try `--gen2` on newer ECUs. |
| `PROBE FAILED ... refused to read the end of a 1 MiB ROM` | The ECU unlocked but may have a smaller ROM or different memory map. Share the log. |
| `The two ROM reads differ` | The connection was unreliable. Both captures are kept but no ROM was saved. Check the cable and battery and try again. |
| `J2534 response timed out` | The ECU stopped answering. Check the cable and ignition, then reconnect. Nothing is resent automatically. |

## Writing a ROM back

The reader never writes. Flashing an edited ROM is done in the MazdaLink desktop app, which
corrects and verifies the checksum on every save and only programs ECUs whose flash procedure
has been independently qualified (currently the Gen 1 profiles).
