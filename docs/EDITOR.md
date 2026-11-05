# SpeedLink ROM editor guide

Edit a supported ROM without an adapter. SpeedLink keeps the original file and
exports each revision as a new file with mandatory ROM-specific checksum/integrity checks.
To write an export to a supported Gen 1 ECU, use the original verified backup
from that ECU and follow the [flashing guide](FLASHING.md).

**Checksum correction is automatic and mandatory on every ROM export, including
saves without table edits. There is no setting to disable it.** Gen 1 saves always
recalculate the established correction word. L3CGEE000 saves verify its native
code checksum and markers; calibration edits do not change that code sum. Invalid
Gen 2 code is rejected without guessed repair. Every export is read back and
verified before its final filename is published. Originals are preserved.

This guide is also included in the app: choose **Help → Editor guide**.
The top **File**, **Edit**, **View**, **Tools** and **Help** menus remain visible
in every workspace. **Map info** inside a map pane, or
**F1**, opens help for the selected map instead.

## Make your first edit

1. Choose **Editor** in the left navigation, then **File → Open ROM…**.
   Opening a ROM shows the map browser, even if it was hidden in your saved layout.
2. Find a map in the category list or search by name, description or category.
   Click **Map info** to check its units, purpose and correction direction.
3. Edit the control using the instructions below for its shape. Compare with
   the baseline and use Undo/Redo to review or reverse an operation.
4. Choose **Save copy…** or press Ctrl+S. Pick a new filename. Every export
   applies its mandatory checksum/integrity policy and verifies the written bytes.

An unsupported, unknown, incomplete or invalid ROM shows an error and leaves the
current workspace intact. Dismiss the message and choose another file. A matching
calibration name alone does not establish support for a changed program layout.

### Single values, such as rev limits

Single values use small cards, normally about 300 pixels wide, with standard-sized
number fields. Before manual placement, cards wrap across the available width; opening the browser can
reduce the number of columns. Drag a card's own edges or corners to resize it independently. The name appears
once, with its value, units, baseline and change.
Use the **ⓘ** button beside the pane title, or F1, for its full explanation.
Click the numeric field, type the value in the displayed units, then choose
**Apply** or press Enter. Apply/Cancel buttons appear only while typing.
**Cancel** or Escape discards the typed value. The **↶** button beside the
baseline restores that reference value.

Focusing the field shows full stored precision. Accepting the value unchanged
preserves its bytes. A valid pending value is applied when saving or switching
maps. Invalid or unencodable input stays visible and prevents saving, switching
maps or changing the baseline until corrected or canceled.

Single-state controls use a named selector in the same compact layout. Choosing a state
applies it immediately. These controls do not need a grid or graph.

### Tables and short curves

Double-click a numeric cell, enter a value using a decimal point, and press
Enter. Drag or Shift-click to select a rectangle. Axis names label the column
and row breakpoints directly; their distinct header bands stay visible while
scrolling. The selected cells highlight their row and column headers.
Static breakpoints are read-only.

Small maps use a grid proportional to their contents. A short curve has a curve
preview; a small surface has a 3D preview beside the grid. Multi-state controls
keep labeled rows and a named selector without an unused graph. Select the row
or cell you want to change, then choose its state. Bit controls preserve the
other bits sharing the byte.

### Long curves, including MAF calibrations

Long curves use a vertical breakpoint/value list beside a 2D graph. Only the
value column is editable. Click a graph point to select its list value; selecting
a list value also brings the point into view. Use the wheel to zoom and
Shift+drag to pan. The first and last points remain reachable in a 129-point curve.

Copy includes both columns at full precision. Paste accepts a value column,
matching breakpoint/value rows, or one horizontal row of three or more values.
Supplied breakpoints must match, and the whole paste must fit. Interpolate uses
the physical X-axis values between the first and last selected points.

## Work on a selection

| Operation | Result |
| --- | --- |
| Set | Replace selected values with the amount in the box. |
| Add | Add the amount to each selected value. Use a negative amount to subtract. |
| × | Multiply each selected value by the amount. For example, 1.1 applies 110%. |
| Copy / Paste | Use tab-separated values without headers. Paste starts at the upper-left selected cell and must fit completely. |
| Interpolate | Fill between selected endpoints using physical axis values: Horizontal along X, Vertical along Y, or 2D between four corners. |
| Reset selection to baseline | Restore selected cells from the chosen comparison baseline. |

