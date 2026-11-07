# SpeedLink ROM editor

An offline editor for the ten bundled Mazdaspeed/MPS stock ROM layouts, built
into the same Windows app as the reader and logger. No adapter is needed to edit
a file. The [flashing workflow](../FLASHING.md) supports all nine bundled Gen 1
ROMs. The experimental Gen 2
L3CGEE000 profile has separate [integrity checks and validation limits](../GEN2.md).

## Find your way around

![Permanent application menus and labeled workspace navigation](images/application-navigation.png)

File, Edit, View, Tools and Help stay visible in every workspace. Open a ROM
or recording from File. Find definition management in Tools and guides,
support reports, crash reports and About in Help. Commands that need a ROM
or map stay disabled until one is available.

The sidebar shows icons and names by default. **☰ Navigation** and
**View → Navigation labels** switch to compact icons and back; your choice is
remembered. **View → Workspace** also switches screens. The example map uses
synthetic values.

![The File menu opens ROMs and recordings and saves a verified ROM copy](images/application-file-menu.png)

## Room for the map

### Axis titles follow the data

![Axis titles centered on the visible rows and columns of two short tables](images/table-axis-alignment.png)

Vertical titles sit beside the populated rows and horizontal titles sit above the
populated columns. Empty space and scrollbars do not pull them away from the data.
Titles follow scrolling and resizing; hover to read a full title in a small pane.
This example uses synthetic values.

![Two resizable map panes over one ROM](images/map-windows.png)

Open related maps together, drag a pane's own edge or corner to resize only it, and use **Expand** to
focus on one without closing the others. **Undock** gives a map its own Windows
window, including editing controls, for another monitor. Each map retains its own
selection, text zoom and graph view. All panes share the same ROM and undo history.
Drag titles to move panes inside the workspace. Manual resizing and movement
allow overlap; clicking a map or selecting it in the browser brings it forward.
Use **View → Map layout and display → Tile maps side by side** to reset manual placement.
The multi-map screenshot uses synthetic values, not vehicle tuning advice.

![A lower curve widened independently below three scalar maps and another curve](images/independent-panes.png)

Each pane owns its size and position. The lower curve fills the unused width
without stretching either row above it; the third scalar is shortened independently.
Manual resizing or title dragging can overlap other panes. Select a covered map
in the browser to bring it forward. This screenshot uses synthetic test data.

![A detached resizable map with its editing controls](images/detached-map.png)

Drag a map's title out of the workspace, or choose **Undock**. Drag it back over
the editor or choose **Dock** to return it. The native window's close button
closes that view while keeping its ROM edits.

![A complete synthetic 16×16 map with both axes visible](images/fit-map.png)

**Fit map** (Ctrl+0) gives the active map the available workspace. Auto fit adjusts
cell spacing without shrinking text. Measured cell minimums keep values readable;
tables scroll when the whole map cannot fit. **− / +** sets a deliberate text size.
The ☰ button expands or collapses navigation; Ctrl+B hides or restores the
map browser. Use **View** for layout options and floating panels.

Axis names sit directly above the column breakpoints and beside the row
breakpoints, with value units in each map pane. The labels stay visible while
scrolling. Selecting cells highlights their row and column headers.

The 16×16 and MAF examples use synthetic layout/curve fixtures. The 16×16
example is a layout test, not an added stock calibration map. VVT screenshots
show an L38KEH000 ROM example, as do the single-value and small-map screenshots.
These are interface examples, not tuning advice.

## Long MAF curves

![A 129-point MAF calibration with a voltage/value list beside its curve](images/curve-editor.png)

Search or browse the map list, keep related maps in visible panes, and edit
numeric cells directly. Curves use the definition's physical axis values.
Long MAF curves use a voltage/value list beside the graph. Click any point to
reach its value, use the wheel to zoom, and Shift+drag to pan. Static breakpoints
stay read-only. Arithmetic and interpolation work across selected points; copy
and paste retain full precision. Axis names and units label the list's two columns.

## Single values and small maps

![Five compact value and state panes visible together](images/single-values.png)

Single-value maps use small wrapping cards, normally about 300 pixels wide,
with standard-size number fields and a single baseline row. Resize individual
cards from their edges or corners. Positions and sizes restore for that calibration.
Full help is available from the
**ⓘ** button beside the title or F1. Type a value and choose **Apply**
or press Enter. Apply/Cancel appear only while typing. Escape or **Cancel**
discards the typed value. The **↶** button beside the baseline
restores the chosen Original ROM, Last save or Comparison ROM value. Undo/Redo
work on applied changes. The field exposes full stored precision when focused.
Invalid or unencodable input stays visible and blocks saving or switching maps
until corrected or canceled. A valid pending value is applied when saving or
switching maps. Single-state maps use a named selector in the same layout.

![A small fuel-control table with a compact surface preview](images/small-map.png)

