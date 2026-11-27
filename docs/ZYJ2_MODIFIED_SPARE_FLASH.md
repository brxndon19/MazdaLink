# Modified BIN/ROM flashing on the detached ZYJ2 spare

This experimental workflow is limited to the detached `ZYJ2EA000ZYV5050` / `ZYC3`
spare with the examined boot-code fingerprints. Keep the good ECU disconnected.
It is not an in-car tune writer. A correct checksum does not prove tuning safety.

Single-block original EB14/EB15 restoration and completed-unit cold entry were
observed on one spare. Combined modified flashing and recovery from power loss
**during an active flash pulse remain unqualified**. The ECU can be left unable
to start. Monitor entry is not guaranteed for every fault; damaged preserved boot
or program bytes are outside this recovery method. Do not intentionally interrupt
an erase/program operation.

## Inputs

Keep the complete MazdaLink hardware backup folder, including `original.rom`,
`backup.json`, `journal.json`, `pass1.read` and `pass2.read`. A loose original BIN
is not a verified backup. The edited file may use either `.bin` or `.rom`; these
are raw 1 MiB images, with no extension conversion required.

1. Use menu **4** with your verified `original.rom`, edited file and a **new file
   destination** such as `D:\MazdaLink\ZYJ2-prepared.rom`. This corrects and verifies
   all 19 checksums without changing either input.
2. Use menu **5** against the same original and prepared file. Only C0100..DFFFF
   data edits are allowed. The two-block writer additionally restricts EB15 edits
   to the established calibration correction word FF6DC..FF6DF. Boot, program,
   header, identity, table descriptors and boot-validity pointer stay original.
3. Select **26**, supply that original backup and the prepared BIN/ROM, and read
   the displayed original/target SHA-256 values. Confirm exactly:
   `FLASH MODIFIED SPARE ZYJ2`.

The current live image must also be an intact supported calibration with valid
checksums. Subsequent tunes can start from a previously written supported tune;
the original remains the reference for preserved bytes. Partial/corrupt images
are handled by the separately confirmed original recovery attempt.

## Power and progress

Use stable bench power. Keep Mongoose USB, OBD 12 V, grounds and CAN connected.
Switch only **both ECU positive feeds**, as prompted. Close other diagnostic
software. After normal identity/stopped-engine preflight, type `POWER OFF` after
removing those feeds; wait through the 60-second off timer. Type `START`, reconnect
both feeds at `ENTRY WINDOW`, and leave the ECU powered.

The application binds the preserved boot, installs/verifies the fixed EB14 kernel
and state, captures the complete live image, then erases and programs EB14 in 1024
ordered 128-byte units. It verifies each unit and performs two exact intermediate
1 MiB reads and monitor identification. The captured EB15 must remain byte-exact.

**Do not cycle power between blocks.** In the same powered monitor connection,
the host installs/verifies the separate fixed EB15 kernel and fresh state, checks
the entire intermediate image, erases EB15 and writes all 1024 intended units.
The final unit contains the unchanged boot-validity pointer and is written last.
Both final 1 MiB reads must exactly match the intended file and all checksums.
Allow around 30–40 minutes with the current byte-verified transport.

Only after both blocks and monitor return pass does the application prompt for
normal restart: ECU-only `POWER OFF`, 60 seconds, reconnect, `POWER ON`, ten-second
startup wait, then separate identity/stopped-engine diagnostics.

## Stop or recovery

Every destructive intent is flushed before execution. No ambiguous command is
resent. A failed comparison, transport error, cancellation or journal failure
stops later writes. Keep the entire folder under
`Documents\MazdaLink\modified-spare-flashes`, including events, transaction journal
and all saved read captures. If a run stops, leave the spare powered for at least
15 seconds before removing power.

Menu **27**, or `modified-spare-recover "path\to\original.rom"`, is a separately
confirmed **experimental** recovery attempt. Type `RECOVER BOTH ORIGINAL ZYJ2`.
It omits application diagnostics, performs fresh cold monitor/boot binding and
checks the entire live image. Only EB14/EB15 may differ from the original. It
freezes the observed unselected bytes, re-erases/restores **all of EB14 from zero**,
then does the same for **all of EB15**, without an intervening power cycle.
Dual exact original full reads, monitor and normal restart are required.

It never resumes an old index, trusts an interrupted journal as proof of success,
or rewrites preserved boot/program bytes. If entry, preserved binding or silicon
status fails, it stops; this is not a guaranteed recovery tool. If both target
reads passed and only the later restart failed, retain that evidence and diagnose
it before performing another erase.

Before considering the good ZY87 ECU, its hardware/boot compatibility and its own
backup/tune must be established separately. Do not use ZYJ2 table addresses in a
ZY87 definition or rename another calibration to bypass this scope.
