# C# validation

## Gen 1 programming — 0.8.0-alpha.1

- Synthetic tests cover write and stock recovery for all nine bundled Gen 1
  layouts, and reject Gen 2 before opening a programming transport. Fault
  cases exercise preflight, ambiguous acknowledgements, cancellation, journal
  failures, readback mismatch and adapter request guards.
- The standalone EXE exercises file validation, arming, progress, locking,
  interruption and recovery through the desktop window with synthetic data.
  See the [hardware results](../../docs/VALIDATION.md) for bench coverage.

## Logger workflow follow-up checks — 0.7.0-alpha.9

- The latest-recording shortcut is exercised across every logger profile after
  consecutive recorded sessions. Tests also cover missing-file recovery,
  retained playback after live-only logging, and locking during acquisition.
- Read-progress tests cover cancellation during an exchange and final progress
  still queued on the dispatcher. Displayed stopped bytes must match the durable
  capture; callbacks from an older operation cannot alter a new read. Cancellation
  still leaves an unverified partial capture and never seals a backup.
- An accelerated 33-channel, six-chart desktop workload checks retained chart
  views, bounded histories and selection changes, quiet bindings for unchanged
  readings, flat values and explicit gaps. Allocation and post-collection managed
  memory are measured in the test harness; production never forces collection.
  The same workload allocated about 67% fewer bytes per update after the renderer
  changes on the test host. This is not a guarantee for every host or workload.
- These changes leave ECU requests, polling policy, raw capture bytes and
  mandatory editor-save integrity unchanged.

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

See [measured performance](../../docs/LOGGER_PERFORMANCE.md) for rates and their limits.

## Independent map sizing — 0.7.0-alpha.3

- Core tests cover bounded per-pane coordinates, all edge/corner calculations,
  overlap, movement, readable minimums and legacy layout migration.
- Required desktop workflows for all nine profiles grow a third map into empty
  space without changing either upper map, resize every pane independently,
  allow overlap, bring covered maps forward, and move a pane without changing
  its neighbors. The visible corner grip is checked through WPF pointer hit testing.
- Checks retain exact bounds through canceled drags, other maps opening/closing,
  native undock/dock, smaller workspaces and calibration reopening. Mixed scalar
  and table panes receive the same coverage. Presentation actions preserve ROM
  bytes; all existing save-integrity and desktop regression gates remain required.
- These are offline checks; no new vehicle or hardware validation is claimed.

## ROM-open recovery and compact values — 0.7.0-alpha.2

- Each of the nine editor profiles exercises a program-modified file that passes
  native integrity but fails stock program identification, an unknown calibration,
  a truncated file and an invalid boot. Rejection is checked in an empty editor
  and with unsaved edits, undo history, selections and a detached map. Tests then
  undo, redo, save with mandatory integrity, open a supported calibration-only
  edited file, and reopen the original. Unexpected programming exceptions still
  propagate to fatal crash reporting.
- Nine map-window workflows require five simultaneous value/state cards to fit
  without internal scrolling at 1280×850 and 1024×768. Default cards are at most
  302 pixels wide and rows at most 150 pixels high; inputs use standard 13-DIP
  text. Tests exercise wrapping when the browser opens, row/column resizing,
  calibration-bound size restoration, full Map info, conditional Apply/Cancel,
  invalid input and named-state undo. Core coverage bounds saved card widths and
  verifies compatibility with layouts saved before card support.
- These checks use synthetic fixtures and no adapter or vehicle. Program-layout
  protections, mandatory ROM-save integrity and disabled ECU programming remain.

## Resizable map windows — 0.7.0-alpha.1

- Required Core coverage exercises measured cell minimums across table shapes,
  viewport sizes and zoom limits, fixed spacing, bounded preference parsing and
  independent per-calibration layout persistence. Resizing never chooses a font.
- Nine synthetic Windows workflows exercise multiple maps, real width/height
  dividers, expand/restore, native detachable windows, shared edits and undo,
  invalid pending input, grid/curve/state/scalar controls, close/dock cycles,
  workspace navigation and fresh-window restoration of maps, zoom and selection.
  Each profile requires 57 assertions, including visible data at the native
  window's minimum size and global commands following the active map.
