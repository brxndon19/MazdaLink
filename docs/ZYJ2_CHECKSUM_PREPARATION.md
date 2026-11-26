# ZYJ2 checksum preparation — alpha.17

Menus 4 and 5 now support the exact `ZYJ2EA000ZYV5050` profile as well as the existing ZY87 profile. This step is offline. No adapter is opened, no ECU power cycling is needed and modified flashing remains disabled.

Keep the complete original MazdaLink backup folder. Make a separate copy for external editing. For ZYJ2, this release accepts data edits only at **C0100..DFFFF**. The first 256 bytes of EB14, including the calibration header and complete software ID, must remain identical. Program, boot and all other data must remain identical apart from the 19 checksum correction words. Accepted addresses do not identify tuning maps or establish safe calibration values.

1. Choose **4 — Prepare an externally edited ROM**. Supply the backup folder's `original.rom`, your separately edited file and a new output filename. Preparation recalculates every correction word, verifies all ranges, flushes the file, verifies its disk copy and publishes the new filename without overwriting any existing file. It preserves both inputs.
2. Choose **5 — Validate modified ROM** to check a prepared output against the same complete backup.
3. Choose **21 — ZYJ2 modified-ROM block report** to inspect its EB14/EB15 difference counts and image hashes. Supply the original and prepared output. Keep this output for the next writer-development step.

Command equivalents:

```
MazdaLink.exe prepare "path\to\original.rom" "path\to\edited.rom" "path\to\prepared.rom"
MazdaLink.exe validate "path\to\original.rom" "path\to\prepared.rom"
MazdaLink.exe modified-rom-plan "path\to\original.rom" "path\to\prepared.rom"
```

The report is a difference inventory, not an executable programming plan. It accepts no block/address overrides. The original-only EB14 writer continues to reject every edited image. A checksum pass establishes arithmetic consistency with this specific verifier, not OEM authenticity, tuning safety or flashing compatibility.

## Independently established ZYJ2 policy

The supplied ZYJ2 verifier at **130B2..13135** selects an entry from **FF650** using a 12-byte stride. Each entry contains a 32-bit start, inclusive end and correction word. The range guard at **136F0..13715** checks start alignment and end ordering/alignment. The verifier accumulates big-endian 32-bit words with modulo-2^32 overflow and adds the selected correction. Success requires **5AA5A55A**; mismatches call its error path. This was traced in ZYJ2's own code, independently of its shared suffix or range-table resemblance to ZY87.

Reviewed verifier SHA-256: `bf85b436c23d3c7de00d435ee65329772b14afb9d71265b75e39614626fa6b5a`.
Reviewed range-guard SHA-256: `fc258a38652ce6de916ae5ae45f535d4e52bc6c6d9a79b25f0ccc21dc7e292e0`.

The public policy requires exact image size, embedded full software ID, calibration header, all 19 start/end pairs, table terminator and boot validity markers. The backup's independently retained dual-read hashes and reported software ID must agree. It is a structural policy, not whole-code authentication. Edited program bytes are refused by comparison with that verified original.

Private, non-hardware instruction-model checks exercised the supplied native verifier for every original range, mutations and corrected images, and malformed range rejection. Public regression tests use generated synthetic images and independent big-endian sums; no supplied ROM bytes are distributed. Instruction modeling does not qualify silicon erase/program behavior.

Range 11 covers **C0000..F7FFF**; its correction word is **FF6DC..FF6DF**, in **EB15 (E0000..FFFFF)**. Thus an ordinary accepted edit in EB14 generally also changes EB15. A checksum-neutral edit can leave that word unchanged, but still does not bypass the modified-flashing block. The present native kernel can erase/program only original EB14 and cannot write EB15.

## Remaining writer work

The alpha.16 hardware evidence passed controlled cold recovery from a completed unit-zero boundary with the application ID erased, followed by exact original restoration and normal restart on one spare. This does not establish recovery after physical power loss during an active chip operation.

Before enabling modified flashing, separately qualify EB15 erase/programming, preservation of its unrelated data and boot/checksum metadata, restoration ordering and original recovery across the affected blocks. The good ECU remains unqualified. This release changes no embedded MCU image, block selector, controller operation, original-restore capability or recovery prerequisite.
