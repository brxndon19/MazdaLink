# ROM checks and compatibility

Definitions are optional for ECU reading. The reader can dump unknown calibration
IDs using the selected compatible Gen 1 or Gen 2 application read protocol and the current 1 MiB range.
Editor exports always apply the profile's mandatory checksum/integrity policy;
checksum correction and verification cannot be disabled when saving an edited ROM.
See the [editor save requirement](EDITOR.md).

This list also controls **optional file inspection and checksum correction**, not read
permission. Unknown images can be backed up and reopened normally.

## Known inspection layouts

| Calibration | Vehicle family | Checksum start | Correction word |
| --- | --- | --- | --- |
| L3P4EM000 | Mazda 6 MPS (2006 Australia) | 0x048000 | 0x048000 |
| L38KEH000 | Mazdaspeed6 / Mazda 6 MPS | 0x048000 | 0x048000 |
| L39SED000 | Mazdaspeed3 | 0x048000 | 0x048000 |
| L39SEE000 | Mazdaspeed3 | 0x048000 | 0x048000 |
| L39SEF000 | Mazdaspeed3 | 0x048000 | 0x048000 |
| L3M6EG000 | Mazdaspeed6 / Mazda 6 MPS / Mazdaspeed Atenza | 0x03F000 | 0x0FFFF4 |
| L3M7EF000 | Mazdaspeed6 / Mazda 6 MPS | 0x03F000 | 0x0FFFF4 |
| L35HEH000 | Mazda3 MPS | 0x048000 | 0x048000 |
| L33XEH000 | Mazdaspeed3 / Axela | 0x048000 | 0x048000 |
| L3CGEE000 | Mazdaspeed3 / Mazda3 MPS (Gen 2) | Native code sum; see below | Validation only; no repair |

These layouts correspond to the published or locally prepared MazdaSpeedDefs
definitions. File checks use each ROM's identity, boot fingerprint and checksum
layout. A different or modified layout can still be captured as a raw dump; its
checksum is not guessed. Read success does not require a passing known-ROM check.

## Stock-ROM logging

All ten layouts above also have a logger profile. L38KEH000 and the three
L39S variants have 38 parameters; L3M7EF000, L33XEH000 and L3P4EM000 have 35; L35HEH000 has 36.
L3M6EG000 offers 33 parameters over 30 requests, with five baseline parameters unavailable.
L3CGEE000 offers 20 parameters; see [Gen 2 coverage and integrity limits](GEN2.md).
Its native sum covers code only; editable calibration bytes are excluded and invalid code is rejected without repair.
Each profile has independent ROM evidence and its own MAP calibration bindings.
Some older ROMs do not expose every baseline identifier through the checked path.
See [coverage and exclusions](../definitions/logging/All_ROM_Coverage.md).
All real-ECU logging checks remain pending. Unknown ROMs remain readable.

## Flashing and recovery

All nine **Gen 1** layouts in the table support writing and stock recovery with
the bundled definition and a verified original backup from the same ECU.
L3CGEE000 is Gen 2. Gen 2 flashing is planned for an upcoming release.
[Flashing and recovery guide](FLASHING.md)

## Validation limits

File-layout checks can be tested against reference images and synthetic data.
The shared reader has been tested through real adapters against a simulated ECU.
L3M6EG000 has a hardware backup with two matching 1 MiB passes through vLinker FS.
A user has also reported a successful Mongoose ECU dump; formal adapter and
vehicle qualification remains incomplete. The physical programming results
currently cover one L3M6EG000 bench ECU. [Validation scope](VALIDATION.md)