- The existing editor, logger, backup, recording, diagnostic, crash and standalone
  checks remain required. Every supported ROM also passes mandatory save-integrity
  tests. Public CI artifacts contain synthetic reports and screenshots, not ROMs.
- These are desktop and offline checks; no new adapter or real ECU validation is
  claimed. ECU programming remains disabled.

## Lifecycle reliability — 0.6.0-alpha.6

- 443 Core cases, including version-one logger preference migration, independent
  profile choices and full default-size Windows diagnostic rotation/export.
- Nine synthetic desktop workflows exercise workspace-scoped floating panels,
  empty/reopened maps, file access errors, invalid AFR correction without losing
  the connection, post-read logger guidance, start/stop recording, immediately
  updated stopped charts, pending/stale/rejected display states, failed recording
  replacement, Gen 2 replay defaults and fresh-process preference restoration.
- Fresh launches restore the active profile and every channel's selection, unit
  and plot choice for all eight Gen 1 profiles and Gen 2, plus the global scan
  rate, stoichiometric AFR and CSV choice. Restored choices never bind an ECU ROM.
- Developer diagnostics tests export 20,000 events and separately force an export
  destination failure. Reports retain the primary result and current build identity.
- Full existing desktop, standalone, all-profile ROM-save integrity and exact
  source-package checks remain required. These new checks use synthetic data and
  do not establish real ECU acceptance, sensor accuracy or hardware logging rates.

## Map browser access — 0.6.0-alpha.5

- Desktop regression coverage opens all nine ROM profiles after restoring saved
  hidden and floating browsers, including minimized floating windows.
- The Maps button and no-map selection prompt restore browser access. Closing and
  reopening the final map preserves unsaved ROM edits.
- Required release checks retain the unit suite, every-ROM save-integrity gates,
  the complete desktop suite and standalone EXE/source-package verification.

## Complete source packaging — 0.6.0-alpha.4

- Source packaging runs for PR/main checks as well as release tags. Git archives
  run from the repository root, regardless of the invoking directory.
- The archive verifier compares every file path and Git blob hash with the exact
  source commit. An incomplete subdirectory archive is explicitly rejected.
- Downloaded release source is independently compared with the merged Git tree.

## Reliability and release maintenance — 0.6.0-alpha.3

- 436 C# cases, including durable crash storage, personal-data redaction, fallback,
  size/retention limits, acknowledgment, safe export and malformed-report rejection.
- Standalone crash check exercises the actual UI exception boundary in a disposable
  process, then verifies next-process review, acknowledgment and ZIP export.
- The full offline desktop suite and every-profile mandatory ROM-save gate remain
  required. No adapter transmissions or real ECU results are added by this release.
- Release package tests reject changed bytes, wrong source/tag, missing hashes and
  extra files. Runtime-update tests exclude preview, downgrade, major and feature-
  band migrations while coupling SDK/runtime versions from one release record.
- GitHub retains only synthetic reports/screenshots, not generated ROM images.
  Release workflow validation tests the same EXE that publishing uploads.

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

## Backup progress — 0.5.5-alpha.3

- All 346 unit cases pass. Added progress cases cover pass byte counts, monotonic
  transfer progress, successful completion after storage closes, failed blocks,
  mismatched captures, failed ROM saving, failed journal completion and stopping
  after the final block. Progress adds no ECU requests or retries.
- Six native UI cases use the actual Read and Stop buttons with an offline peer:
  success, cancellation, timeout, short reply, mismatch and preflight failure.
  Checks cover both passes, measured remaining time, resetting a new operation,
  locked controls, late callbacks, completion state and minimum-window layout.
- Documentation screenshots are rendered by these UI cases. Their synthetic
  transfer times are not real-ECU throughput measurements. Transport and adapter
  setup code are unchanged; real-ECU and Mongoose acceptance remain pending.

## J2534 receive compatibility — 0.5.5-alpha.2

- All 340 unit cases pass. Thirty native-receive cases exercise messages returned
  with timeout-like statuses, fatal errors, message counts and bounds, complete
  ISO15765 lengths, receive metadata, response deadlines and no request resends.
