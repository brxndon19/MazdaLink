# ZYJ2 native transactions — alpha.13

Menu **16**, `native-transaction-check <original.rom>`, requires **TRANSACTION ZYJ2** and a physically detached ZYJ2EA000ZYV5050 spare with its own complete verified backup. Keep the good ECU disconnected. No normal application identity/RPM preflight runs before cold entry. This is experimental; never run it in a vehicle.

Keep Mongoose USB, OBD 12 V, grounds and CAN connected. Switch only both ECU positive feeds. Follow POWER OFF, the 60-second host wait and START. Reconnect both feeds when ENTRY WINDOW appears, within its 20-second window. Leave the ECU powered while the script runs. The host wait does not measure internal ECU voltage.

The independent native capability permits only this immutable script:

1. Compare every preserved boot byte at 0..70FF with the verified backup.
2. Upload the fixed 2028-byte read/staging kernel at FFFF8000 and read back every byte. Upload and verify the zeroed 48-byte state once. Individual upload echoes only drain adapter traffic; they do not prove ECU acceptance.
3. Initialize both silicon algorithms with the fixed RAM timer/PB15 service callback. FWE stays low. No PD configuration, programming key or erase/program operation exists in the embedded build.
4. Execute sequenced, cookie-bound commands to stream all 2048 ROM blocks of 512 bytes. Enforce ordered data and metadata frames, command identity, CRC32 and full-width FPFR/status checks. Compare the complete capture exactly with the existing original before staging.
5. Upload, byte-verify and echo eight original 128-byte EB14 units: 0, 1, 127, 255, 511, 767, 1022 and 1023. These writes affect volatile RAM only. No modified file is accepted.
6. Finish and identify the monitor, then independently check normal diagnostics after the prompted 60-second POWER OFF and 10-second POWER ON restart.

The fixed kernel uses the established callback's per-entry PB high-byte latch normalization and RAM shadow. It preserves other physical output phases, but cannot restore unreadable prior input-latch history. A latched timer/pin fault stops further callback output changes. It does not disable watchdogs.

There is a 15-minute total script budget, bounded receives and per-command deadlines. No failed or ambiguous request is resent. If it stops, follow the displayed ECU-only power-cycle instructions before option 1. Driver cleanup uncertainty ends the application.

Local `Documents/MazdaLink/native-transaction-checks/<run>` holds bounded schema-11 `events.jsonl` metadata and `verification.read`. The latter is a verification capture, not a replacement for the original matching-dual-read backup. The original folder is never overwritten. Logs contain no ROM or frame payloads. Keep this whole evidence folder locally; send `events.jsonl` for review.

## Implemented offline writer work

The same owned C source has a **separate offline flash candidate**. It is compiled and executed only in a synthetic SH-2 model; its bytes are not embedded in the application and no hardware channel selects it. The read/staging binary's SHA-256 is `3ef235e86c0d603f697615e3810580fc9028a2fcb6d83cdba03dfcf89d965ee8`.

The candidate fixes erase to SH7058S EB14 (C0000..DFFFF), with 1024 ordered 128-byte program units. It verifies staging CRCs, protection, DMA-off and RAM vectors before enable; masks interrupts; routes NMI to a RAM failure latch; runs the documented silicon entry and RAM callback; checks full FPFR, cleanup and readback. An uncertain operation taints its erase generation. A fatal synchronous exception stays in RAM servicing the watchdog until operator power removal; it does not pretend to abort a flash pulse safely.

The host's original-only transaction engine records and flushes an intent before each destructive request, verifies erased/preserved/unit bytes and requires two exact final full-image reads. It is single-use; a new run always cold-binds and erases the whole block. There is no resume-index shortcut or command retry. Its create-new, bounded, hash-chained journal contains metadata only. **No shipped J2534 implementation implements this destructive device contract.**

A pinned compiler emits integer assembly, then the SH-2-only assembler rejects newer instructions. CI rebuilds both variants, checks the embedded bytes and executes independent synthetic success/fault cases, including RAM-only callback access while flash is busy and a RAM NMI handler. Silicon pulse timing and physical recovery are abstracted; these tests cannot qualify them.

## Remaining hardware gates

A read/staging pass establishes that transport path on this spare. It does not establish actual busy-flash callback behavior, original restoration or recovery after power loss during erase/programming. Those must pass on the detached spare before modified flashing or any good-ECU work. ZYJ2 modified-image checksum correction also remains independently unqualified; a common read protocol or suffix does not establish it. No road-tuning readiness or release date is claimed.
