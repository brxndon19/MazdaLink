# ZYJ2 original-restoration qualification — alpha.15

This is an experimental first erase/program test on the **physically detached ZYJ2EA000ZYV5050 / ZYC3 spare**, not a modified-ROM flasher. It can leave that spare unable to start. Interrupted-write recovery has not been proved. Keep the good ECU disconnected; never run this in a vehicle.

The user's alpha.14 evidence passed the full ROM stream, eight RAM staging units, monitor return and normal restart on one spare. The verification capture exactly matched its previous original. This establishes the read/staging transport on that spare, not erase/program behavior or broader compatibility.

## First bench test

Use **menu 17**, or:

```
MazdaLink.exe original-restore-check "path\to\original.rom" "path\to\alpha14\events.jsonl"
```

The first path must be `original.rom` in the complete matching-dual-read backup folder. Keep `backup.json`, `journal.json`, `pass1.read` and `pass2.read` with it. The second path is the completed **alpha.14 menu-16** log that matched that same backup; it must include successful normal restart. This is consistency evidence, not authentication of a unique ECU or OEM-stock provenance. No edited-file, block/address, external kernel or resume override is accepted.

Read the risk/setup text and type **RESTORE ORIGINAL ZYJ2** only for the detached spare. Use stable bench power throughout execution. Do not intentionally interrupt power during this first restoration test. Close other diagnostic software. The initial workflow requires normal calibration/stopped-engine checks before cold entry.

Keep Mongoose USB, its OBD 12 V, grounds and CAN connected. Switch only **both ECU positive feeds**, following POWER OFF, the 60-second host wait and START. Reconnect the feeds when ENTRY WINDOW appears, within its 20 seconds, and leave the ECU powered. These host timings do not measure internal ECU voltage. Allow roughly 20 minutes for the complete script.

The fixed workflow:

1. Read and compare every preserved boot byte; upload and byte-verify the fixed writer and zero state.
2. Initialize both MCU algorithms, then stream the full ROM and require exact original equality before erase.
3. Flush an erase intent. Erase **only EB14, C0000..DFFFF (128 KiB)** through the documented SH7058S routine. Require successful full FPFR, checked FWE cleanup and evidence that the silicon routine invoked the RAM callback and serviced an observed timer overflow during the operation interval.
4. Read the complete ROM again: every EB14 byte must be FF, and every other byte must match the original before any programming.
5. Stage and byte-verify each of the 1024 original 128-byte units. Flush its intent before the programming request. Check the chip result and kernel readback, then obtain and compare a separate host unit readback before allowing the next unit. No unit is resent.
6. Obtain two complete final ROM reads, compare both exactly to the original, confirm the final monitor response and independently check normal startup after the prompted 60-second POWER OFF / 10-second POWER ON sequence.

Do not disconnect power during erase/programming. Cancellation or timeout does not send an abort/reset or prove whether an operation completed. A failure stops all later requests, retains its result and requires review of both logs. If power must be removed after a stopped run, leave the spare powered for at least 15 seconds first; this wait does not establish a successful flash outcome.

## Separate recovery attempt

**Menu 18**, `original-restore-recover <original.rom> <alpha14-events.jsonl>`, requires **RECOVER ORIGINAL ZYJ2**. It deliberately omits normal application identity/RPM preflight, since the calibration ID may be erased. It requires the same backup and prerequisite evidence, fresh cold entry, exact boot binding and full preserved-region equality. Only existing EB14 contents may differ.

Every recovery attempt **erases the complete block again** before a new one-pass restoration. It never resumes an old index or repeats an ambiguously completed unit. Prior journal progress cannot authorize skipping that erase. This is an unqualified recovery implementation; its presence is not a guarantee that a failed first test can be recovered. Do not intentionally interrupt a flash until original restoration and a separately designed recovery test have been reviewed.

## Evidence and scope

Keep the complete run folder under `Documents\MazdaLink\original-restore-tests`:

- `events.jsonl`: bounded schema-12 metadata, result, counters and separate restart evidence.
- `transaction.jsonl`: create-new, hash-chained records flushed before every destructive request and after verification. Incomplete intent is treated as an unknown outcome, never permission to resend.
- `final-pass-1.read` and `final-pass-2.read`: durable captures saved only after exact comparison with the original. They do not replace the original backup folder.

Send **both JSONL logs** for review. Logs contain no raw ROM/frame payloads. Driver cleanup uncertainty ends the application. Generic `flash` remains refused before driver open; modified flashing is disabled.

The read/staging image remains byte-identical to alpha.14. The separate original-only writer is **3012 bytes**, SHA-256 `64e9d433dfd752f28195537a97c1afe80f938c6ea9af1217f162a4a22a37d430`. Its RAM exception vectors are installed before FWE enable. NMI latches a failure in RAM; a fatal synchronous exception stays in RAM servicing the watchdog until operator power removal. The wrapper counts only callbacks while the chip operation is active, preserving R8..R15/GBR and using a reserved stack. Existing per-entry PB shadow ownership, overflow-only keyed timer service and pin/error latches remain in force. No ROM call or flash-controller write occurs in the callback.

Independent machine-code tests model silicon ABI calls and faults; they do not reproduce physical pulse timing, supply behavior or recovery. Passing the first original-restoration run does not itself qualify interrupted-write recovery or modified files. Native checksum corrections for modified images reside in preserved **EB15**, which this workflow never writes. A separate correction/write policy is still needed before tuning.
