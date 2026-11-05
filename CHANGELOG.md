# Changelog

## 0.8.0-alpha.2

- Add **Flash & Recover** below Connection and a shortcut after a verified Gen 1 backup.
- Find the original backup by VIN or chassis ID and calibration for a normal write. Remember backup locations across versions and match recovery backups to the exact interrupted journal.
- Show saved interrupted runs by date and calibration and accept older run folders. **Check ECU state** distinguishes normal, recovery and unconfirmed replies and finds the matching backup when recovery mode is confirmed. Recheck boot mode before erase.
- Offer two, one or zero full readbacks for normal writes. Skipped readbacks produce an explicitly unverified result. Recovery still requires two.

## 0.8.0-alpha.1

- Add **Tools → Gen 1 flashing and recovery…** for every bundled Gen 1 ROM layout. Verify the original backup, target image, connected ECU identity, supply and retained boot before a normal write.
- Restore the original stock image after an interrupted write using the same ECU's verified backup and the failed run journal. Check the retained boot server and require two full readbacks after reset.
- Add built-in and repository flashing and recovery guides. Gen 2 flashing is planned for an upcoming release. Gen 2 reading, editing and logging retain their existing scope.
- Add experimental Gen 1 flashing and recovery for Mongoose-Plus ISO/CAN3 through its installed J2534 04.04 driver. Physical programming qualification is pending.

## 0.7.0-alpha.9

- Make **View recording…** open the session just saved. Keep the last saved recording available after live-only logging, recover cleanly if a file was moved, and disable the shortcut while a recording is still being written.
- Include the final durably saved block in stopped-read progress, even when its display callback is still queued. Ignore callbacks from previous operations and keep partial reads clearly unverified.
- Reuse live chart views and draw frozen vector paths instead of rebuilding their controls on every update. Avoid repeated grid notifications for unchanged values. The same 33-channel, six-chart stress workload allocated about 67% fewer bytes per update on the test host; polling and recording remain independent of redraws.
- Add required regressions for recording shortcuts across all ten profiles, cancellation/display races, bounded chart histories and selection changes, flat values and missing-data gaps. All 559 core tests and the ten-profile ROM-save integrity matrix passed, alongside desktop and read-only bench checks through both supported adapters.
- Keep ECU programming disabled. Physical sensor accuracy and running-engine sampling behavior remain pending validation.

## 0.7.0-alpha.8

- Default to **Polling → Max**, with no artificial scan-rate wait. Optional numeric limits remain available for lower bus traffic or recording volume; older settings migrate to Max while retaining channel, unit, plot and recording choices.
- Finish vLinker logger receives after a complete, exact-length, matching reply, then consume the prompt and verify adapter synchronization. Preserve response timeouts and never repeat an uncertain ECU request. Verified standard read batches reduce requests on nine Gen 1 layouts without changing payload precision; Gen 2 retains its independent identifier path.
- Separate display updates from polling and recording. Expire stale screen values during stalled receives and capture each frame's UTC/elapsed timestamps together so recorded clocks stay consistent.
- Validate both wired adapters on one L3M6EG000 bench ECU with more than 25,000 read-only requests combined, mixed workloads, recording off/on, sustained all-parameter recording and repeated desktop sessions. Both achieved about 50 single-parameter polls/s or 1.85 complete all-33-parameter scans/s. Physical sensor accuracy and running-engine rates remain pending; see the logger performance guide.
- Fix Start logging staying disabled after Stop. Normal Stop completes the current exchange and recording, preserves a healthy connection and ROM/parameter choices, and allows another Start without reconnecting. Each start uses fresh polling state and a separate recording.
- Recheck ECU identity before each logging session. Keep explicit reconnect requirements after transport errors, identity changes and before switching between ROM-reading and logging sessions. Failed or uncertain requests are never retried automatically.
- Exercise three consecutive Start/Stop cycles, immediate cancellation, distinct replayable recordings, retained choices and failure cleanup across every supported logger profile. Add a separately gated read-only real-ECU logger test path; ECU programming remains disabled.

## 0.7.0-alpha.7