- The isolated two-adapter bench passed three cases with Tactrix reading from a
  vLinker FS ECU peer: boundary lengths, a full two-pass 1 MiB backup and ten logger
  scans. All 901 exchanges matched the independent peer trace. Support exports
  retained receive metadata and excluded ROM payloads.
- The receive code is unchanged from the tested fix; this release adds its own
  version and updates the download links and bundled reporting guide.
- These tests use simulated ECU replies. Mongoose hardware and real-ECU
  acceptance remain pending; the reported Mongoose timeout needs a new-version
  retest and support report to establish whether it is resolved.

## Diagnostic support reports — 0.5.5-alpha.1

- 310 unit cases pass, including 18 added cases for security-access retention,
  privacy, ROM-byte exclusion, restart persistence, rotation/retention, storage
  failure, damaged journals, driver-open failures and unchanged request counts.
- J2534 diagnostics retain receive/pending/notification counts and driver status.
  A regression case preserves the existing ECU response deadline after a slow
  driver write; request timing is recorded separately.
- Native UI checks open Export support report through its actual button, verify
  connection controls are locked while exporting, select a saved failed session
  offline, save the ZIP and restore controls. The standalone EXE runs the same check.
- Existing menu, seven-profile layout/editor/workbench/import, logger and replay
  suites remain required. The reporting guide is bundled under Help.
- The isolated, powered two-adapter bench on 2026-10-23 passed full two-pass
  1 MiB reads, boundary lengths, short/missing/pending replies, all-38-parameter
  logging and native logger workflows with both vLinker FS and Tactrix as readers.
  Reports retained the failing address and factory seed/key exchange, excluded
  ROM bytes/personal identifiers and matched the independent peer request trace.
  Failed reads issued the affected memory request once and created no verified backup.
- These are simulated ECU replies over physical adapters. No Mazda ECU was used;
  Mongoose hardware and real-ECU acceptance remain pending.

## Compact maps and preview resizing — 0.5.4-alpha.9

- Native editor checks exercise the value field, units, explanation, full stored
  precision, Enter/Escape, numeric validation, storage overflow, baseline reset
  and byte-exact Undo/Redo. Valid drafts commit on map changes; invalid drafts
  stay visible and block saving, map changes and baseline changes.
- Single-state and multi-state controls retain named selections. Each bundled
  small-map shape is checked at 1024/1280 logical window widths for proportional
  bounds, readable rows, unclipped cells and the appropriate preview.
- Native GridSplitter drag events exercise both directions with no snapback.
  Checks cover persisted width, pop-out/docking, window resizing, Fit map reset
  and restoration in a fresh workspace. Resizing preserves ROM bytes.
- Existing full 16×16, long MAF, axis-frame, scrolling, help and checksum checks
  remain required. Core tests cover preview bounds, preference compatibility
  and natural small-map sizes. Definitions and ECU-access code are unchanged.

## Joined axis frame — 0.5.4-alpha.8

- A single outline surrounds the axis bands and table viewport. The row- and
  column-axis bands share its lower-left and upper-right corners.
- Native layout checks verify the frame with no scrollbars, horizontal only,
  vertical only and both. Scrolling checks also exercise a scaled layout.
- Full 16×16 fitting, frozen axes, the final map cell and Map info/F1 access
  retain their existing checks. Definitions and ECU-access code are unchanged.

## Direct map help — 0.5.4-alpha.7

- A labeled Map info button shares the map-tab row. Native layout checks keep it
  visible while retaining full 16×16 fitting at both supported compact sizes.
- Editor checks open help through the button and a routed F1 key event, reopen
  it after closing, and bring the same window back from its minimized state.
- Help updates when changing maps and closes when no map is active. Both entry
  points are disabled or ignored without a selected map. Viewing help preserves
  ROM bytes and table height.

## Scrollbars and table edges — 0.5.4-alpha.6

- Dark scrollbar templates retain the standard track, arrow, page and thumb
  interaction paths. Native editor checks exercise both scroll directions,
  page commands and forward/reverse thumb drags on the stock ignition map.
- Axis bands use the rendered scrollbar sizes and stop before their corners.
  Checks reach the last row/column and verify the final value is unobscured.
