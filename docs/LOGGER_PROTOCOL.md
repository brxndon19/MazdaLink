# Stock logger protocol and timing

SpeedLink sends sequential stock read requests over 500 kbit/s ISO-TP, using
CAN IDs 7E0/7E8. Enhanced `22 xxxx` identifiers retain their native data widths
and conversions. On nine independently checked Gen 1 layouts, byte-equivalent
standard Mode 01 RPM, absolute load, spark, MAF and fuel-pressure fields can share
one request. Gen 2 retains its separately established enhanced-identifier path.
No patch, security access or ROM reading/programming session is needed.

## Polling and rates

Selected parameters and the dependencies needed for calculated values form a
sequential polling plan. Shared identifiers are queried once per scan and
decoded into their associated channels. **Polling → Max** inserts no scan-rate
wait. Optional numeric limits cap complete rounds for lower bus load or recording
volume. They do not guarantee a per-channel refresh rate. Screen rendering uses
a bounded display handoff and cannot hold up acquisition or recorded frames.

The logger records request timing, per-channel sample timestamps and achieved
host rates. A request/response rate does not establish how often the ECU
updates its underlying variable. Read-only query and repeated-session acceptance
passed on one L3M6EG000 bench ECU with both wired adapters. Physical sensor
accuracy, running-engine behavior and broader vehicle rates remain pending.

Calculated values require fresh inputs within the configured maximum skew.
Unavailable, malformed, negative, mismatched or stale data remains unavailable;
it is not replaced by a zero reading. Recordings preserve explicit gaps.

## Failures and cancellation

Unsupported identifiers are skipped for the rest of that logging session. Condition rejections
can be queried again on a subsequent normal scan. Malformed responses, timeouts
and adapter failures stop logging without resending the uncertain exchange or
automatically reconnecting. Stop waits for the bounded exchange in progress, then
retains a healthy connection for another logging session. Each Start rechecks ECU
identity and starts fresh sample, rejection and recording state. Disconnect and
reconnect before changing from logging to a ROM backup read.

The vLinker logger validates the entire expected reply, ends its remaining
idle receive wait, consumes the ready prompt and verifies adapter synchronization.
It does not wait out the timeout after a valid complete reply, shorten the
response timeout or repeat the ECU request. Pending replies and partial messages
are not completion. Unchanged values are valid; a stalled receive still expires
old display values. Tactrix driver reads likewise return when data is available.

A completed unsupported standard-batch rejection selects individual enhanced
queries on the next scheduled scan. A malformed or uncertain exchange stops
without fallback or resend. Synthetic native-handler fixtures cover all nine
enabled alias layouts; real acceptance is narrower. See
[performance](LOGGER_PERFORMANCE.md) and [validation](VALIDATION.md) for scope.

## Definition and test evidence

Portable evidence is kept with the logger definitions:

- [Profile coverage and exclusions](../definitions/logging/All_ROM_Coverage.md).
- [All-profile ECU checklist](../definitions/logging/All_ROM_ECU_Checklist.csv).
- [Definition format and native validation scope](../definitions/logging/README.md).
- Per-profile `.evidence.json` and `.vectors.json` files in
  [definitions/logging](../definitions/logging/).

These files describe request/decoder validation and remaining ECU checks.
ROM images, private research paths and external software binaries are excluded
from this repository.

## Future scheduling work

Per-parameter minimum intervals may reduce unnecessary polling of slow signals.
Any scheduler change needs to preserve shared-query deduplication, actual sample
timestamps, fresh calculation inputs, explicit gaps and bounded cancellation.
Transport changes require adapter testing and separate real ECU validation.
