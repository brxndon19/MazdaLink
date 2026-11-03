# Alpha validation scope

## Logging restart and performance — 0.7.0-alpha.8

- Required desktop tests cover all ten profiles: three consecutive recorded
  Start/Stop cycles on one connection, immediate cancellation followed by another
  live-only session, retained ROM/selection/unit/plot choices, separate replayable
  recordings, and explicit disconnect. Failure requires manual reconnect without
  retrying the uncertain request; a changed ECU identity is rejected before new
  logger traffic or recording.
- All 558 core tests passed, including ten-profile mandatory ROM-save integrity,
  exact-length reply completion, fragmented/pending/malformed/no-data cases,
  prompt races, adapter resynchronization, no duplicate ECU requests, completed
  batch rejection, all-profile conversions, preference migration and bounded
  concurrent display handoff. Native alias fixtures cover 45 cases across nine
  Gen 1 reference layouts; Gen 2 is not assigned these aliases.
- Each wired adapter completed eight uncapped ten-second workload phases with
  recording off/on and a three-minute all-33-parameter recording on one powered
  L3M6EG000 bench ECU. More than 25,000 requests combined completed without
  transport failures or stale/skew gaps in completed frames. Both achieved about
  50 single-parameter polls/s or 1.85 complete all-parameter scans/s.
- Actual desktop Max-polling sessions with plots, diagnostics and CSV passed
  three consecutive Start/Stop cycles per adapter for one, six and all 33
  parameters. Each released its device cleanly and all six recordings reopened.
  Traffic was limited to identification and known stock logger queries.
- These results establish query/session behavior on one bench ECU. Physical
  sensor accuracy, ECU-internal source update rates, running-engine behavior and
  the remaining profile/channel vehicle checks are pending. Missing or stale
  values remain explicit gaps. No ECU programming was performed.

See [measured performance](LOGGER_PERFORMANCE.md) for rates and their limits.

## L3M6EG000 integration — 0.7.0-alpha.7

- The finished definition supplies 263 tuning tables and 600 diagnostic controls.
  Its final bank-selection checks distinguish otherwise identical load-limit
  and injection-phase banks. SpeedLink retains those addresses, axes, scaling,
  bits and states; its help uses the established tuner-facing wording and units.
- The actual reference ROM passes editor opening and an independent 863-control
  audit: 21,389 decoded values, 1,683 bit edits, 259 numeric edits and four named
  switches. No-op edits and exports remain byte-identical; edits stay within
  their storage, undo restores bytes, and edited exports verify the native sum.
- The native program sum is independently checked over 0x3F000–0xFFFFF. Every
  save applies correction at 0xFFFF4 and verifies the durable result. Ten-profile
  save-integrity tests cover edited, repeated, no-edit and failed saves.
- Only the established 128-byte per-ECU identification record is normalized for
  the L3M6EG000 boot fingerprint. Synthetic alternate records remain compatible;
  adjacent boot mutations are rejected, and saves preserve the record exactly.
  Other profiles retain their existing boot policy. Short and complete known
  calibration IDs match; unknown builds and unsupported program changes do not.
- The logger executes bounded native encoders and identifier-copy paths with
  synthetic RAM: 33 parameters, 30 requests, 247 vectors and 203 independent
  scalar checks. Native status construction does not encode the neutral bit;
  that channel is unavailable. Actual-ROM logger binding and MAP calibration
  checks pass. Sensor generation, scheduler/peripheral behavior and actual
  vehicle values/rates are outside these checks.
- The core suite passes 522 cases and 75 required regressions. Local desktop
  checks cover the new profile's editor, import, navigation, small maps, map
  windows, projects, unsupported-file recovery, 1,726 every-table axis views,
  logger recording and connection failure. Protected checks run the expanded
  all-profile matrix before publication.
- The source capture has two matching hardware ROM reads through vLinker FS.
  This integration used offline tests only: no adapters were opened, no CAN
  traffic was sent, and no ECU was written. Real-ECU logger acceptance and
  physical readings/rates remain pending. ECU programming stays disabled.

## VIN and chassis identification — 0.7.0-alpha.6

- Standard 17-character VINs retain their validation. Recognized short chassis
  numbers accept trailing spaces/nulls, are displayed as Chassis ID, and remain
  separate from VINs in backup metadata. Arbitrary/malformed identifiers and
  malformed response headers remain rejected.
- Identification regressions cover both adapter response paths, normalization,
  pre-read identity changes, unknown-calibration two-pass backups, logger-profile
  binding and support-export redaction. Chassis numbers do not authorize another
  calibration's logging addresses or editor definition.
