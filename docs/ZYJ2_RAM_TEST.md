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
5. After successful RAM execution and monitor return, disconnect only both ECU
   positive feeds again and type **POWER OFF**. Keep them off for the application's
   second **60-second** wait. No monitor-entry traffic runs during normal restart.
6. When prompted, reconnect both ECU positive feeds and type **POWER ON**. Leave
   them powered during the **10-second startup wait** and the single normal
   diagnostic check. Keep adapter power, grounds and CAN connected throughout.

Keep the resulting `events.jsonl` from `Documents\MazdaLink\ram-self-tests`.
Failure, cancellation, unexpected replies or byte mismatches stop progression;
no packet or execution is automatically retried. If driver cleanup is uncertain,
the app exits and instructs a full adapter/ECU disconnection before restarting.

## Program and verification contract

The application accepts no external kernel, address or executable. Its immutable
64-byte SH-2 image is linked at RAM address `0xFFFF8000`; its source is
[`zyj2-ram-selftest.S`](../dotnet/firmware/zyj2-ram-selftest.S).
SHA-256: `5fbb400331919923cde4b96952c8e5ae65aa0768294776009d7796c1607d6742`.

The native upload routine does not acknowledge successful RAM copies. The host
therefore reads back all 64 bytes using fixed, single-byte native checksum
queries. A single byte's checksum is its exact value, including zero and 0xFF;
this avoids relying on an eight-bit whole-image checksum for executable code.
Every required reply must match before the native backend can advance. Only
after all 64 bytes match may one fixed execution request be sent.

The program calls the fingerprinted resident hardware-class routine at `0x0EE4`,
which is already used by native RAM copying and clock setup. That routine saves
SYSCR2, temporarily enables H-UDI status access, reads SDSR and restores SYSCR2.
The owned program then reads FCCS at `0xFFFFE800` and FMATS at `0xFFFFE805`, stores
three result bytes in its own RAM and sends `ML2` plus those observations through
the examined ROM CAN sender. It returns through the existing stack trampoline to
the monitor. The host requires the ordinary monitor identity again before normal
restart. Class decoding reflects the resident routine, not an ECU label:

| Resident class | Meaning | Limit |
| --- | --- | --- |
| 0 | Legacy SH705x branch, SDSR high byte `0B`/`0D` | Does not establish one exact MCU or erase map. |
| 1 | SH7058S branch, SDSR high byte `50` | Requires SH7058S algorithm/timing qualification. |
| 2 | SH7059 branch, SDSR high byte `0E`/`0F` | Nominal 1.5 MiB flash; the current 1 MiB capture is not a complete device backup. |
| Other | Unknown resident class | No device-specific programming plan can be selected. |

It does not write FKEY, FCCS, FPCS, FECS, FMATS or FTDAR, download an on-chip flash
algorithm, change the flash mapping, erase or program ROM, or issue an ECU reset.
FWE is a read-only observation of the hardware write-enable input; zero reports
write protection at that instant. It cannot be enabled by setting the FCCS bit.
The board-level control path remains unestablished; FLER reports the
controller's error protection state. MAT reports the current mapping. None of
these observations qualify a flash algorithm. An unexpected or missing result
does not establish recovery capability.

GNU binutils rebuild the image in CI; an independent restricted instruction model
checks its loads, three RAM stores, resident-call argument handling and return
targets. Synthetic transport tests
cover missing replies, corrupted bytes, cancellation, isolation of the original
identification capability and rejected external-code/address arguments. The
alpha.6 log established exact readback, execution and monitor return for its
previous 56-byte program. The built-in normal-restart check timed out; subsequent
independent normal identification passed after a longer power cycle. This new
64-byte class-query program and the complete revised restart sequence remain
physically unqualified until separately demonstrated on the spare. Schema-5
local evidence records those stages independently.

To reproduce the owned program without hardware, install `binutils-sh-elf` and run:

```sh
python3 dotnet/tools/Verify-RamSelfTestProgram.py
```

## Constraints for the subsequent writer

The MCU's erase geometry is not the editor's calibration geometry. Shared boot
code does not establish the same silicon or programming algorithm. The official
SH7058S/SH7059 manual describes these device-specific differences:

| Device | User flash and upper erase blocks | Algorithm constraints |
| --- | --- | --- |
| SH7058 | 1 MiB; EB9–EB15 are 128 KiB, `0x020000–0x0FFFFF`. | Up to 2 KiB on-chip download; four required NOPs after SCO. |
| SH7058S | 1 MiB; same erase-block division as SH7058. | Up to 3 KiB on-chip download; eight required NOPs after SCO. |
| SH7059 | 1.5 MiB; EB9–EB11 are 128 KiB, `0x020000–0x07FFFF`; EB12–EB15 are 256 KiB, `0x080000–0x17FFFF`. | Up to 3 KiB on-chip download; eight required NOPs after SCO; different RAM layout. |

On these devices EB0–EB7 cover `0x000000–0x007FFF` in 4 KiB blocks and EB8 covers
`0x008000–0x01FFFF` in one 96 KiB block. Preserve reset/monitor/resident-loader
blocks. For confirmed SH7058/SH7058S, EB14 covers `0x0C0000–0x0DFFFF` and EB15
covers `0x0E0000–0x0FFFFF`; on SH7059, EB13 covers `0x0C0000–0x0FFFFF` as one
256 KiB block, including neighboring data, checksum table and startup pointers.
No erase target is selected by this RAM test. Any later writer must construct
and verify complete erase blocks from the same ECU's complete original image.

No writer is implemented here. The next gates include exact device qualification,
a supported board-level hardware write-enable path, an independently reviewed RAM
flash algorithm, initialization/exit and watchdog behavior, an exact block plan,
failure handling, stock restore and complete readback on the spare, then actual
interrupted-write recovery. Modified calibration testing and the good ECU follow
those gates. Do not cross-flash ZY87 and ZYJ2 images because their boot code is similar.

References: Renesas [SH7058 hardware manual](https://www.renesas.com/en/document/mah/sh-2e-sh7058-f-ztat-tm-hardware-manual),
section 23, and [SH7058S/SH7059 hardware manual, revision 5](https://www.renesas.com/en/document/mah/sh-2e-sh7059-f-ztattm-sh7058s-f-ztattm-hardware-manual),
SDSR table 20.2 and flash sections 24/25 (especially 24.2.5 and 25.2.5).
Silicon documentation does not establish board-level FWE control or a qualified
Mazda programming procedure.