Undo/Redo restores complete operations and exact prior bytes. The latest 200
operations are retained for the open ROM. Shared-storage controls update together.

Cells display compact numbers. Double-click a cell to see its full stored value;
accepting it unchanged preserves the original bytes. Copy and tooltips retain
full precision. Entered values are quantized to the ROM's storage resolution.
Nonfinite and out-of-storage-range values are rejected; existing invalid values
display as **Invalid**. Storage limits do not establish a suitable tuning range.

## Compare revisions

Use **Edit → Compare ROM…** to load a comparison file with the same calibration.
Choose **Original ROM**, **Last save** or **Comparison ROM** in the baseline
selector. The single-value card has its own baseline selector.

Orange cell borders and browser dots identify differences from that baseline.
Cell tooltips and the selection status show current, baseline and difference
values. **Changed maps only** filters the browser. **View → Map layout and display → Changes** opens a
list of changed maps; select an entry to jump to it. These are comparison views,
not a record of the order in which edits were made.

## Arrange the workspace

Maps open together in separate panes over the same ROM document. Opening an
already open map brings its existing pane forward. Numbered maps remain sorted
naturally in the browser. Each pane has its own selection, comparison baseline,
text zoom and graph view; Undo/Redo and saved ROM bytes belong to the shared ROM.

Drag any pane's edge or corner to resize only that map. Drag its title to move
it inside the workspace. The other panes retain their positions and sizes;
manual placement and resizing allow overlap. Click a map or select it in the
browser to bring it to the front, even if another pane covers it.

**View → Map layout and display → Tile maps side by side** and **Stack maps vertically** reset manual
placement and restore automatic arrangement. Independently sized panes stay
where you placed them when the window gets smaller; scroll the workspace to
reach them. **Expand** gives one map the workspace; **Restore**
returns to the previous arrangement without closing the other maps.

**Auto fit** is enabled by default. It adjusts cell spacing, never text size.
Cell minimums include the measured displayed numbers and column headings plus
padding. At the minimum, the grid scrolls instead of truncating numbers or
reducing precision. Axis headings stay visible. **− / +** deliberately changes
that map's text size between 85% and 150%, with matching cell minimums. Turning
Auto fit off uses fixed spacing. Resizing and expanding preserve the chosen zoom.

Axis titles are centered on the visible table data: the top title sits over the
populated columns, and the vertical title sits beside the populated rows. Empty
space, row headers and scrollbars are excluded. They stay alongside the visible
cells while scrolling and resizing. Long titles can be trimmed in very small
panes; hover over an axis title to read its full name and units.

**Undock** moves a map and its editing controls into a separate resizable Windows
window, which can move to another monitor. **Dock** returns it. Dragging a map's
title outside the workspace also detaches it; dropping that window over the editor docks it. Closing
a detached window or a pane's **×** closes that view and retains the ROM edits.
Invalid pending input blocks closing that view and blocks saving the ROM until
corrected or canceled. Opening another pane does not discard a pending entry.

Map windows hide while using another workspace and return with Editor. Opening
the same calibration restores its open maps, independent pane bounds, window bounds,
text sizes, selections, scrolling and graph views. Different calibrations keep
separate layouts. This restores presentation, not unsaved tuning edits.

Use the app's **☰ Navigation** button to collapse navigation to icons, or toggle
**View → Navigation labels**. The sidebar starts with names and remembers your
choice. **View → Map layout and display → Hide map
browser** (Ctrl+B) hides or restores the map list. Drag its edge to change its
width, or pop it into a separate window.

The **Maps** button in the editor toolbar always brings the map browser back.
When no map is selected, **Browse maps** opens the list instead of leaving an
empty grid. Closing the last map pane keeps the ROM and its edits loaded.

**Fit map** (Ctrl+0) restores automatic sizing and hides the browser, Changes
drawer and map-information window. With several maps open, it expands the active
map. Full tables fit when the measured cell minimums and selected text size allow;
otherwise they scroll. Long curves retain their list/graph layout. Windows display
scaling affects available logical space. Small panes temporarily omit the compact
preview to give the table priority; the selected preview view returns with room.

| View option | Use it for |
| --- | --- |
| Table with 3D preview / Table with curve preview | A compact graph at the right while keeping the full table height. The label follows the selected map. |
| Table above graph | A larger graph beneath the table, separated by a draggable horizontal divider. |
| Table beside graph | Side-by-side panes with a draggable vertical divider. Long MAF curves use this layout. |
| Table only / Graph only | Give the selected view the available space. |

