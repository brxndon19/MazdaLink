# ZYJ2 original-only EB15 qualification — alpha.18

This is the first hardware erase/program qualification for **EB15**, not a modified-ROM writer. Use only the physically detached ZYJ2EA000ZYV5050 / ZYC3 spare and Mongoose ISO3. Keep the good ECU disconnected. Never run this test in the vehicle.

The alpha.16 evidence passed cold recovery with an incomplete EB14 application while EB15 remained intact. It does **not** establish recovery with EB15 erased or physical power loss during an active chip operation. This test can leave the spare unable to start. Do not intentionally interrupt it.

## Inputs and selection

Choose **22 — ERASE / RESTORE ORIGINAL EB15**. Supply:

1. The complete verified backup folder's **`original.rom`**, retaining its `backup.json`, `journal.json`, `pass1.read` and `pass2.read`. Do not select the edited or prepared tuning file.
2. The **completed alpha.16 controlled-recovery run folder**, containing `events.jsonl`, `boundary.json`, `partial.read`, `prepare/transaction.jsonl`, `recover/transaction.jsonl`, `final-pass-1.read` and `final-pass-2.read`. Choose the extracted folder, not the ZIP or an individual file.

The reviewed paired alpha.16 source/version, 174 records, completed boundary, pre-erase partial check, both original-matching final captures, both complete hash-chained journals and successful normal restart are required. These checks establish evidence consistency, not unique ECU identity or OEM authenticity. The original itself must pass the exact ZYJ2 layout and all 19 checksums.

Read the setup/risk text and type **RESTORE EB15 ORIGINAL ZYJ2**. Command equivalent:

```
MazdaLink.exe eb15-original-restore-check "path\to\original.rom" "path\to\completed-alpha16-run-folder"
```

## Bench sequence

Keep stable bench power throughout execution. Keep Mongoose USB, OBD 12 V, grounds and CAN connected. Switch only both ECU positive feeds, and only when the application explicitly prompts.

1. The first test requires normal ZYJ2 identity and stopped-engine RPM. Confirm POWER OFF for the ECU-only 60-second wait, then START and reconnect both positive feeds during the 20-second ENTRY WINDOW.
2. Freshly identify the monitor and compare every boot byte. Upload and independently read back every byte of the separate fixed EB15 kernel and its cleared state. Compare the complete current 1 MiB image with the original before erase.
3. Flush erase intent, then erase **only EB15 E0000..FFFFF (128 KiB)**. This removes checksum corrections, other original calibration/data and the boot-validity pointer. The early boot monitor and all of EB14 must remain unchanged.
4. Verify the entire erased block and every preserved byte before programming. Program all **1024 original 128-byte units**, in order, once per erase generation. Flush intent before each destructive request and independently read back each unit. The correction word at FF6DC is in unit 1005; the final unit 1023 at FFF80..FFFFF contains the pointer at FFFFC and is restored **last**.
5. Obtain and durably save two exact full 1 MiB original readbacks, require protection/FWE cleanup and monitor return, then follow the separate POWER OFF / 60-second wait / POWER ON / 10-second startup sequence for normal diagnostics.

Allow roughly 20 minutes. Never cut power while erase/programming is running, and never guess an interruption point from a progress counter. No automatic retry, unit resume, ECU reset command or external block/code override is available.

## If a run stops

Keep the complete evidence folder. No ambiguous command is resent. If the app has stopped and power must be removed, first leave the spare powered for at least 15 seconds. Do not rerun preparation or try a modified ROM.

**Menu 23 — original EB15 recovery attempt** is separate and unqualified. It accepts the same original backup and completed alpha.16 folder, requires **RECOVER EB15 ORIGINAL ZYJ2**, omits normal application/RPM preflight, and attempts fresh cold monitor entry. Exact preserved-region equality is mandatory. Only existing EB15 contents may differ. If monitor entry succeeds, it freshly verifies code/state, erases the entire block again and restores every original unit from zero. It never trusts the earlier unit index. A missing monitor response stops before erase; availability of this attempt is not proof that physical recovery will work.

```
MazdaLink.exe eb15-original-restore-recover "path\to\original.rom" "path\to\completed-alpha16-run-folder"
```

If both final original readbacks and monitor return already passed but a later restart/cleanup stage failed, preserve that earlier result and send the evidence. Do not erase again solely because of the later failure.

## Evidence and implementation boundaries

ZIP the **complete** run folder under `Documents\MazdaLink\eb15-original-restore-tests`, including `events.jsonl`, `transaction.jsonl` and both `final-pass-*.read` captures. Event schema 14 records the selected block, exact kernel hash, commands, full FPFR, callback/timer flags and normal restart separately. The 2054-record journal uses schema 2 with **Block=15** on every record and a separate per-run session/hash chain. Existing EB14 journals retain schema 1 without a block field; they cannot qualify an EB15 run.

The separate 3012-byte kernel hash is **59a08f914f848badd851ee139b416859d2b7ee80873d5ee2b53dbeb848ade4f6**. Its erase block and programming base are compile-time constants; no descriptor can choose EB14 or another block. The existing read/staging and EB14 kernel bytes remain unchanged. Independent SH-2 instruction-model checks exercise exact binary/ABI scope, high unit addresses, callback/vector protection, full FPFR, taint and cleanup. Host/native tests cover all ordered units, preserved/blank checks, durable intent, missing replies, failed readback and fresh whole-block recovery. These models do not reproduce physical flash pulses or prove recovery.

Modified flashing stays disabled. A successful uninterrupted EB15 restoration will qualify only that observed original-restoration case on this spare. Separately review and test cold recovery with EB15 incomplete, two-block modified-image programming/recovery, the actual tuning-map changes and the good ECU before extending the scope.
