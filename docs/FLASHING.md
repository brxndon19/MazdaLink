# Gen 1 flashing and recovery

SpeedLink supports writing and stock recovery for all nine bundled **Gen 1**
ROM layouts. **Gen 2 flashing is planned for an upcoming release** and remains
disabled in this build. See the [supported ROM list](SUPPORTED_ROMS.md) and the
[adapter matrix](ADAPTERS.md) before starting.

## Before writing

1. Use a **Tactrix Openport 2.0** with its installed 32-bit J2534 driver or a
   wired **vLinker FS**. **Mongoose-Plus ISO/CAN3** or **ISO3** is also available
   as an experimental option with its installed 32-bit J2534 04.04 driver.
   Close other applications using the adapter. Switch the ignition **ON** with
   the engine **OFF** and connect a stable battery support supply. SpeedLink
   checks for 12–15 V at the adapter.
2. If this ECU has no saved verified backup, identify it in **Connection** and
   run **Read & verify backup** with ignition on and engine off. Keep the
   full backup directory, including `rom.bin`, its manifest, journal and both
   read captures. Use a backup from the same physical ECU, captured while it
   had the stock ROM if you want a factory restore. Recovery restores the
   exact contents of that backup.
3. For an edited ROM, open that backup in **Editor**, make the changes and
   choose **Save copy…**. SpeedLink corrects and verifies the ROM checksum
   before export. Keep the original backup unchanged.

## Write a ROM

1. Open **Flash & Recover** below **Connection** and choose **Write ROM**.
   After a verified Gen 1 backup, **Open in Flash & Recover** carries it over
   from Connection. Disconnect any active Connection session before programming.
2. Select the adapter and the ROM to write, then choose **Identify ECU & find
   backup**. SpeedLink matches the VIN or chassis ID and calibration and shows
   the verified original it found. The target ROM can be moved or renamed.
   **Find backup folder…** adds an older SpeedLink installation, its data
   folder, or a full original backup folder. SpeedLink remembers the location
   across versions. Use an existing verified backup when one is available.
3. Review the match and confirm the setup before choosing **Write ROM**.
   SpeedLink checks the bundled layout, checksum, program signature and
   differences allowed by the definition.
4. SpeedLink matches the connected ECU's identity again, checks stopped-engine RPM,
   reads the complete calibration ID, and compares the retained first 32 KiB
   against the backup before erase. It rewrites the complete 992 KiB
   application and preserves the retained boot and identity bytes.
5. Keep power and USB connected through erase, transfer and reset. Watch the
   current stage and acknowledged byte count. **Stop** is available before
   erase and during readback. After reset, the default is two full readbacks.
   For normal writes, **After writing** also offers **One full readback** and
   **Skip readback**. Each selected pass must match the intended 1 MiB image.
6. Wait for the result. With readbacks, **Complete** means the captured ROM
   matched the intended image. With **Skip readback**, SpeedLink checks ECU
   identification after reset and reports **Write acknowledged · readback skipped**.
   The written bytes are unverified. You can verify them later through Connection.
   On the tested ECU, each full pass took about 25 minutes.

## Recover an interrupted write

1. Keep the failed run folder and the same original verified stock backup.
   Record the stage at which the write stopped. If SpeedLink says
   **Verification required**, the reset was acknowledged. Reconnect in
   **Connection**, read a verified backup and compare it with the intended
   image before using the ECU.
2. For **Recovery required**, keep the same physical ECU attached. Once the
   failed operation has ended and the adapter is closed, turn the ignition off
   and wait for the ECU to shut down, then turn the ignition back on without
   starting the engine. Open **Flash & Recover → Recover interrupted write**.
3. SpeedLink selects the saved interrupted run when there is only one.
   If several runs are listed, choose by date and calibration. **Browse older
   run…** accepts the original run folder.
4. Select the adapter and choose **Check ECU state**. Once recovery mode is
   confirmed, SpeedLink looks up the exact verified backup using the saved
   run's recorded hash and identity. **Normal mode** and **ECU state
   unconfirmed** keep recovery disabled. Confirm the ECU is the same physical
   unit, then choose **Recover stock**.
5. SpeedLink rechecks the ECU before erase. It requires positive boot-session responses and explicit
   service-not-supported responses for normal application probes. Silence
   alone cannot arm recovery. A qualified recovery starts a fresh erase and
   writes the full original application from block one. It requires two exact
   full readbacks after reset.

Recovery depends on Mazda's retained CAN boot server. If it cannot be reached
or the backup and journal fail validation, keep the files and use
[troubleshooting](TROUBLESHOOTING.md). The run folders contain ROMs and local ECU
identifiers. Keep them private when reporting a problem.

## Validation scope

The nine layouts were checked in native-handler replay and simulated workflows.
Uninterrupted stock and edited writes were verified on one L3M6EG000 bench ECU
with both adapters. A controlled interruption after one acknowledged block and
stock restore were exercised on that same ECU with Tactrix. Mongoose
flashing and recovery have synthetic coverage and still need physical tests.
Broader physical profile coverage, vLinker FS interruption recovery, power loss during erase and
in-vehicle programming remain pending. See [validation](VALIDATION.md).