Drag the divider at the table's right edge to resize the preview. Its width is
remembered across launches and graph docking. **Fit map** resets that custom
width to automatic sizing. Small-map previews stay directly beside their grids.
Graph options are disabled for controls without a curve or surface.

Use the graph's **↗** button or **View → Map layout and display → Pop out graph** for a larger floating
view. **Dock graph** or closing the floating window returns it to the workspace.
The map browser has equivalent pop-out/dock commands. Floating panels can move
to another monitor. Navigation, browser visibility/width, curve-list width,
preview width, view, split, Fit map sizing and floating-panel bounds are saved
beside the EXE in `data/editor-layout.json`.

### Read the graph

Drag a 3D surface to rotate it in the direction of the mouse; use the wheel to
zoom. The on-map rulers identify the column axis in blue, the row axis in green
and value/height in amber. Labels and tick values stay aligned while rotating.
Compact previews show fewer ticks than larger graphs.

Click a curve point or surface vertex to select its table value. Graph dragging
changes the view, not the ROM value. **Fit view** restores the camera; compact
previews use a **↺** button with the same function. Fit view changes the camera;
Fit map changes the workspace layout.

## Understand the active map

Click **Map info**, press F1, or choose **View → Map layout and display → Map information…**. The same
window comes forward for that map without reducing table height.
It is unavailable until a map is selected.

Value units and input axes appear above the tuning explanation. Descriptions
distinguish targets, limits, multipliers and signed corrections, with examples
where established. Unconfirmed physical units are identified explicitly. Expand
**Storage details** for the address, type and dimensions. Imported definitions
supply their own descriptions and units. See [map-help conventions](TUNING_HELP.md)
for examples of interpreting corrections and coefficients.

## Save a revision

**Save copy…** writes a new binary, flushes it and verifies its bytes. It refuses
to overwrite the original or any existing file. Pick a new filename for every
revision. Every export applies mandatory ROM-specific checksum/integrity checks; there is no opt-out.
Checksum correction is undoable if it changes bytes. The Original ROM baseline
always remains the image first opened in this session.

Use a descriptive filename and keep the current ECU backup separately. Closing
SpeedLink with unsaved edits prompts before discarding them. There is no saved
tune-project format or recovery of unsaved work in this version.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+O | Open ROM. |
| Ctrl+S | Save a new copy. |
| Ctrl+0 | Fit map. |
| Ctrl+B | Hide/restore the map browser. |
| F1 | Open/restore help for the active map. |
| Ctrl+Z / Ctrl+Y | Undo/Redo ROM operations when a text field is not being edited. |
| Ctrl+C / Ctrl+V | Copy/paste selected table values; in a focused text field, edit its text. |
| Enter / Escape in a single-value field | Apply / cancel typing. |

Layout shortcuts apply when you are not typing in a text field.

## Compatible files and definition updates

Supported stock layouts: **L33XEH000, L35HEH000, L38KEH000, L39SED000,
L39SEE000, L39SEF000, L3M6EG000, L3M7EF000 and L3P4EM000**. Opening requires a complete 1 MiB image
with matching calibration, boot, checksum layout and program-area signature.
The program check allows defined tuning controls and checksum changes while
rejecting other program changes. Patched/extended layouts are unsupported.
Recognizing a calibration does not prove factory provenance or vehicle fit.
Unknown images can still be dumped with the reader and inspected in ROM checks.

Use **Definitions → Import…** to install an editor XML update for a supported
layout. Save open work before reopening the ROM to use it; importing does not
change existing edits or their definition. [Definition import](DEFINITIONS.md)
explains supported files and restoring a bundled copy.

Axis editing, patch installation, smoothing, tune-project history and log-to-map
overlays are not included. Offline checks do not validate engine behavior.

[Editor screenshots](editor/README.md) · [Documentation index](README.md)

## Gen 2 integrity checks

L3CGEE000 uses mandatory native code-sum and marker validation on every export.
Calibration edits are outside the code sum. Invalid code is rejected without
repair; additional calibration-integrity rules are not established. See the
[Gen 2 guide](GEN2.md). Its 155 DTC controls govern reporting, not readiness.

For L3M6EG000, every save corrects the native big-endian program sum over
0x3F000–0xFFFFF using reserved padding at 0xFFFF4. The range start contains live
instructions and is preserved. A known per-ECU identification record is excluded
only from the boot fingerprint; exports preserve that record exactly. No setting
or imported definition can disable checksum verification or change this policy.
