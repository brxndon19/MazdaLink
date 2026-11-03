# Logger performance

Leave **Polling** on **Max** to collect data as quickly as completed replies allow.
There is no scan-rate delay in this mode. A numeric limit is optional when you
want less bus traffic or smaller recordings. Older settings migrate to Max while
preserving channel selections, units, plots and recording choices.

## Measured bench results

The following results used one powered L3M6EG000 bench ECU, Windows, the installed
manufacturer drivers, and continuous read-only polling. Each adapter ran eight
ten-second workloads with recording off/on, followed by a three-minute recording
with all 33 parameters selected. Together they completed more than 25,000 requests
without transport failures or stale completed-frame readings. This does not
validate physical sensor accuracy or running-engine behavior.

| Selection | vLinker FS USB | Tactrix Openport 2.0 |
| --- | --- | --- |
| RPM only | about 50 polls/s | about 50 polls/s |
| Six tuning parameters | about 8.0–8.3 complete scans/s | about 8.3 complete scans/s |
| Eleven-parameter Essentials selection | about 5.2 complete scans/s | about 5.2 complete scans/s |
| All 33 parameters, sustained recording | about 1.85 complete scans/s | about 1.85 complete scans/s |

A scan covers the selected set and its required calculated-channel inputs.
Requests/s, scans/s and per-channel polls/s are different quantities. Shared
identifiers and verified standard batches reduce the number of requests needed.
The **Hz** column reports observed per-channel timing; fewer selected parameters
usually allow each to be polled more frequently.

These are measured results for this ECU and host, not promises for every vehicle,
adapter firmware or ROM. A reply may repeat an unchanged value: receive timing
does not establish the ECU's internal sensor/calculation update rate.

## What changed

- The vLinker logger recognizes a complete, exact-length, identifier-matched
  reply before the adapter's idle receive timeout. It ends only the remaining
  idle wait, consumes the ready prompt and verifies adapter synchronization.
  It does not shorten the response timeout or resend the ECU request. Missing,
  truncated, mismatched or malformed replies stop the session.
- Read-only standard batches combine byte-equivalent RPM, absolute-load, spark,
  MAF and fuel-pressure data on nine independently checked Gen 1 layouts.
  Enhanced channels, MAP voltage/calibration and numeric precision are retained.
  Gen 2 keeps its separately established enhanced-identifier path. A completed
  unsupported-batch rejection selects individual queries on the next scheduled
  scan; uncertain exchanges stop without fallback or resend.
- A bounded display handoff separates polling and recording from screen redraws.
  Display refresh does not cap acquisition. A delayed screen can discard old
  display updates while recordings retain the captured frames. Values become
  explicitly stale when their freshness limit expires, including during a
  stalled receive.
- Recording UTC and elapsed timestamps use the same clock capture, preserving
  a consistent recording origin even when Windows preempts a worker thread.
- Tactrix reads continue using bounded driver waits that return when data arrives.
  Testing shorter 1 ms and 5 ms driver waits did not improve its measured response
  cadence, so the existing wait policy is retained.

Use [the logger guide](LOGGER.md) for setup and recording, and
[validation scope](VALIDATION.md) for tested behavior and remaining checks.