- Add the verified L3M6EG000 / L3M6EG000L3T60A1 stock layout: 263 tuning tables and 600 diagnostic controls. Include tuner-focused help, units and named code states. Unknown compatible ROMs remain readable without a definition.
- Apply mandatory checksum correction and verification on every L3M6EG000 editor export. The native program sum covers 0x3F000 through 0xFFFFF; correction uses reserved padding at 0xFFFF4 and preserves live instructions at the range start.
- Exclude only the established per-ECU identification record from this profile's boot fingerprint. Exports preserve that record byte-for-byte; the remaining boot bytes, calibration identity, program signature and checksum-layout checks remain enforced. Existing calibration-ID matching is unchanged.
- Add a 33-parameter stock logger using 30 sequential requests, with native encoder and identifier-copy checks plus 247 synthetic conversion vectors. Five unverified/unoffered baseline parameters remain unavailable, including a neutral bit that this stock status encoder does not populate. Real-ECU logging acceptance, physical values and rates remain pending.
- Extend the editor, import, layout, navigation, map-window, recovery and save-integrity checks to the tenth ROM. A reference-ROM audit covers all 863 controls, 21,389 logical cells, localized edits, undo and checksum-verified exports. ECU programming remains disabled.

## 0.7.0-alpha.6

- Recognize supported short chassis-number replies, including space/null padding, instead of rejecting them as invalid 17-character VINs. Show **Chassis ID** in Connection and retain the chassis number separately in local backup metadata.
- Preserve standard VIN validation and reject malformed identifiers. Recheck VIN/chassis identity and calibration before starting a backup; changing any of them still stops before read-session access. Unknown compatible ROMs remain dumpable without borrowing another profile's logger or editor definitions.
- Redact chassis identifiers from support exports, including logged text and hexadecimal forms. Keep factory security diagnostics and omit ROM payloads.
- Add offline identification, adapter-response, backup, logger-binding and privacy regressions, plus chassis-based Gen 1/Gen 2 desktop backup checks. A real bench ECU successfully identified and reported stopped-engine RPM through both supported adapters using the patched reader. This does not establish ROM-dump, logging or flashing compatibility for its calibration. ECU programming remains disabled.

## 0.7.0-alpha.5

- Center vertical axis titles beside the visible data rows, including short tables with empty space below them. Center horizontal titles over the visible data columns, excluding row headers, scrollbars and unused space.
- Keep titles aligned after switching maps, scrolling, zooming, resizing and detaching windows. Use the arranged cell geometry so pixel rounding and partially visible edge cells are handled correctly. Full titles remain available on hover.
- Add required alignment checks for all 7,522 bundled maps and controls across nine ROM profiles at two window sizes, plus 2,216 table-shape/view/zoom combinations and independent pane checks. Retain editing, map-window and mandatory save-integrity regressions.
- Update the editor guide, About and screenshot gallery. ECU programming remains disabled.

## 0.7.0-alpha.4

- Keep File, Edit, View, Tools and Help available in every workspace. Open a ROM or recording from File, find definition management in Tools, and use Help for guides, support export, crash reports and About.
- Show sidebar icons with workspace names by default. The sidebar button and View menu toggle the same compact mode, with descriptive tooltips and saved preferences. Existing navigation choices remain respected.
- Reuse the editor's commands and mandatory integrity checks from the application menus. Preserve ROM edits while changing workspaces, and disable commands whose required ROM or map is unavailable.
- Keep compact tables readable at narrow window sizes by using their measured cell minimums instead of requiring unnecessary horizontal scrolling.
- Add required nine-profile application-navigation checks for menu visibility, keyboard use, command states, Undo/Redo, saved preferences and edit preservation. Refresh the guides and screenshots. ECU programming remains disabled.

## 0.7.0-alpha.3

- Resize any map from its own edges or corners without moving or resizing the other maps. A lone map can fill unused space beside it. Manual resizing and title dragging allow overlapping panes; the active map comes to the front, and choosing a covered map in the browser reveals it.
- Preserve each pane's position and size when opening or closing other maps, docking back, resizing the workspace and reopening the same calibration. Scroll to reach panes in a smaller workspace. Tile and stack commands restore automatic arrangement. Native detachable windows remain available by dragging a title outside the workspace or choosing Undock.
- Add required nine-profile desktop checks for independent resizing, overlap, movement, pointer hit testing, cancellation, mixed scalar/table layouts and saved geometry. Keep readable text minimums, shared edits and mandatory ROM-save integrity unchanged.

## 0.7.0-alpha.2

- Handle unsupported, unknown, incomplete and invalid ROM files as recoverable editor errors. Keep the current ROM, unsaved edits, undo history and docked/detached maps intact after rejection; supported files can still be opened afterward. Program-layout and checksum protections remain enforced.
- Show single values and named states in small cards that wrap across the workspace, normally about 300 pixels wide. Use standard-size text and number fields, one baseline row, compact window commands, and Apply/Cancel only while typing. Keep full help behind the information button. Card widths, heights and popout sizes remain resizable and restorable.
- Label boost correction modes as Engine load and Boost pressure, with help explaining the target-versus-actual difference. Stored mode values are unchanged.
- Require nine-profile desktop regressions for file rejection and recovery, five simultaneous cards at two window sizes, browser-driven wrapping, width/height resizing, saved-size restoration, full help access, invalid input and shared edits. Retain all-profile save-integrity and existing editor regression checks.

