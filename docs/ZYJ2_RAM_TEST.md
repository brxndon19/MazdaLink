# ZYJ2 spare: fixed RAM test

This is a separately selected experiment in the independent MazdaLink terminal,
not a flash kernel or interrupted-flash recovery test. Use only the detached
ZYJ2EA000ZYV5050 spare and its complete verified backup. Keep the good ECU
disconnected. The existing identification-only command remains restricted to its
two fixed payloads; it cannot enable this capability.

Choose menu **10**, supply the existing backup `original.rom`, and confirm
**RAM ZYJ2**. Normal identity and stopped-engine checks run first. Then:

1. Disconnect only both ECU positive feeds. Keep Mongoose OBD 12 V, USB, grounds
   and CAN connected. Type **POWER OFF** after disconnecting the ECU feeds.
2. Keep the ECU off during the application's 60-second wait. This occurs before
   entry traffic and follows the successful spare-entry procedure. It does not
   measure internal ECU voltage or prove a minimum discharge time.
3. With the ECU still off, type **START**. Reconnect both ECU positive feeds
   promptly at **ENTRY WINDOW**, within its 20 seconds, and leave them powered.
4. After native monitor identification, the fixed RAM experiment runs once.
5. After successful RAM execution and monitor return, follow the second ECU-only
   power-cycle prompt, then press Enter to check normal diagnostics again.

Keep the resulting `events.jsonl` from `Documents\MazdaLink\ram-self-tests`.
Failure, cancellation, unexpected replies or byte mismatches stop progression;
no packet or execution is automatically retried. If driver cleanup is uncertain,
the app exits and instructs a full adapter/ECU disconnection before restarting.

## Program and verification contract

The application accepts no external kernel, address or executable. Its immutable
56-byte SH-2 image is linked at RAM address `0xFFFF8000`; its source is
[`zyj2-ram-selftest.S`](../dotnet/firmware/zyj2-ram-selftest.S).
SHA-256: `311ce48738ec5699ac0980c82e68acee4aaaaf61664755cda4d2183896b8ebb0`.

The native upload routine does not acknowledge successful RAM copies. The host
therefore reads back all 56 bytes using fixed, single-byte native checksum
queries. A single byte's checksum is its exact value, including zero and 0xFF;
this avoids relying on an eight-bit whole-image checksum for executable code.
Every required reply must match before the native backend can advance. Only
after all 56 bytes match may one fixed execution request be sent.

The program performs two register reads: FCCS at `0xFFFFE800` and FMATS at
`0xFFFFE805`. It stores those two bytes only in its own RAM result, sends a distinct
marker plus those observations through the examined ROM CAN sender, and returns
through the existing stack trampoline to the monitor. The host then requires the
ordinary monitor identity again before asking for normal restart.

It does not write FKEY, FCCS, FPCS, FECS, FMATS or FTDAR, download an on-chip flash
algorithm, change the flash mapping, erase or program ROM, or issue an ECU reset.
FWE describes the observed hardware write-enable input; FLER reports the
controller's error protection state. MAT reports the current mapping. None of
these observations qualify a flash algorithm. An unexpected or missing result
does not establish recovery capability.

GNU binutils rebuild the image in CI; an independent restricted instruction model
checks its loads, two RAM stores and return targets. Synthetic transport tests
cover missing replies, corrupted bytes, cancellation, isolation of the original
identification capability and rejected external-code/address arguments. Actual
RAM upload/execution and its normal restart remain physically unqualified until
separately demonstrated on the spare.

To reproduce the owned program without hardware, install `binutils-sh-elf` and run:

```sh
python3 dotnet/tools/Verify-RamSelfTestProgram.py
```

## Constraints for the subsequent writer

The SH7058 user-flash erase geometry is not the editor's calibration geometry.
Any later writer must construct and verify complete erase blocks from the same
ECU's original image, while retaining the reset/monitor/resident-loader blocks.

| Blocks | Address range, inclusive | Required treatment |
| --- | --- | --- |
| EB0–EB7 | `0x000000–0x007FFF` | Preserve the boot/monitor/loader and neighboring bytes. |
| EB8 | `0x008000–0x01FFFF` | 96 KiB block; cannot use a calibration-only erase range. |
| EB9–EB13 | `0x020000–0x0BFFFF` | Five 128 KiB program blocks. |
| EB14 | `0x0C0000–0x0DFFFF` | 128 KiB calibration block; a possible first same-image bench restore target. |
| EB15 | `0x0E0000–0x0FFFFF` | Preserve neighboring data, checksum table and startup pointers when reconstructing the block. |

No writer is implemented here. The next gates include the independently reviewed
RAM flash algorithm, initialization/exit and watchdog behavior, exact block plan,
failure handling, stock restore and complete readback on the spare, then actual
interrupted-write recovery. Modified calibration testing and the good ECU follow
those gates. Do not cross-flash ZY87 and ZYJ2 images because their boot code is similar.

Reference: Renesas [SH-2E SH7058 F-ZTAT hardware manual](https://www.renesas.com/en/document/mah/sh-2e-sh7058-f-ztat-tm-hardware-manual),
sections 16 and 23. Silicon documentation does not establish board-level FWE
control or a qualified Mazda programming procedure.