- Fifteen offline read-progress desktop cases include chassis-based Gen 1 and
  Gen 2 success cases, standard VINs, cancellation, malformed/short replies,
  mismatched passes, preflight failure and unknown Gen 2 protocol selection.
- The patched reader identified one real bench ECU and confirmed zero RPM
  through the vLinker FS and Tactrix, sending only Mode 09 identification and
  Mode 01 RPM queries. No ECU programming, security unlock, ROM download or
  live logger test was performed. Its ROM layout remains unestablished.

## Table-axis alignment — 0.7.0-alpha.5

- Every bundled map/control is opened at two window sizes: 7,522 entries across
  nine profiles, for 15,044 measured views. Numeric axis titles are checked
  against rendered headers and rows; curves retain their direct column labels,
  and single values and switches do not acquire irrelevant axis bands.
- Another 2,216 table-shape/view/zoom combinations check both scroll ends.
  Coverage includes all four table/graph layouts, graph-only restoration,
  fitting column resizing, independent map resizing, detached windows and
  100%, 125% and 150% layout scales. Empty space and clipped parts of edge
  cells are excluded from title positioning. Full titles remain available on hover.
- The axis matrix runs in the required C# workflow. It uses synthetic fixtures
  and verifies that layout, scrolling and moving windows leave ROM bytes intact.
  This is desktop presentation coverage; it makes no new hardware claim.
- All 465 unit cases (including 68 baseline cases), mandatory save-integrity
  checks for nine profiles, eight retained layout workflows, nine editor
  workflows and nine map-window workflows (1,611 assertions) pass locally.

## Application navigation — 0.7.0-alpha.4

- File, Edit, View, Tools and Help remain available in all six workspaces.
  Menus retain readable popup surfaces, disabled states, shortcuts and keyboard
  activation independently of Windows menu colors.
- Navigation starts with icons and labels. The sidebar button and View menu
  control the same setting; explicit compact and labeled preferences survive
  restart. Existing stored preferences remain respected.
- Nine synthetic navigation workflows verify loaded-ROM command states,
  Undo/Redo, edit preservation across workspaces and minimum window sizes.
  Nine embedded editor workflows and eight retained layout workflows pass.
- The wider sidebar exposed unnecessary scrolling in an eight-column compact
  table at 1,024 pixels. Compact auto-fit now uses measured readable minimums
  instead of requiring 64 pixels per column when space is tighter.
- All 465 unit cases, 68 baseline regressions and mandatory save-integrity
  checks for all nine editor profiles pass. These are offline desktop results;
  no new hardware or ECU acceptance is claimed. Programming remains disabled.

## .NET security maintenance — 0.6.0-alpha.2

- SDK 10.0.403 and bundled runtime 10.0.14 are pinned together. Dependency locks
  are refreshed with that SDK; locked restore stays enabled.
- Release checks cover all 426 unit cases and 62 baseline regressions, the full
  offline desktop suite, and standalone startup, menus, support export and replay.
- Mandatory ROM-save tests cover every supported definition using independent
  checksum sums: edits, repeated saves, no-edit correction and native rejection.
  CI fails if either required save test is missing for any bundled ROM. Private
  checks also exercise all nine actual ROM files without modifying their sources.
- The standalone startup report must show the pinned runtime in a 32-bit process.
  About also displays the actual running runtime. All nine ROM profiles remain
  bundled; definitions, protocol logic and integrity rules are unchanged.
- This is a self-contained runtime rebuild. Installing a system runtime does not
  patch an older SpeedLink EXE. Keep the existing data folder when replacing it.
- No new physical adapter or ECU result is claimed. Gen 2 real ECU acceptance,
  sensor values and rates remain pending. ECU programming remains disabled.

## Experimental Gen 2 support — 0.6.0-alpha.1

- Nine editor/logger profiles. L3CGEE000 adds 287 tuning entries and 155 DTC
  reporting controls, while 20 reviewed reference controls remain excluded.
- Reference-ROM verification checks all 24,028 table cells and 4,334 axis labels,
  568 numeric edit/save/restore operations and 626 switch-state operations.
  A no-edit save is identical and the source ROM remains unchanged.
- The Gen 2 reader uses its own session/security level and ALFID memory-read
  format. Native bounds limit blocks to 1,023 bytes in an exact 1 MiB range.
  Offline tests check both passes, tail bounds, response lengths, cancellation,
  mismatches, storage failures, native J2534 receive handling and support export.
