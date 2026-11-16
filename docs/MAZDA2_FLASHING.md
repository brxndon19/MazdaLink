# Mazda 2 DE flashing and recovery research

## Supported target

MazdaLink's flashing preflight is intentionally limited to the 2008 Mazda 2 DE, ZY-VE, Denso ECU using the Renesas SH7058 and software identity `ZY87EC000ZYV5050`.

## Current implementation

The current standalone application is `MazdaLink.exe`, a terminal program, not the
historical SpeedLink GUI. It only accepts the complete software identity
`ZY87EC000ZYV5050`, including before security access and in captured memory.
Other vehicles are accepted only when they report and contain that exact software.

Two matching complete reads are retained as `pass1.read` and `pass2.read`, with
`original.rom`, `backup.json` and a completed `journal.json`. Keep the entire folder.
The Export command creates a separate raw `.rom` copy without altering checksums.
The Prepare command compares an externally edited file with the complete original
backup, refuses changes outside the calibration region or to the software identity,
and corrects/verifies all 19 checksum ranges before and after saving a new file.
The Validate command rechecks the prepared image and its original backup.

Flashing and recovery remain unavailable. The Flash command refuses before opening
an adapter or sending any ECU request. No Gen 1 programmer or generic UDS write
sequence is included in the shipped binary. A checksum pass does not prove tuning
safety, factory provenance or a programming procedure. Actual Mazda 2 ECU/cable
qualification remains separate from synthetic software tests.

## Factory recovery / SH7058 boot mode

The Renesas SH7058 hardware documentation describes a hardware-selected boot mode using the MD mode pins and FWE, with programming communication over the on-chip SCI. It is separate from the normal user application and the Mazda 2's CAN/OBD read session.

The Renesas SH7058 hardware manual identifies Boot Mode and User Boot Mode as flash-programmable modes. The on-board programming application note says Boot Mode uses the on-chip SCI: the host first performs bit-rate adjustment and inquiry/selection; when the sequence leaves inquiry/selection and enters programming/erasing state, the boot program erases the entire user MAT and user boot MAT. Therefore, this is not a read-only ECU identification probe and must be treated as a destructive, full-flash recovery path. A complete, correct image must be prepared before entering it.

References:
- Renesas, [SH-2E SH7058 F-ZTAT hardware manual](https://www.renesas.com/en/document/mah/sh-2e-sh7058-f-ztat-tm-hardware-manual)
- Renesas, [SH7058F on-board programming application note](https://www.renesas.com/en/document/apn/sh7058f-group-018-m-process-f-ztat-microcomputer-board-programming)
- Tactrix, [Reflashing SH7058 ECUs using Renesas boot mode](https://www.tactrix.com/index.php?option=com_content&view=article&id=66)

The Renesas documents describe the microcontroller capability, not the exact Mazda ECU implementation. The exact Mazda 2 ECU board pinout, boot-mode pads, FWE and MD wiring, SCI electrical levels/baud negotiation, watchdog feed (if required by the board), host command sequence for this implementation, and successful write/readback evidence remain unverified. The Renesas application note describes a generic chip-level protocol; it does not prove the Mazda ECU exposes those signals at any particular pad or follows an unchanged reference implementation. Third-party SH7058 recovery descriptions are useful leads only and must not be treated as a Mazda 2 procedure.

## Qualification gates before recovery is enabled

1. Identify the exact ECU hardware and PCB revision.
2. Trace and verify the relevant MD/FWE/SCI and power/ground points from authoritative board-level evidence.
3. Establish the watchdog and electrical interface requirements without applying boot mode to a customer/vehicle ECU.
4. Obtain a documented command/response sequence for this exact SH7058 implementation.
5. Verify complete image layout and whether the boot MAT/user boot MAT are included in erase/program operations.
6. Determine whether the vehicle ECU has a separately implemented user-boot or other OEM recovery path; do not assume the SH7058 User Boot Mode exists in the Mazda firmware.
7. Demonstrate successful programming and verification on a spare bench ECU, including recovery from an interrupted attempt.

Until those gates pass, recovery remains research-only and live programming remains disabled.