- The scrolling checks run at 100% and a 125% layout transform. This exercises
  scaled layout; it is not a claim of physical multi-monitor DPI testing.
- The seven-profile editor/layout/import checks retain full 16×16 fitting,
  frozen axis labels, long MAF editing, unchanged ROM bytes and checksum repair.

## Map help and axis separation — 0.5.4-alpha.5

- Reviewed 6,185 entries across the seven bundled profiles, including 300 distinct
  non-diagnostic map names. Descriptions distinguish targets, limits, thresholds,
  multipliers, signed corrections, rate units and factory activation qualifications.
- Corrected the knock-reference offset's misleading retard-degree label. Native
  lookup and multiply/add instruction slices were checked in all seven baseline
  images, including the older layout's branch around an inline literal pool.
  The 504 arithmetic cases use explicit RAM inputs and a status-return fixture;
  they are not physical knock-sensor or whole-ECU tests.
- A metadata comparison preserves all table names, storage addresses, dimensions,
  axis values, binary types, scaling expressions, switch states and bits.
- Native editor checks verify visible value units with storage details collapsed,
  help updates when switching maps, unchanged ROM bytes, header separators, frozen
  axis alignment, MAF breakpoint separation and complete 16×16 fitting.
- Seven additional C# cases protect critical help/unit contracts. ECU access and
  the mandatory checksum-correction path retain their existing scope.

## Map-mounted 3D axes — 0.5.4-alpha.4

- Blue column-axis, green row-axis and amber value-axis rulers are projected
  from the map's 3D coordinates. Names, units and ticks come from the current
  definition and values. The compact preview uses fewer tick labels.
- Native projection checks compare all three directions with WPF at five
  azimuths and three elevations, in compact and wide/tall floating views.
- The existing 279 C# tests and seven-profile layout, Workbench and import
  workflows remain required, including full 16×16 fitting, long MAF editing,
  linked selection, precision, menu behavior and mandatory checksum exports.
- The VVT example demonstrates the approved labeled surface. Its source ROM
  remains unchanged. No new adapter or real-ECU validation is claimed.

## Surface dragging and experimental Mongoose — 0.5.4-alpha.3

- Horizontal dragging now makes the visible surface follow the pointer.
  Vertical tilt retains its existing direction and limits.
- Seven layout workflows check the camera-facing reference geometry through
  WPF's actual 3D projection. Left, right, up and down movement are checked in
  the compact preview and in wide/tall floating graph windows, along with tilt
  limits, reset, full 16×16 fitting and the existing MAF workflow.
- 279 C# tests pass. Additional cases cover candidate driver names and ISO15765
  capability filtering, preserved Tactrix discovery, actual adapter identity,
  wrong API rejection before CAN connect and blocked programming requests.
- Mongoose-Plus ISO/CAN3 has an experimental J2534 reader/logger option. Its
  registered 32-bit 04.04 driver is selected separately from Tactrix. Startup
  checks select both paths, refresh driver lists and check the visible status.
- No Mongoose hardware or real ECU was accessed. Driver loading, multi-frame
  behavior, full reads, measured logging rates and USB recovery need user tests.

## Editor menu fix — 0.5.4-alpha.2

- File, Edit and View use explicit popup surfaces and item templates, independent
  of Windows menu colors. Desktop checks open each popup with conflicting host
  colors and verify visible labels, shortcut hints, disabled Save and checkmarks.
- Keyboard activation of a view command and Escape dismissal are exercised.
  The same checks run against the published single EXE, including screenshots.
- The existing core, layout, editing, logger, Workbench and import checks remain
  required. ROM storage and ECU access are unchanged.

## Table axis labels — 0.5.4-alpha.1

- 261 C# regression tests remain required, alongside the editor, logger,
  recording, Workbench and definition-import desktop workflows.
- Seven layout workflows check labels against definition metadata, visible
  selection highlighting, rectangular selections and fixed axis-title positions
  while both table scrollbars are in use. Scrolled headers retain highlighting.
- A complete synthetic 16×16 grid and the 3D preview still fit at 1024×700 and
  1280×720 logical window sizes without table scrolling. Long row-axis names
  remain visible beside the breakpoints; labels consume no additional map height.
