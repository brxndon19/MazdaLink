# Mazda 2 DE flashing and recovery research

## Supported target

MazdaLink's flashing preflight is intentionally limited to the 2008 Mazda 2 DE, ZY-VE, Denso ECU using the Renesas SH7058 and software identity `ZY87EC000ZYV5050`.

## Current implementation

- Select an original full 1 MiB hardware backup created by MazdaLink and a modified 1 MiB ROM. The original backup's manifest, completed read journal, two captured read files, and SHA-256 are validated before it is accepted.
- Require the known ZY87EC000 identity and checksum table layout.
- Verify all 19 independently established checksum ranges in both images.
- Require the original and modified software identity to match.
- Reject any differences in the boot block, engine program, or other memory regions.
- Report calibration-region changes and image SHA-256 hashes.
- No ECU programming transport is connected to this workflow. No erase, download, transfer, reset, or recovery command is generated or sent.

This is a preflight and dry-run only. A passing result is not authorization to flash.

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