Small tables use a grid sized to their data, with clear axis bands and readable
cells. Short curves get a curve preview with fewer ticks when space is limited.
Multi-state controls retain their labeled rows without an empty graph.

## Rotatable 3D surfaces

![Intake cam advance table with a compact 3D preview at the upper-right](images/table-preview.png)

**Table with 3D preview** places the surface at the upper-right of the grid,
keeping the table's full height. Small panes temporarily omit the preview to
prioritize the table; expanding restores it. The graph stays on the right, like the
MAF editor. Its controls and linked cell selection work the same way.

Drag the divider at the table's right edge to give the grid or preview more room.
The width is remembered across launches and graph docking. **Fit map** resets it
to the automatic compact preview so a full 16×16 grid can fit again. Short curves
use the same divider, with a wider default preview for readable curve labels.

![An intake cam advance VVT surface showing its raised areas and transitions](images/surface-editor.png)

Axis lines, names and tick values sit directly on the map. Blue is the column
axis, green is the row axis, and amber is the value/height axis. They stay aligned
as you rotate. The preview keeps fewer ticks; the larger graph shows more detail.

Drag to rotate a surface in the direction of your mouse and use the wheel to zoom.
The preview and floating graph use the same controls. Click a point on the
graph to select its grid cell. Resizable panes let you give the grid or graph
more room. **Fit view** frames the entire surface. Pop the graph into a separate
window to use another monitor; closing that window restores the preview.
Floating map, graph and information windows hide when you leave Editor and return
when you come back. The graph stays hidden until a graphable map is selected.

## Map information and changes

![Map information in a separate window](images/details-editor.png)

Click **Map info** inside a map pane, or press **F1**, to open help without
reducing the map's height. Reusing the control brings that map's help forward.
**View → Map layout and display → Map information…**
remains available too. Map info is disabled until a map is selected.
Value units and input axes stay visible above the explanation. Help distinguishes
targets and thresholds from multipliers and signed corrections, with simple
examples. Controls with a guide topic link to it directly. The
[tuning guide](../TUNING_HELP.md) explains common units and related controls.
**Storage details** expands address, data type and dimensions; long content can
be scrolled. Cell tooltips and the status strip show comparison values.
Axis titles and breakpoint headers have a distinct blue-gray band and bright
borders separating them from editable data. Read-only MAF breakpoints use the
same treatment.
**View → Map layout and display → Changes** opens a drawer
of maps that differ from the selected baseline. Select a map there to jump to it.

The **View** menu supports table and graph above/beside one another, plus
table-only and graph-only views. The browser can also pop into a separate
window. Layout preferences are remembered. Open maps and unsaved edits remain
in place when switching to Logger or Recordings.

Drag a map's edges or corners to change only its width and height. Close a pane with **×**, or close
a detached window, to remove that view while keeping the shared ROM edits.
For step-by-step controls and shortcuts, open **Help → Editor guide** in the app
or use the [full editor guide](../EDITOR.md).

## First edit

1. Open **Editor → File → Open ROM…**.
2. Choose a map and check **Map info**. For a single value, type in the field
   and choose **Apply** or Enter; Cancel or Escape discards typing.
   For a table, double-click a cell, enter a value and press Enter. State
   controls use named choices.
3. Review the original/last-save/comparison values and the curve or surface.
   Use Undo/Redo to restore complete operations.
4. Select **Save copy…**. Every save applies its mandatory checksum/integrity policy, uses
   a new filename and verifies the written bytes.

For a rectangular selection, the toolbar supports Set, Add, Multiply, copy/paste
and horizontal, vertical or 2D interpolation. Mode switches use named choices;
each named DTC bit is exposed separately while neighboring bits are preserved.
See the [full editing guide](../EDITOR.md) for shortcuts and exact behavior.

## Compatibility and current limits

Supported profiles: **L33XEH000, L35HEH000, L38KEH000, L39SED000, L39SEE000,
L39SEF000, L3M6EG000, L3M7EF000, L3P4EM000 and L3CGEE000**.

The editor checks the calibration, boot, checksum layout and reference program
signature. Changes to defined tuning controls remain editable; other program
modifications are rejected. Extended/patched ROM layouts are unsupported.
Every export applies its established integrity policy and verifies the saved
bytes. Gen 2 L3CGEE000 validates its native code sum and markers and updates the
established correction word after supported MAP scaling edits. The program
signature guards the surrounding code. See [Gen 2 limits](../GEN2.md).

Static axes are read-only. This version has no patch installer, smoothing,
tune-project history or log-to-map overlay. Offline software checks do not
establish vehicle behavior or the suitability of tuning changes.

[All documentation](../README.md) · [Logging](../LOGGER.md) ·
[Validation](../../dotnet/docs/Validation.md) · [Download SpeedLink](https://github.com/mrphyslaw/SpeedLink/releases)