- The existing 129-point MAF list retains its direct column labels, read-only
  breakpoints, final-point navigation, arithmetic, interpolation and clipboard
  checks. Precision, exact undo and mandatory checksum exports remain covered.
- These are offline UI changes; no new adapter or real-ECU validation is claimed.

## Table preview — 0.5.3-alpha.1

- 261 C# tests pass, including preview sizing and layout persistence.
- Seven desktop layout workflows retain a visible upper-right 3D preview while
  a complete synthetic 16×16 grid fits at both target logical window sizes.
  The same workflows cover linked selection, pop-out/dock behavior, precision,
  checksum exports and the existing 129-point MAF editor.
  Each fit starts with an oversized grid to exercise removal of both scrollbars.
- Seven Workbench and seven definition-import workflows pass. An L38KEH000
  intake-cam VVT example verifies the standalone editor and illustrates a
  surface with distinct levels and transitions. The original ROM is preserved.
- Existing reader/logger behavior is unchanged. Tests remain offline; no new
  adapter or real-ECU validation is claimed.

## Compact editor — 0.5.2-alpha.1

- 259 C# tests pass, including all seven long MAF curves, clipboard bounds and
  breakpoint checks, atomic failures, exact undo, full-precision no-op edits,
  physical-axis interpolation, and layout settings persistence/recovery.
- Seven desktop layout workflows fit a synthetic 16×16 table at both 1024×700
  and 1280×720 logical window sizes with readable cells, visible axes and zero
  horizontal/vertical table scrolling. This fixture is not a stock tuning map.
- Layout checks exercise information, browser and graph windows, unchanged ROM
  edits while moving panels, fresh-window restoration, surface fit in wide/tall
  panes, and each 129-point MAF table's final point, scaling, interpolation,
  clipboard round trip, full-precision cell entry and checksum-verified export.
- Existing definition imports, Workbench navigation and logger/replay checks
  remain required. These tests use synthetic images and offline replies.

No physical adapter or ECU was accessed for this UI change.

## Definition import — 0.5.1-alpha.1

- 249 C# tests pass, including import persistence and fallback for all seven ROMs,
  stale replacement/concurrent-writer checks, corrupted-file recovery, parser
  bounds, protected storage, logger compatibility checks and corrected exports.
- Seven desktop import workflows cover both definition types, review metadata,
  retained open edits, fresh-window persistence and restoring bundled copies.
- Seven existing Workbench and fourteen normal/fault logger workflows pass.
- The importer does not add new ROM layouts or expand the logger request allowlist.
  Program guards and mandatory checksum correction remain application controlled.

These are offline software checks. No adapter or Mazda ECU was accessed for this
change, and existing ECU validation remains pending.

ECU access is read-only. Offline ROM editing is available.
Real Mazda ECU validation is pending; the physical tests below use two adapters
on an isolated, terminated bench and a RAM ECU model. The adapter roles are
specified for each version.

## 0.3.0-alpha.2 logger qualification and recorded-log viewer

- 165 offline C# tests pass, including all 52 original parity cases. The seven
  unchanged logger profiles retain 2,030 native-derived vectors and 258 channels.
- Fourteen desktop recording/failure workflows cover all seven profiles. Viewer
  checks cover quoted CSV, incomplete final rows, timestamps, sample age, gaps,
  spike preservation, selection limits and recorder round trips for every profile.
- The standalone EXE passes startup and ROM binding for all seven profiles, plus
  a separate saved-recording viewer exercise. Normal and minimum-size plots were
  visually checked; opening the viewer needs no adapter and leaves the CSV intact.
- Physical logger tests used vLinker FS and Tactrix on the isolated bench, each
  taking a turn as the reader. All seven profiles passed in both directions.
- Unsupported identifiers, transient negative responses, delayed replies, short
  replies and missing replies passed in both directions. An uncertain exchange
  stops the logger without retrying. Each reader completed 1,020 sustained queries.
- Actual desktop connect/start/stop and recording/replay checks passed with each
  adapter. Independent audits compare client replies with the peer's CAN trace,
  check request ordering and independently decode recorded values.