## 0.7.0-alpha.1

- Open several maps together in resizable panes, with independent native Windows popouts. Expand and restore a map without closing other views. Each pane keeps its own graph, baseline, selection and text zoom over one shared ROM and undo history.
- Auto fit changes cell spacing without shrinking text. Measured displayed values set the minimum width; small windows scroll instead of clipping values. Deliberate text zoom is bounded to 85–150%. Compact maps and long MAF curves retain their specialized editors.
- Restore map layouts, split proportions, window bounds, selections, scrolling and graph views separately for each calibration when its ROM is opened. Pending invalid input blocks saving and closing its view; another pane cannot erase it.
- Add required nine-profile map-window desktop checks and Core readability/persistence cases. Retain all existing editor, logger, backup, integrity and standalone release checks. Update the editor guide and screenshots. ECU programming remains disabled.

## 0.6.0-alpha.6

- Keep floating map panels inside the Editor workspace and hide an empty floating graph until a graphable map is selected. Handle file permission errors without terminating the editor.
- Remember logger channels, units and plots separately for each ROM profile, including Gen 2 choices and changes made without starting a recording. Restore choices on launch without trusting an old ECU/ROM binding.
- Validate display settings before logging starts so invalid AFR input leaves the connection available. Explain the reconnect required after a ROM read with a direct Connection action.
- Refresh stopped chart statuses immediately. During a partial scan, retain fresh last-known values with an explicit pending age; clear expired or rejected data without changing recording or calculation freshness rules.
- Restore the prior recording and usable status if a replacement cannot be opened. Correct Gen 2 replay default-selection checks while retaining the six-plot limit.
- Correct diagnostic rotation byte accounting so full-size Windows logs export reliably. Developer reports use the current build identity and preserve primary results if support export fails.
- Add synthetic desktop lifecycle regressions for all nine profiles and full-size diagnostic export/failure checks to the required release workflow. ECU programming remains disabled; real ECU acceptance remains pending.

## 0.6.0-alpha.5

- Show the map browser whenever a ROM opens, including after restoring a hidden layout. Keep a visible Maps button and a clear map-selection prompt when no table is selected.
- Cover saved hidden layouts, closing the final map tab, and restoring minimized floating browsers in desktop regression checks for every supported ROM.

## 0.6.0-alpha.4

- Correct release source packaging to archive the complete repository from its root, including definitions, documentation and build resources.
- Verify every archive path and file against the exact source commit's Git blob hashes before release packaging can complete.
- Includes all alpha.3 crash reporting, CI evidence, automated releases and reviewed-maintenance improvements. Its source archive supersedes the incomplete dotnet-only archive in alpha.3.

## 0.6.0-alpha.3

- Capture unexpected application errors locally, offer reports on restart and export a crash support ZIP. Keep existing drafts and backups intact; no automatic uploads or memory/ROM dumps.
- Retain synthetic CI reports and screenshots for 14 days, bound check duration and cancel superseded branch runs.
- Build, test and publish the same standalone EXE through GitHub, with matching source, SHA-256 checksums, release identity and build provenance. Windows code signing remains pending.
- Propose weekly NuGet and pinned-action PRs, plus tested coupled SDK/runtime patches for review. No automatic merge or release.
- ECU programming remains disabled. No new physical adapter or real ECU acceptance is claimed.

## 0.6.0-alpha.2

- Rebuild the standalone EXE with the .NET 10.0.14 security update and SDK
  10.0.403. Refresh dependency locks with the pinned SDK.
- Pin the bundled runtime explicitly and require its exact version in the
  standalone startup check. Keep the Windows x86 build and installed-driver paths.
- Show the running .NET version in About. Replace the EXE to receive this update;
  preserve the existing `data` folder.
- Require automatic checksum correction and verification on every editor export,
  with no opt-out. Enforce prepared-byte and durable-readback integrity checks.
  Gen 2 native code validation rejects invalid code without guessed repair.
- Add an explicit contributor/agent requirement and mandatory save tests for every
  supported ROM, including repeated edits, no-edit saves and invalid input.
- ROM definitions, read/logging protocols and integrity policies are unchanged.
  ECU programming remains disabled; real ECU validation remains pending.

## 0.6.0-alpha.1

- Add experimental Gen 2 application reading, automatic known-family selection
  and an explicit protocol choice for compatible unknown Gen 2 ROMs.
