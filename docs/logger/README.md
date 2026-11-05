# SpeedLink logger

Choose engine parameters, watch live values and record a CSV for later review.
The logger supports nine Gen 1 profiles with 33–38 parameters each, plus the
experimental L3CGEE000 Gen 2 profile with 20. Reader/logger adapter paths include
Vgate vLinker FS USB and Tactrix Openport 2.0, with Mongoose-Plus ISO/CAN3
available for experimental testing. The logger uses the stock ROM interface.
See the [adapter matrix](../ADAPTERS.md) and [Gen 2 scope](../GEN2.md).

## Live data and recording

![SpeedLink logger with parameter selection, values, display units, measured sample rates and synchronized charts](images/live-logger.png)

Select the parameters you need, choose display units and leave **Record CSV**
checked to capture the session. Use **Plot** to select up to six synchronized
charts for logged parameters.
The **Hz** column reports each channel's measured rate.

This screenshot uses simulated values and rates. A separate powered L3M6EG000
bench ECU passed read-only queries and recording through vLinker FS and Tactrix.
[Measured performance and limits](../LOGGER_PERFORMANCE.md)

## Review a saved log

![SpeedLink recorded-log viewer with channel selection and synchronized engine speed, boost, lambda and fuel-pressure plots](images/recorded-log.png)

Choose **Recordings** in the left navigation to open a recording without an
adapter. Select up to six channels, move
the shared cursor to inspect readings, and zoom into part of the run. Missing
readings appear as gaps. This screenshot uses the bundled synthetic example log.

## First recording

1. Connect your adapter and select **Connect & identify**.
2. Open **Logger → Load current ECU ROM…** and choose the full ROM backup
   currently installed in the ECU. Its calibration ID must match.
3. Choose **Essentials** or select individual parameters and display units.
4. Leave **Record CSV** checked, select **Start logging**, then **Stop** to finish.
5. Use **Recordings → Open recording…** to open the recording's `log.csv`.

Recordings are stored under `data/logs` beside the EXE. Keep the entire recording
folder when sharing a session for troubleshooting. Fewer selected parameters
allow faster updates. Leave **Polling → Max** selected to avoid a scan-rate
wait. Optional numeric limits reduce bus traffic or recording volume; achieved
rates depend on the parameters, transport and ECU. See
[measured performance](../LOGGER_PERFORMANCE.md) for rates and validation limits.

Logging can run with the engine running. ROM backup reads require the engine
stopped; disconnect and reconnect after a backup before starting the logger.

See the [full logger guide](../LOGGER.md) for adapter setup, units, rates,
recording files and troubleshooting. Check [profile coverage](../../definitions/logging/All_ROM_Coverage.md)
before connecting an ECU. Unknown ROMs can still be dumped, but logging requires
a supported profile.

[Documentation index](../README.md) · [ROM editor](../editor/README.md) · [Download SpeedLink](https://github.com/mrphyslaw/SpeedLink/releases)

L3P4EM000 adds 35 parameters checked against its own ROM handlers and MAP
calibration bindings. Its live ECU logging, rates and adapter acceptance still
need testing. Desired fuel rail pressure, fuel injection amount and ambient air
temperature are excluded from this profile.