- Physical USB removal of each reader stopped the next query without retrying
  and retained the partial recording. After reconnection, each reader completed
  ten full scans (340 queries) with clean driver shutdown. Cleanup warnings while
  USB was absent remain in the evidence. The vLinker returned to its default
  115200 baud on USB reconnection; its original 16 ms latency was verified before
  the next connection. Active logging still uses 2 Mbaud and 1 ms FTDI latency.
- The independent audit matched 5,402 logger queries and decoded 4,443 recorded
  values. Separate desktop audits matched both UI recordings with the CAN peer.
- Seven regression cases cover the developer-only vLinker ECU peer parser. The
  peer now discards a previously consumed monitor line when it accompanies the
  next request; a legitimate repeated request remains distinct. This corrects the
  test fixture and does not change the production reader's retry policy.

These tests qualify the USB/CAN transport and host logger against simulated
responses. All real-ECU channel checks remain pending. Bench throughput varies
with peer timing and does not establish ECU refresh rates or vehicle performance.
See the [protocol findings](../../docs/LOGGER_PROTOCOL.md) and
[logger guide](../../docs/LOGGER.md).

## 0.3.0-alpha.1 stock-ROM logger draft

- All seven ROMs with tuning definitions have logger profiles: 35–38 channels
  sharing 31–34 identifiers per ROM, including calculated boost and injector duty.
- 2,030 native instruction-derived vectors cover decoding and calculations. Native
  execution uses bounded ROM routines with synthetic RAM, not a complete ECU.
- 146 offline C# tests pass, including all 52 baseline regression cases and
  existing reader protections. Logging identifier requests are restricted to the
  bundled catalog; programming and unknown identifier requests remain blocked.
- Offline tests cover hidden dependencies, shared requests, signed values, unit
  conversion, calibration/identity guards, unsupported/transient rejections,
  malformed replies, disconnection, cancellation and calculated-input skew.
- Both adapter transports pass logger framing and negative-response handling
  with test fixtures. Their earlier physical read qualification remains separate.
- Fourteen actual desktop button workflows (record/stop and connection failure
  for each profile) pass full-profile recording, chart updates,
  remembered settings, stop/connection cleanup and injected connection failure.
  A partial failed scan is retained; the failed request is not retried.
- The logger table and recording controls remain accessible at the minimum
  window size. The standalone EXE includes all profiles and logger help.

**No real ECU or physical-adapter logger validation has been performed for this
release.** All 258 ROM/channel ECU checks remain pending. Host sample rates shown by
the app are measured responses, not proof of ECU variable refresh rates. Session
acceptance, physical units/polarity, sensor readings and sustained logging rates
need testing. No modified-ROM logging request, logging patch or ECU programming is added.

See the [logger guide](../../docs/LOGGER.md),
[coverage](../../definitions/logging/All_ROM_Coverage.md) and
[ECU test checklist](../../definitions/logging/All_ROM_ECU_Checklist.csv).

## 0.2.0-alpha.2 Tactrix reader

The new reader uses Tactrix Openport 2.0 through its installed 32-bit J2534
driver. The standalone Windows build is now x86 and also runs on 64-bit Windows.
The vLinker reader remains available. Registered Openport-compatible driver
entries remain listed and selectable.

- 95 passing offline tests, including all 52 baseline regression cases.
- Eight reversed-role physical probes passed, through a 3,968-byte ROM block.
- Production C# identification, 12 blocked requests, 15 read sizes and boundaries,
  and 1,000 repeated exchanges passed with Tactrix as the reader.
- A complete known-image backup passed: both 1 MiB reads matched the source.
- Actual desktop controls passed cancellation, disconnect, reconnect and a
  complete unknown-image backup. Both full unknown-image reads matched; no
  checksum layout was guessed.
- Short, negative and missing memory replies stopped after one request and
  created no verified backup. A pending reply followed by the final data passed.
- The x86 vLinker reader passed its read-size/boundary checks and a complete
  two-pass known-image backup, using Tactrix as the ECU peer.
- The single EXE passed startup, adapter selection, retained driver selection,
  bundled-help checks and fresh-process reopening of both known and unknown
  backups. No Simulator connection or programming is enabled.

