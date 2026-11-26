# ZYJ2 completed EB15 unit-zero boundary / cold recovery — alpha.19

The supplied alpha.18 run passed uninterrupted original-only EB15 restoration on one detached ZYJ2 spare: all 1024 units, both exact original captures, monitor return and normal restart. The next test checks cold entry and original restoration with deliberately incomplete EB15 boot/checksum metadata. It does **not** cut power during an active chip operation or qualify unknown command completion.

Use only the physically detached ZYJ2EA000ZYV5050 / ZYC3 spare. Keep the good ECU disconnected. Never run in a vehicle. Maintain stable bench power and close other diagnostic software. Keep Mongoose USB, OBD 12 V, grounds and CAN powered; switch only both ECU positive feeds at explicit prompts.

## Inputs

Choose **menu 24 — controlled EB15 boundary / cold recovery**. Supply the complete verified backup folder's `original.rom` and the **completed alpha.18 original EB15 restoration run folder** you retained from the passed test. That extracted folder must contain `events.jsonl`, `transaction.jsonl`, `final-pass-1.read` and `final-pass-2.read`. Choose the folder, not the RAR/ZIP or an individual file. Do not rerun menu 22 to create a newer prerequisite: this qualification is bound to the reviewed alpha.18 source.

The original, exact alpha.18 source/version and block scope, 115 records, successful original restoration/normal restart, both exact original captures and the complete schema-2 Block=15 journal are checked before adapter access. These checks establish consistency, not unique ECU identity or OEM authenticity. No edited file is accepted.

Read the risk/setup text and type **BOUNDARY EB15 RECOVERY ZYJ2**. Allow roughly **30–40 minutes** for the paired test. The spare may be left unable to start if recovery does not work.

```
MazdaLink.exe eb15-controlled-recovery-check "path\to\original.rom" "path\to\completed-alpha18-run-folder"
```

## Paired sequence

1. Require normal identity/stopped-engine RPM before preparation. Follow the ECU-only POWER OFF / 60-second wait / START / 20-second reconnect-window prompts. Enter the monitor, bind the preserved boot, and verify every byte of the fixed kernel and cleared state.
2. Compare the full current 1 MiB ROM against the original. Erase only EB15, then verify the complete blank block and every preserved byte.
3. Program and independently verify **only original unit 0 at E0000..E007F**. Leave **E0080..FFFFF erased**, including the checksum table/corrections and boot-validity pointer at FFFFC. EB14 and its calibration ID remain intact. Compare and save the entire exact planned partial image as `partial.read`.
4. Finish with checked protection/FWE cleanup and require monitor identification. Close the native channel. Flush the eight-record Block=15 preparation journal ending in **controlled-stop**, never complete. Save and validate a separate EB15 `boundary.json`, binding the original, partial capture, kernel and both prerequisite file hashes.
5. Only after **BOUNDARY READY** and the explicit **POWER OFF** prompt, disconnect both ECU positive feeds. Confirm POWER OFF, wait the host's 60 seconds, type START and reconnect during the next 20-second ENTRY WINDOW. Host timing does not measure internal ECU voltage or prove electrical reset.
6. Open a **new** cold channel, bind boot/code/state anew and compare the **complete planned partial/preserved image before erase**. Do not request normal application identity/RPM in this partial state. Any unexpected byte stops the controlled path before recovery erase.
7. Freshly erase the whole EB15 block again and restore all 1024 original units once. Verify each unit independently, obtain two exact full original captures, require monitor return, then follow the separate normal-restart POWER OFF / 60-second wait / POWER ON / 10-second startup sequence and normal diagnostics.

**Never remove power while erase/programming is running.** Do not guess an interruption point from a timer or progress counter. No active-operation cut, ECU reset, automatic retry or unit-index resume is implemented. A passed completed-boundary test does not establish physical power-loss recovery during a flash pulse or across arbitrary partial states.

## Recovery-only continuation

Only after the earlier run has stopped/closed, **menu 25 — resume validated EB15 boundary recovery** can attempt the recovery half. Supply the same original and completed alpha.18 folder plus the stopped boundary run folder. Type **RESUME EB15 BOUNDARY ZYJ2**.

```
MazdaLink.exe eb15-controlled-recovery-resume "path\to\original.rom" "path\to\completed-alpha18-run-folder" "path\to\eb15-boundary-run-folder"
```

This requires a valid EB15 receipt, exact partial capture and complete eight-record schema-2 Block=15 preparation journal. EB14 receipts, active-interruption claims and changed prerequisite hashes are refused. The artifacts are copied to the new run folder for self-contained evidence. Fresh cold entry and an exact live boundary comparison always precede full-block erase; no unit index is reused.

If recovery already changed EB15, the live image may no longer match that receipt and this path must refuse. Do not restart preparation or repeatedly erase. Save/send the evidence for review. If both final original reads and monitor return passed but a later restart stage failed, retain that earlier result and do not erase again solely for the later failure. If power must be removed after a stopped run, first leave the spare powered for at least 15 seconds.

## Evidence and scope

ZIP the complete folder under `Documents\MazdaLink\eb15-controlled-recovery-tests`: master `events.jsonl`, `boundary.json`, `partial.read`, `prepare/transaction.jsonl`, `recover/transaction.jsonl` and both `final-pass-*.read` captures. The partial capture is evidence, not a tuning target or replacement backup. A receipt alone is not proof of recovery. Event schema 15 records phase, block, source/kernel hashes, typed pointer-erased preparation result and cold partial confirmation separately from restoration and normal restart.

All three embedded MCU images are byte-identical to alpha.18. The host composes fixed scripts using the existing EB15 kernel; no controller operation, block selector or external code/address override is added. The first stage writes one original unit and verifies all remaining metadata erased; the recovery stage re-erases and restores every original unit. Existing EB14 workflows, receipts and schema-1 journals keep their own scope. Generic/modified flashing, active-operation power-loss qualification, two-block modified programming and the good ECU remain unavailable/unqualified.