- Add L3CGEE000 editing with 442 controls and mandatory native code/marker
  validation on save, without arbitrary code repair.
- Add a 20-parameter L3CGEE000 stock-ROM logger with exact reply checks,
  calibration binding and explicit unavailable channels.
- Record the read protocol in verified backup manifests and support diagnostics.
- Publish diagnostic storage failure state before flush callers resume.
- Update bundled Help, README, About and the Gen 2 validation guide.
- Native/offline and desktop checked; real ECU acceptance and logging values/rates
  remain pending. Production ECU programming stays disabled.

## v0.5.5-alpha.5 — logger calibration matching

- Recognize known complete ECU calibration identities as well as short software
  IDs, including L3P4EM000L3T6051. Fix the false ROM mismatch when loading a
  matching ECU backup for logging. The ROM filename does not affect matching.
- Keep unknown build suffixes, different profiles and ambiguous IDs rejected.
  Show the selected ROM profile and reported ECU calibration when they differ.
- Add full-identity regression checks across all eight profiles and exercise
  complete IDs in the desktop logger checks. Logger parameters and ROM editing
  definitions are unchanged; live ECU logging validation remains pending.

## v0.5.5-alpha.4 — L3P4EM000 editor and logger

- Add the 2006 Australian Mazda 6 MPS L3P4EM000 stock-ROM definition: 895 maps
  and controls, ROM identity/boot/program checks and mandatory checksum correction
  on export. Existing definitions remain available.
- Add 35 stock logger parameters with matching ROM code guards, MAP calibration
  bindings and 277 native-derived vectors. Three unavailable baseline parameters
  are excluded. Live ECU logging and rate validation remain pending.
- Extend the editor/logger/import checks and coverage checklist to eight profiles.

## v0.5.5-alpha.3 — backup progress

- Show prominent backup progress on Connection, including the current pass,
  bytes read, overall percentage, elapsed time and estimated time remaining.
- Estimate from measured transfer speed across both passes. Keep verification
  below 100% until the backup and its completion records have been saved.
- Retain the last progress on Stop or failure, clearly marked not verified.

## v0.5.5-alpha.2 — J2534 receive compatibility

- Preserve validated messages returned with timeout-like J2534 statuses instead
  of treating every such return as empty. Keep fatal errors and malformed replies
  fatal, bounded response waits and no automatic request resends.
- Use the complete reported ISO15765 message length and record inconsistent
  extra-data indexes rather than truncating the diagnostic reply.
- Include returned-message counts, timeout-with-data counts and native-call
  timings in support diagnostics. Mongoose hardware validation remains pending.

## v0.5.5-alpha.1 — diagnostic reports

- Capture persistent, bounded diagnostics before the adapter opens: setup,
  requests, timing, errors, J2534 receive statistics and factory seed/key access.
- Export a support ZIP with a readable preview and session picker, including
  failed sessions after restarting. Personal information is redacted; ROM bytes
  stay in backups. Keep backup journals unchanged and export a sanitized copy.
- Add a reporting guide in Help and a guided GitHub problem-report form.
- Retain read-only ECU access, existing deadlines and no automatic resends.

## v0.5.4-alpha.10 — updated user guides

- Reorganize the editor guide around first edits, single values, small tables,
  long MAF curves, selections, comparisons, layout and checksum-verified exports.
- Explain preview resizing, Fit map versus Fit view, Map info and shortcuts.
- Align the included Getting started and Logger help with workspace navigation.
- Refresh the documentation index, editor overview, About text and release status.
- Retain the existing editor, definitions and ECU-access behavior.

## v0.5.4-alpha.9 — single values and resizable previews

- Replace the one-cell grid with a dedicated value editor: units, explanation,
  baseline and change together, with Apply/Enter, Cancel/Escape, reset and Undo/Redo.
- Preserve full stored precision, reject invalid or unencodable values inline,
  retain invalid drafts, and commit valid drafts before saving or switching maps.
- Use the same focused layout for single-state selectors. Size small grids and
  multi-state controls to their contents, and improve short-curve preview labels.
- Make the right-hand table/preview divider draggable. Remember its width across
  launches and docking; Fit map restores the automatic compact preview.
- Retain complete 16×16 fitting, long MAF editing and mandatory checksum correction.
- Update the editor guide and screenshots. Definitions and ECU access are unchanged.

## v0.5.3-alpha.1 — table preview

- Keep a compact 3D preview at the upper-right of the map grid, using the same
  right-hand graph position and controls as the MAF editor.
- Retain the full table height and fit a complete 16×16 grid at both tested
  logical window sizes while the preview remains visible.