- The 20-parameter logger uses 19 native identifiers. Original identifier lookup,
  payload callbacks and physical encoder slices pass 379 instruction checks;
  77 decoding/calculation vectors cover all offered channels. Ambient temperature
  is factory disabled in the checked lookup. Other unestablished baseline
  channels remain unavailable, without assuming their signals are absent.
- The suite passes 407 unit cases, including all 62 baseline regressions. Nine
  editor, nine Workbench, nine definition-import, 18 logger and 13 read-progress
  desktop cases cover the integrated catalog. Two extra logger desktop cases
  bind the actual Gen 2 reference ROM and test recording and failure handling.
- Every Gen 2 export validates native code and markers without guessed repair.
  Editable calibration bytes are outside the code sum. Additional calibration
  integrity rules and flashing compatibility are not established.
- This update has no new physical Gen 2 adapter or ECU test. Real ECU acceptance,
  measured sensor values and rates remain pending. Earlier adapter results below
  retain their original scope. ECU programming remains disabled.

## Complete ECU calibration identities — 0.5.5-alpha.5

- Regression cases reproduce the former rejection of complete ECU identities
  for all eight profiles, including `L3P4EM000L3T6051`.
- Known short and complete IDs now select the same logger. Unknown build
  suffixes, truncated IDs, different profiles and ambiguous IDs remain rejected.
- The complete suite passes 369 unit cases. Sixteen offline desktop workflows
  bind the actual profile ROM with its complete ECU identity and exercise
  recording/stop and connection-failure handling. Source ROMs remain unchanged.
- These checks use simulated live replies. Real ECU logger values and rates
  remain pending; no new physical adapter or ECU test is claimed.

## L3P4EM000 support — 0.5.5-alpha.4

- Eight bundled editor/logger profiles. L3P4EM000 adds 895 maps and controls,
  35 logger parameters, its identity/boot/program guards and checksum layout.
- Its original bounded logger encoders and response-copy routine passed
  277 vectors and 217 independent scalar checks with synthetic RAM.
- The complete suite has 361 unit cases; all eight profiles are included
  in editor, layout, Workbench, import and logger UI checks.
- Existing physical bench results below cover the original seven profiles.
  No new adapter traffic or ECU logging is claimed for L3P4EM000.

## C# logger and saved-log viewer — v0.3.0-alpha.2

Date: 2026-10-19. Live programming remains disabled. Real ECU logging has not
been tested; all 258 channel/profile combinations retain their ECU-pending status.

The local offline suite passed 165 C# tests, including all 52 original parity
cases. The unchanged seven logger profiles retain their 2,030 native-derived
vectors. Fourteen desktop recording/failure workflows cover all seven profiles.
Replay checks cover CSV quoting and incomplete tails, malformed data, sample
ages, gaps, extrema preservation and round trips from every profile's recorder.
The viewer was exercised and visually inspected at normal and minimum size.
Seven developer ECU-peer parser regression cases also passed.

Physical logger qualification used the vLinker FS and Tactrix Openport 2.0 on an
isolated, powered, terminated bench. Each adapter acted as the reader while the
other carried simulated ECU replies. This exercised all seven profiles in both
directions, unsupported identifiers, transient negative responses, delayed
replies, short replies and missing replies. Each direction completed a sustained
1,020-query run. The actual desktop connect/start/stop controls and saved recording
were also exercised with each adapter. Raw client/peer traces and recorded values
were checked independently. Exploratory fixture failures were retained and their
causes corrected before successful repeat runs.

Physical USB interruption/reconnect checks passed for both readers. Each stopped
without retrying and retained the partial recording, then completed 340 fresh
queries after reconnection with clean shutdown. The final independent audit
matched 5,402 queries and decoded 4,443 recorded values. Cleanup warnings caused
by USB removal remain recorded; the fresh connections closed without warnings.

The saved-log viewer opens existing SpeedLink wide CSV files without an adapter.
Its checks verify synchronized selection, cursor/zoom, six-plot limit, bounded
loading, visible gaps, stale-value handling and unchanged source files. The single
EXE receives separate startup/resource checks for all seven ROM bindings and a
saved-recording viewer check. CI repeats offline checks; physical bench tests
are local and cannot run on a GitHub-hosted runner.

Bench rates describe the adapters and test peer. They do not establish actual
ECU response times, variable refresh rates, vehicle compatibility or road-use
reliability. Native code evidence does not establish sensor accuracy on a vehicle.
See [logger protocol research](LOGGER_PROTOCOL.md) and
[per-ROM pending ECU checks](../definitions/logging/All_ROM_ECU_Checklist.csv).

For current editor and Workbench checks, see [C# validation](../dotnet/docs/Validation.md).
