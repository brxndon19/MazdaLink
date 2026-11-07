# Experimental Gen 2 support

L3CGEE000 adds a Gen 2 editor definition and a stock-ROM logger. Reading uses
a separate Gen 2 application protocol. Real ECU acceptance, measured sensor
values and update rates remain unverified. Gen 2 flashing is planned for an
upcoming release. [The current flashing workflow](FLASHING.md) supports Gen 1 only.

## Read a ROM

1. Connect and identify with the ignition on and engine stopped.
2. Known short or complete calibration IDs select their reader automatically.
   For an unknown Gen 2 calibration, select **Gen 2 read protocol (experimental)**
   in the adapter settings. This selects a compatible protocol; it does
   not establish support for every Gen 2 ECU.
3. Start the read and leave the adapter connected through both complete passes.
   The progress panel shows each pass, bytes and elapsed time.
4. A verified backup requires two identical 1 MiB captures and durable save
   verification. Partial captures and the journal remain available after failure
   or cancellation. Reconnect before another attempt; requests are not retried.
5. Use **Export support report…** for diagnostics. The report includes session,
   security and read-address context while omitting ROM payload bytes.

The Gen 2 reader requests extended session `10 03`, read security `27 03/04`
and bounded memory reads using `23 24`, a four-byte address and a two-byte
length. Each block is at most 1,023 bytes and stays within `0x000000–0x0FFFFF`.
It does not upload a driver, enter programming session, erase or write memory.
An editor definition is optional for dumping. Unknown variants do not acquire
logging addresses or checksum rules from L3CGEE000.

## Editor and integrity

The L3CGEE000 definition has 293 tuning entries and 155 DTC reporting controls.
Six MAP scaling controls cover the shared control and diagnostic conversion and
the separate logging conversion. Both use sensor voltage times two multipliers,
then subtract the offset in kPa. Keep the corresponding values matched. The
diagnostic MAP value still has a 255 kPa ceiling.
DTC reporting options do not guarantee that fault detection or failsafes stop,
and they do not force readiness complete. Year, market and factory provenance
of the reference image have not been established.

Every export validates the native code sum, four application markers and the
recognized stock program layout. The wrapping big-endian 32-bit sum covers
`[0x004000, 0x0C0000)`, excluding `[0x01C000, 0x020000)`, and must equal
`0x5AA55AA5`. The six MAP coefficients are inside this sum. SpeedLink
automatically updates the existing correction word at `0x03FFC0` when needed.
The guarded program layout, checksum routines and four markers must match.
Calibration tables above `0x0C0000` lie outside the native sum. Additional
calibration integrity rules remain unestablished.

## Logger

Load the matching current ECU backup, then select parameters and start logging.
Short `L3CGEE000` and complete `L3CGEE000L3T6060` calibration IDs select the same
profile. Other build suffixes do not match automatically.

The first set offers 20 parameters: RPM, speed, timing, coolant and intake
temperature, airflow, absolute and relative throttle position, pedal position,
fuel trims, manifold and barometric pressure, calculated boost, rail pressure,
measured and requested lambda, absolute and calculated load, and module voltage.
Only verified native identifiers are polled. Ambient temperature is factory
disabled in the checked identifier lookup; other unestablished baseline
channels are listed as unavailable in the definition.

Manifold pressure uses a stock diagnostic byte with 1 kPa resolution and a
255 kPa ceiling. Boost is manifold minus barometric pressure, requiring fresh
inputs. Unsupported, missing, stale or malformed responses never become zero
measurements. Requested polling rates are targets, not verified ECU rates.
Logging uses normal application identifiers without security or session changes.

## Verification limits

Native instruction checks establish identifier lookup, payload packing,
physical conversion arithmetic and read bounds. Offline tests cover two-pass
backups, cancellation/failure, identity matching, editor saves, logger decoding
and recording. Reference-ROM checks compare every included cell and axis.
Desktop checks exercise the application controls. These do not establish
vehicle acceptance, sensor accuracy, ignition timing, drivability or flashing
compatibility. Keep the original backup and report actual ECU results separately.