- Add Table with 3D preview to View and make it the Fit map view for surfaces.
  Popping the graph out and docking it restores the compact preview.
- Use an intake-cam VVT map with clear changes in height for 3D screenshots.
- Prevent scrollbars from keeping one another visible when fitting a table
  after opening or resizing on a narrow desktop.

## v0.5.2-alpha.1 — compact editor

- Collapse app navigation to icons and reduce the editor's top and bottom bars.
- Add Fit map, a resizable/hideable/floating map browser, floating graph and
  separate map-information window. Remember panel sizes, views and positions.
- Fit a full 16×16 table and both axes without scrolling at 1024×700 and
  1280×720 logical window sizes using Fit map with compact navigation.
- Display long MAF curves as breakpoint/value rows beside a curve with zoom,
  pan and linked point selection. Keep static breakpoints read-only.
- Keep full precision during cell editing and clipboard operations; support
  long-curve value-column paste and validated breakpoint/value paste.
- Fit the 3D surface to wide and tall panes. Preserve mandatory checksum saves.

## v0.5.1-alpha.1 — definition import

- Import editor XML and logger JSON updates through the Definitions window.
- Keep imported copies in `data/definitions`, with explicit replacement review,
  version/source details, bundled fallback and retained previous imports.
- Preserve open ROM edits during imports; apply updates when a ROM is next loaded.
- Reject unsupported layouts, protected storage, unrecognized logger requests
  and invalid files while retaining mandatory checksum verification on exports.

## v0.5.0-alpha.1 — Workbench UI

- Added a single-window workspace with vertical navigation and graphite/amber styling.
- Embedded the ROM editor and recording viewer; switching workspaces retains edits.
- Added Focused mode, four grid/graph layouts, collapsible map details and a changed-maps drawer.
- Added up to six live logger plots sharing a 30-second time axis and remembered plot selections.
- Editor saves always correct and verify the checksum; removed the checksum opt-out from the UI and save API.
- Added seven-profile Workbench UI checks and checksum-repair regression tests.
- ECU access remains read-only; real Mazda ECU validation remains pending.

## v0.4.0-alpha.1 — offline ROM editor

- Added definition-driven editing for all seven existing tuning ROM profiles.
- Added searchable categories, naturally ordered related maps, map tabs, resizable
  panes, tuner help, a heat-map grid, 2D curves and rotatable/zoomable 3D surfaces.
- Added named switches and individual DTC bits with preservation of shared bytes.
- Added original/last-save/compatible-ROM comparison, changed-map filtering,
  selection arithmetic, spreadsheet clipboard, physical-axis interpolation,
  byte-exact undo/redo and reset to baseline.
- Saves create a new verified file, with checksum correction and original
  preservation. ECU programming remains disabled. No ROMs are bundled.
- Reject other program modifications with masked program-area signatures; retain
  unknown-ROM reading. Added synthetic editor desktop checks to CI.

## v0.3.0-alpha.2 — 2026-10-19

- Add a saved-log viewer with up to six synchronized plots, cursor inspection and zoom.
- Preserve recorded units, missing-data gaps and narrow peaks/dips during plot reduction.
- Show sample ages and unavailable counts; refuse stale cursor values.
- Read complete rows from an interrupted CSV tail without modifying the recording.
- Retain partial logger frames on cancellation even without a progress callback.
- Add replay parsing, malformed-data, timestamp, gap/spike and seven-profile round-trip tests.
- Document stock logger request scheduling and timing limits.
- Add developer-only physical logger fixtures; real-ECU logging validation remains pending.

## v0.3.0-alpha.1 — 2026-10-19

- Add stock-ROM logging for all seven defined Mazdaspeed/MPS ROMs: 35–38 channels over 31–34 identifiers per profile.
- Check each ROM's native encoder paths, calibration locations and available identifiers; exclude unverified channels rather than copying addresses.
- Select profiles automatically from a matching current ECU backup and calibration identity; clear calibration when switching profiles.
- Add parameter selection, display units, measured host sample rates and a rolling chart.
- Save CSV with per-channel timestamps/status, raw replies and recording metadata.
- Bind MAP calibration from a matching ECU backup and check live calibration identity.
- Keep unsupported readings unavailable and stop on transport failures without retries.
- Add 2,030 native-derived vectors, 146 offline C# tests and recording/fault desktop workflows for all seven profiles.
- Real-ECU logger readings and performance remain pending validation.

## v0.2.0-alpha.2 — 2026-10-19

- Add Tactrix Openport 2.0 ROM reading through J2534 alongside vLinker FS.
- Publish a self-contained Windows x86 EXE with 95 offline tests and isolated adapter qualification.
