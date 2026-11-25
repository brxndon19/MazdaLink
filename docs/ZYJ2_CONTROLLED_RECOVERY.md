# ZYJ2 completed-unit boundary / cold recovery — alpha.16

The supplied alpha.15 evidence passed uninterrupted EB14 original restoration on one detached ZYJ2 spare: all 1024 units, both full original readbacks, monitor return and normal restart. The next test establishes whether that spare can enter the monitor and restore its original after a cold power cycle with an intentionally incomplete application.

**This is not a power cut during an active erase/program operation.** No operation is interrupted by this workflow. Power may be cycled only after the app confirms a completed, verified unit-zero boundary and explicitly prompts POWER OFF. Active-operation power-loss recovery and modified flashing remain unqualified.

## Inputs

Use only the physically detached ZYJ2EA000ZYV5050 / ZYC3 spare. Keep the good ECU disconnected and never run the test in a vehicle. Keep Mongoose USB, OBD 12 V, grounds and CAN powered. Switch only both ECU positive feeds when prompted. Use stable bench power; close other MazdaLink/diagnostic instances.

Choose **menu 19**, or:

```
MazdaLink.exe controlled-recovery-check "path\to\original.rom" "path\to\completed-alpha15-run-folder"
```

The first input is the complete matching-dual-read original backup folder's `original.rom`. The second input is the **completed alpha.15 original-restore-tests run folder**, containing `events.jsonl`, `transaction.jsonl`, `final-pass-1.read` and `final-pass-2.read`. Choose the folder, not one of its files. All four files are checked against the original, including the complete journal hash chain and unit intent/verification order. This establishes consistency, not unique ECU identity or OEM-stock authenticity.

Read the risk/setup text and confirm **BOUNDARY RECOVERY ZYJ2**. Recovery remains experimental; the spare may be left unable to start. Allow roughly 30 minutes for both halves.

## Paired test

1. Normal identity/RPM preflight, operator-confirmed 60-second ECU-only off wait, fresh entry window and exact boot/kernel/state verification.
2. Require the complete current ROM to match the original. Erase EB14, then verify the complete blank block and all preserved regions.
3. Program and independently verify **only original unit 0**, C0000..C007F. Leave C0080..DFFFF erased. The calibration ID at C0091 lies in unit 1 and remains erased. No bytes outside EB14 change.
4. Read and exactly compare the complete planned partial image. Save `partial.read`. Finish the kernel with checked protection/write-enable cleanup and require monitor identification. Close that native channel. Flush a distinct eight-record `controlled-stop` journal and save/verify `boundary.json`. This is not recorded as a completed original restoration.
5. Display **BOUNDARY READY** and the explicit POWER OFF prompt. Only now disconnect both ECU positive feeds, confirm POWER OFF, wait the host's 60 seconds and type START. Reconnect during the next 20-second ENTRY WINDOW. Host timing does not measure internal ECU voltage or prove electrical reset.
6. Open a new native channel with a new script/session and freshly verified state. Do **not** request normal application identity/RPM before recovery. Compare all boot bytes and the complete expected partial image, including erased calibration-ID bytes and every preserved region, before permitting erase. Any unexpected byte stops this controlled recovery path.
7. Erase the whole block again; program all 1024 original units once, with individual verification. Obtain two exact full-image readbacks, confirm monitor return, then independently check normal startup after the final 60-second POWER OFF and 10-second POWER ON sequence.

Never unplug power while a script is erasing or programming. Do not use a timer to guess an interruption point. The application sends no abort/reset, no automatic retry and no command that cuts power. A completed boundary and subsequent cold recovery do not establish recovery from an unknown completion or physical power loss during a pulse.

## If the app stops or closes

The app records the current phase and stops without resending a request. Save the evidence. If both original final readbacks already passed but normal restart later fails, that earlier result is retained; do not erase again solely because of the later failure.

Only after the earlier run has stopped/closed, **menu 20** can attempt the recovery half:

```
MazdaLink.exe controlled-recovery-resume "path\to\original.rom" "path\to\completed-alpha15-run-folder" "path\to\boundary-run-folder"
```

Confirm **RESUME BOUNDARY ZYJ2**. This requires a valid receipt, exact `partial.read` and complete eight-record preparation journal. A missing, corrupt, unfinished or wrong boundary cannot authorize this recovery. The new run copies those boundary artifacts into its own evidence folder, then uses fresh cold entry and an exact live partial-image check before full-block erase. It never resumes at unit 1 or trusts old unit progress to skip erase. Do not restart preparation against the partial ECU. If a valid receipt is unavailable, stop and send the evidence for review.

## Evidence

Keep the entire run folder under `Documents\MazdaLink\controlled-recovery-tests`:

- `events.jsonl`: bounded schema-13 phase/results/counters; no raw frame or ROM payloads.
- `partial.read` and `boundary.json`: the exact intentionally incomplete image and metadata receipt. The partial capture is not a flash target or a replacement original backup.
- `prepare\transaction.jsonl`: eight ordered, hash-chained, flushed records ending in `controlled-stop`, never `complete`.
- `recover\transaction.jsonl`: the new generation's complete erase/program verification journal.
- `final-pass-1.read` and `final-pass-2.read`: original-matching durable final captures.

Send the **complete run folder as a ZIP** for review. A receipt alone is insufficient evidence of recovery.

Both embedded MCU images are byte-identical to alpha.15; no new firmware, block selector or register write was added. The host composes fixed scripts for original unit 0 and full original restoration. Native gates still enforce all boot/code/state/descriptor/staging bytes, full FPFR, cleanup flags, exact data, one-pass units and durable intent. Failed sessions cannot advance a journal through late replies. EB15 and modified-image checksum policy remain unchanged. Generic `flash` remains unavailable.
