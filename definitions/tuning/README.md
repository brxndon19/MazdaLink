# Bundled tuning definitions

These ten XML files are SpeedLink snapshots of the MazdaSpeedDefs definitions,
incorporated under its MIT license (LICENSE in this folder). They
are embedded into the executable. No ROM image or commercial software is bundled.

Source: https://github.com/mrphyslaw/MazdaSpeedDefs

The editor requires an exact definition identity, the SpeedLink
boot/checksum-layout checks and a program-area signature. Program signatures
cover the recognized program area with defined numeric controls, named switch bits and
the correction word masked out. Table edits remain supported; other program
modifications are rejected. SHA-256 signatures contain no ROM payload. Static axes are display metadata. A
BitwiseSwitch's sizex counts named bits within shared storage, not sequential
data elements. All named bits are exposed individually and neighboring bits
are preserved. See the [editor guide](../../docs/EDITOR.md) for usage and current
limits and the [tuning guide](../../docs/TUNING_HELP.md) for control explanations.

L3CGEE000 retains the supported native addresses and geometry. Its help clarifies
knock-reference arithmetic and encoded injector coefficients. Export verifies the
native code sum and four markers. The six MAP scaling controls are inside the
code sum and use the established correction word on save. Other editable
calibrations are outside that sum. Additional calibration integrity is unestablished.

Update these snapshots deliberately, retain the license, and rerun editor tests
and the local reference-ROM audit after definition changes.

L3M6EG000 includes 263 tuning tables and 600 diagnostic controls. Its native
program sum starts at 0x3F000; automatic correction uses reserved padding at
0xFFFF4 and preserves the instructions at the range start. Its boot fingerprint
normalizes only the established 128-byte per-ECU identification record. That
record is retained byte-for-byte in every exported ROM; the remaining boot bytes
and program/checksum guards remain checked.