The reversed bench uses vLinker FS as a RAM ECU peer. It sends explicit ISO-TP
frames and remains ready for the next tester request while returning its final
response frame. Its serial-command pacing affects bench timing. These results
do not establish read speed or compatibility with a real ECU.

Tactrix USB removal as the reader has not been physically repeated in this
version.

## Earlier 0.2.0-alpha.1 qualification

The following results describe the x64 vLinker reader with Tactrix as its ECU
peer. They are retained separately from the new Tactrix reader's coverage.

## Offline and desktop checks

- 68 passing C# tests, including all 52 baseline regression cases.
- All 25 original storage-failure subcases, plus framing, identity, unknown-ROM,
  checksum, cancellation, and ambiguous-response checks.
- All seven available known ROM layouts inspected and checksum-corrected copies
  verified to change only their correction word. Source images stay unchanged.
- An unknown layout remains readable and has no guessed checksum correction.
- Verified backup fixtures retain their canonical format and journal integrity.
- Self-contained Windows x64 EXE launches, loads its bundled help and notices,
  and reopens a hardware backup in a fresh desktop process.

## FTDI transport qualification

The independent C# D2XX backend passed the fastest-first gate at 2 Mbaud:
three consecutive verified, two-pass 1 MiB backups, followed by 1,000 full-size
3,968-byte reads at varied addresses. The backups include an unknown all-FF
image. No UART line, framing, timeout, data comparison or cleanup error was
reported. Each connection restored its original FTDI latency and returned the
adapter to 115200 baud. Lower rates were not needed after this gate passed.

The three full backups took 198.95, 201.71 and 198.33 seconds on this bench.
The 1,000-read stress test took 367.26 seconds. The independent CAN-side audit
matched all 2,622 qualification requests and accepted response hashes. These
measurements cover USB/CAN transfer and the application's durable backup files;
they do not measure a real ECU's timing or flash programming.

## Physical adapter checks

The measured setup uses vLinker FS firmware 2.3.02, Tactrix Openport firmware
1.16.4819, 500 kbit/s CAN, and a temporary 2 Mbaud host serial link.

| Check | Status |
| --- | --- |
| Known image: two complete 1 MiB reads match source | Passed |
| Unknown image: two complete 1 MiB reads match source | Passed |
| Unreported calibration: full backup still allowed | Passed |
| Fifteen read sizes and end-of-ROM boundaries | Passed |
| Twelve forbidden requests rejected before serial output | Passed |
| Cancellation retains partial captures and incomplete journal | Passed |
| Changed identity stops before session/security entry | Passed |
| Negative and short replies stop without retry | Passed |
| Different pass hashes never seal a verified backup | Passed |
| Lost reply stops once; pending reply followed by final is accepted | Passed |
| One thousand consecutive diagnostic exchanges | Passed |
| Actual desktop buttons: cancel, reconnect, full backup | Passed |
| Abrupt worker termination: fresh process detects incomplete journal | Passed |
| Full desktop read after interruption | Passed |
| vLinker physical USB removal and full read after reconnection | Passed |
| Tactrix physical USB removal and full read after reconnection | Passed, RAM peer restarted |

The protocol cases and desktop controls passed again with the FTDI backend.
The physical vLinker USB removal test also passed with FTDI: the first attempted
read after removal stopped, no retry reached CAN, and no verified backup was
created. Reconnection was followed by the actual desktop cancel/reconnect/full
backup sequence. Its 558 memory replies matched the reference bytes, and driver
settings were restored afterward.

The original process-kill test and Tactrix-side USB removal used the Win32
backend. Fresh full backups after the Tactrix restart passed with FTDI. A peer
restart reloads the reference image and does not establish persistence across
an actual ECU power failure. The final adapter check reported 115200 baud,
16 ms original/restored latency, a closed handle and no driver errors. The
Tactrix RAM peer was stopped cleanly.

The independent Tactrix-side trace is compared with accepted host response
hashes. These tests exercise the actual USB/CAN adapters, not MCU flash memory.
Programming/recovery state-machine cases are offline tests only; the C# app and
its read-only bench client cannot erase, program, reset, or clear DTCs.

