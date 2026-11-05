# Adapter status and testing candidates

Hardware support is separate from ROM/profile coverage. For programming, normal
write checks used one L3M6EG000 bench ECU with both adapters. Tactrix also passed
controlled interruption and recovery on that ECU. [Gen 1 flashing guide](FLASHING.md)

| Adapter | SpeedLink status |
| --- | --- |
| Vgate vLinker FS USB | Reader and logger available. Gen 1 writing bench-tested. Recovery available with offline coverage and physical recovery pending. |
| Tactrix Openport 2.0 | Reader and logger available through its installed J2534 driver. Gen 1 writing and controlled recovery bench-tested. |
| Mongoose-Plus ISO/CAN3 | Experimental reader/logger and Gen 1 flashing/recovery. A user reported a successful dump. Physical programming is pending qualification. |

## Mongoose-Plus ISO/CAN3

The experimental option uses the J2534 reader/logger transport. Gen 1
flashing and recovery use the dedicated J2534 programming path.

1. Install the manufacturer's Mongoose-Plus ISO/CAN3 Windows driver, including
   its 32-bit J2534-1 04.04 component. Close other diagnostic applications.
2. In **Connection**, select **Mongoose-Plus ISO/CAN3 (experimental)**, then
   **Refresh devices** and choose the registered Mongoose driver. No COM port
   or serial baud selection is needed.
3. With ignition on and the engine stopped, select **Connect & identify**.
   Record the displayed adapter, firmware, driver/API versions and calibration.
4. If identification succeeds, follow the [ROM-dump workflow](DUMP_A_ROM.md)
   for **Read & verify backup**. Keep the complete operation folder privately.
   Success requires two matching full 1 MiB reads.
5. Disconnect and reconnect before testing the [logger](LOGGER.md). Load the
   matching current ROM, start with **Essentials**, record a short CSV and note
   the measured scan rate and any rejected channels or interruptions.
6. For Gen 1 programming, disconnect and open **Flash & Recover** in the
   sidebar. Select the registered experimental Mongoose entry and follow the
   [flashing guide](FLASHING.md).

The picker accepts registered Mongoose-Plus ISO/CAN3 / ISO3 entries that advertise
ISO15765. This adapter has its own list; Openport/Tactrix entries remain on their
existing path. Connection records retain the actual driver name, experimental
status, firmware, DLL version and API version. SpeedLink loads registered
manufacturer libraries, never a DLL supplied with a ROM file.

For the existing Windows x86 app, qualification requires a compatible 32-bit
J2534-1 04.04 driver exposing ISO 15765. A 05.00-only entry is not interchangeable
with the existing binding. Use the manufacturer's driver package; drivers are
not bundled with SpeedLink.

If no driver appears, confirm that the manufacturer's 32-bit 04.04 driver is
installed and registered, then refresh. If connection or reading fails, save
the exact error and stop that attempt. Reconnect for a fresh session; uncertain
requests are not automatically resent.

### Report a test result

Use **Connection → Export support report…** and attach the ZIP to the
[problem-report form](https://github.com/mrphyslaw/SpeedLink/issues/new?template=bug-report.yml).
Include the SpeedLink version, Windows version, exact adapter
model/connection type, registered driver name, firmware and driver/API versions.
State which steps passed: identification, both full reads, backup verification,
then logging duration, channel count and measured scan rate. Include the exact
error and last successful step for failures. Remove VINs and personal paths;
keep raw captures and the full backup private unless a private route is agreed.

## Qualification checklist

1. Record the exact model, connection type, firmware, driver/API versions and
   registered driver name. Confirm DLL architecture and ISO 15765 capability.
2. Check library loading, version reporting, open/close and cleanup without
   connecting an ECU. Preserve existing driver selection behavior.
3. On the isolated bench, check 500 kbit/s, 11-bit CAN on the standard OBD pair,
   request/response IDs 0x7E0/0x7E8, flow-control filtering and frame padding.
4. Exercise identification and short reads, then full-size multi-frame replies
   and two matching 1 MiB ROM reads. Check cancellation, negative/pending replies,
   timeouts, USB removal/reconnection and fresh-session recovery.
5. Exercise sustained logger requests and recording through the same transport.
   Measure rates and response timing; do not assume another adapter's settings.
6. Obtain real-ECU reader/logger acceptance before describing that combination
   as vehicle validated. Adapter failures must not trigger automatic resends.

Physical Mongoose logging, Gen 1 programming and recovery still need
qualification. The experimental programming option covers the ISO/CAN3 or ISO3
registration.

Manufacturer references: [driver downloads](https://opusivs-au.com/support/downloads/)
and [J2534 driver discovery](https://opusivs-au.com/support/oem-customer-support/how-to-program-with-opus-ivss-passthru-sae-j2534-dll/discovery-of-the-dll/).

## Reporting a problem

Diagnostic capture starts automatically with **Connect & identify**, including
failed driver opens. On **Connection**, choose **Export support report…**, select
the session with the problem and save its ZIP. This also works after restarting
without reconnecting. Follow the [reporting guide](TROUBLESHOOTING.md) and attach
the ZIP to the [problem-report form](https://github.com/mrphyslaw/SpeedLink/issues/new?template=bug-report.yml).
The report includes requests, timing, errors and factory security access; personal
information is redacted and ROM bytes remain in your backup files. Review the ZIP
before sharing.