## Serial investigation

Initial serial implementations reported overruns and truncated replies. Win32
COM reads also failed at 500 kbaud; reducing the baud rate alone did not resolve
the issue.

Interoperability research identified a direct FTDI stream and a 1 ms latency
setting in the installed commercial application. SpeedLink independently uses
FTDI's public D2XX API, without copying or distributing commercial code. It
loads the installed Windows driver, checks UART line errors, bounds reads and
writes, and restores the captured latency setting when closing. Exact reply
lengths and the two-pass hash comparison remain enabled. The API and latency
changed together, so the successful qualification does not isolate either one
as the sole cause of the earlier failures.

## Reproduce

Run the commands in the main C# README for offline tests and a standalone build.
`tools/Verify-Regression.ps1` checks passing TRX results against the original case
list. The source-only bench clients require an explicit `--isolated-bench` flag
and COM port. Never connect a simulated ECU peer to a vehicle network.

Private captures, VINs, traces and ROM binaries are excluded from the repository.
# Editor validation — 0.4.0-alpha.1

ECU access remains read-only. This editor alpha adds offline editing only.

- 211 C# tests passed, including the previous reader/logger tests and 46 editor
  cases across all seven ROM profiles. Zero skipped or failed tests.
- Independent XML/binary decoding agreed with the editor on all 174,741
  displayed numeric cells and individual named switches across 6,185 controls.
  These local reference images had no nonfinite defined numeric values.
- Every defined control was exercised for no-op preservation, localized edits
  and byte-exact undo. Bit groups changed only the named bits within shared
  storage. Source ROM hashes remained unchanged.
- No-edit binary exports were identical to their inputs. Edited exports had
  valid checksums; injected checksum-word faults were repaired and that repair
  could be undone. Existing-file/original-file overwrites were rejected.
- Standalone desktop acceptance passed for all seven profiles, including direct
  numeric cell commit, arithmetic, named-bit selection, undo/redo, comparison,
  changed-map filtering, grid/curve/surface rendering and no-edit export.
- The existing standalone main/logger startup smoke test passed. No adapter or
  ECU traffic was used for editor validation.

The definitions themselves retain their existing research confidence. These
checks verify software encoding and file behavior, not tuning safety or vehicle
behavior. Axis editing, code patches, smoothing, saved project metadata and
log-to-map overlays are outside this first editor version.

For local desktop checks with your own supported ROM:

```powershell
./publish/SpeedLink.exe --editor-smoke --file C:/roms/L38KEH000.bin --output C:/new-editor-evidence --smoke-report C:/new-editor-evidence/result.json
```

Use a new output directory each time. Developer checks write labeled local test
copies and screenshots. `tools/SpeedLink.EditorAudit` also accepts an independent
golden JSON array (`path`, `maps: [{name, values}]`) and a new evidence directory.
ROMs and lab evidence are excluded from the distributable source.

The release review added masked program-area signatures. A modified-program
reference image retains the stock identity and boot but is now rejected.
Seven synthetic profile tests cover this rejection and reopening valid table
edits. Synthetic editor UI workflows run in CI with no ROM files or hardware.

# Workbench validation — 0.5.0-alpha.1

The single-window Workbench retains the same ROM encoders, program signatures,
transport implementations and logger polling policy. Every editor save now
corrects and verifies the checksum; the save API has no opt-out.

- 218 C# cases include seven no-edit checksum-fault repair tests, one per profile.
- Seven embedded Workbench workflows check unsaved edits across workspace changes,
  the changes drawer, all four grid/graph layouts, Focused mode and minimum sizing.
- Seven standalone editor workflows and fourteen normal/fault logger workflows
  cover all profiles. Recording-viewer and standalone startup checks remain in CI.
- The Workbench retains native automation containers for its workspace contents.
  A manual native-input check verified editing and declining the unsaved-close prompt.
- Live plots have independent selections, capped at six, and a shared 30-second
  time axis. Chart selection never adds channels to the logger polling set.

No Mazda ECU was used for this redesign. Existing physical adapter evidence
covers the earlier transport/logger implementation; it is not a new vehicle or
hardware acceptance test for this UI release.
