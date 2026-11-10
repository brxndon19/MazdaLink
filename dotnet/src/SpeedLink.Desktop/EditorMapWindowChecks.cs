using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal async Task<object> ExerciseMapWindows(string folder)
    {
        var cases = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            cases.Add(name);
            File.WriteAllText(Path.Combine(folder, "MapWindowProgress.json"), System.Text.Json.JsonSerializer.Serialize(new { cases }));
        }
        async Task Idle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        var doc = document!; byte[] original = doc.Snapshot();
        var maps = doc.Definition.Tables.Where(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice).Take(4).ToArray();
        var scalar = doc.Definition.Tables.First(t => t.Count == 1 && !t.IsChoice && t.Storage == "float");
        var first = OpenMap(maps[0]); var second = OpenMap(maps[1]); await Idle();
        Check(mapSlots.Count == 2 && first.MapGrid.IsVisible && second.MapGrid.IsVisible, "Two real maps visible together");
        foreach (var slot in mapSlots)
        {
            var closeBounds = slot.Editor.CloseMapButton.TransformToAncestor(slot.Frame).TransformBounds(new Rect(slot.Editor.CloseMapButton.RenderSize));
            Check(closeBounds.Right <= slot.Frame.ActualWidth + 1, $"Map window commands stay inside their pane ({closeBounds.Right}/{slot.Frame.ActualWidth})");
        }
        Check(ReferenceEquals(first.document, second.document), "One shared document");
        Check(ReferenceEquals(OpenMap(maps[1]), second) && mapSlots.Count == 2, "Opening an existing map focuses its pane");
        Check(!MapTabs.IsVisible && !second.MapTabs.IsVisible, "No mandatory map tabs");
        double font = first.MapGrid.FontSize;
        double width = first.MapGrid.ActualWidth;
        double paneWidth = DockedBounds(first).Width;
        var secondBounds = DockedBounds(second);
        DragPaneEdge(first, MapResizeEdge.Right, -70, 0); await Idle();
        Check(DockedBounds(first).Width < paneWidth && first.MapGrid.FontSize == font && SameBounds(secondBounds, DockedBounds(second)), "Pane edge changes space without shrinking text or resizing other maps");
        DragPaneEdge(first, MapResizeEdge.Right, 70, 0); await Idle();
        Check(first.MapGrid.Columns.All(c => c.ActualWidth >= c.MinWidth - 1) && second.MapGrid.Columns.All(c => c.ActualWidth >= c.MinWidth - 1), "Both panes honor measured cell minimums");
        first.SelectCell(Math.Min(first.table!.Count - 1, first.table.Columns + 2));
        int[] selected = first.SelectedIndices();
        first.CellZoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        double zoom = first.cellZoom;
        first.ExpandMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(!second.MapGrid.IsVisible && first.MapGrid.IsVisible && first.MapGrid.ActualWidth > width, "Expand hides other panes without closing them");
        first.ExpandMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(second.MapGrid.IsVisible && first.cellZoom == zoom && first.SelectedIndices().SequenceEqual(selected), "Restore keeps selection and deliberate zoom");
        for (int i = 0; i < 20; i++) first.CellZoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle();
        Check(first.cellZoom == 1.5 && first.MapGrid.FontSize == 19.5 && !first.CellZoomIn.IsEnabled, "Manual zoom has an upper bound");
        var glyph = new FormattedText("0123456789", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(first.MapGrid.FontFamily, first.MapGrid.FontStyle, first.MapGrid.FontWeight, first.MapGrid.FontStretch), first.MapGrid.FontSize, UiTheme.Text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Check(first.MapGrid.RowHeight >= glyph.Height + 2, "Large text fits within the measured row height");
        for (int i = 0; i < 20; i++) first.CellZoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle(); Check(first.cellZoom == .85 && first.MapGrid.FontSize >= 11 && !first.CellZoomOut.IsEnabled, "Manual zoom cannot pass the readability floor");
        while (first.cellZoom < zoom) first.CellZoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle();
        var firstSnapshot = CaptureMap(first);
        second.ActivateMap(); PopGraph_Click(this, new()); await Idle();
        Check(second.graphWindow is { IsVisible: true } && first.graphWindow is null, "Global graph command follows the active map");
        DockGraph_Click(this, new()); await Idle();
        Check(second.graphWindow is null, "Global graph docking follows the active map");
        first.Graph.ZoomCurve(.5);
        second.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        var secondSlot = mapSlots.Single(s => ReferenceEquals(s.Editor, second));
        Check(secondSlot.Window is { IsVisible: true } && second.MapGrid.IsVisible && second.ScalarPanel.Visibility == Visibility.Collapsed, "Detached map has a real native window");
        var secondWindow = secondSlot.Window ?? throw new InvalidOperationException("Detached window is missing.");
        secondWindow.Width = 510; secondWindow.Height = 430; await Idle();
        Check(second.MapGrid.FontSize >= 11 && second.MapGrid.Columns.All(c => c.ActualWidth >= c.MinWidth - 1), "Small detached window remains readable");
        secondWindow.Width = secondWindow.MinWidth; secondWindow.Height = secondWindow.MinHeight; await Idle();
        var closeAtMinimum = second.CloseMapButton.TransformToAncestor(secondSlot.Frame).TransformBounds(new Rect(second.CloseMapButton.RenderSize));
        Check(closeAtMinimum.Right <= secondSlot.Frame.ActualWidth + 1 && second.MapGrid.FontSize == 13, "Minimum native window keeps controls and text usable");
        Check(second.MapGrid.ActualHeight >= second.MapGrid.ColumnHeaderHeight + second.MapGrid.RowHeight - 1, $"Minimum native window displays data rows ({second.MapGrid.ActualHeight})");
        secondWindow.Width = 510; secondWindow.Height = 430; await Idle();
        second.SelectCell(1); double before = doc.Read(maps[1], 1);
        second.OperandBox.Text = "1"; second.Add_Click(second, new()); await Idle();
        Check(doc.Read(maps[1], 1) != before && UndoButton.IsEnabled && second.UndoButton.IsEnabled, "Detached edit updates shared history");
        second.Undo_Click(second, new()); await Idle();
        Check(original.AsSpan().SequenceEqual(doc.Snapshot()), "Undo from a detached map restores shared ROM bytes");
        second.Redo_Click(second, new()); await Idle();
        Check(doc.Read(maps[1], 1) != before, "Redo across views");
        second.Undo_Click(second, new()); await Idle();
        SetWorkspaceActive(false); await Idle(); Check(!secondWindow.IsVisible, "Detached windows hide outside editor");
        SetWorkspaceActive(true); await Idle(); Check(secondWindow.IsVisible, "Detached windows return with editor");
        second.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(secondSlot.Window is null && second.MapGrid.IsVisible && second.UndockMapButton.Content.ToString() == "Undock", "Dock returns the map without losing it");
        second.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        second.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(secondSlot.Window is null && second.MapGrid.IsVisible, "Repeated detach and dock works");
        var valueEditor = OpenMap(scalar); await Idle();
        valueEditor.ScalarInput.Text = "not a value";
        second.SelectCell(0); second.OperandBox.Text = "1"; second.Add_Click(second, new()); await Idle();
        Check(valueEditor.scalarDraft && valueEditor.ScalarInput.Text == "not a value", "Another map's edit preserves a pending scalar entry");
        Check(!FinishAllMapEdits() && !CloseMap(valueEditor), "Invalid entry blocks save preflight and closing its pane");
        valueEditor.ScalarCancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(FinishAllMapEdits() && CloseMap(valueEditor), "Cancel invalid entry then close safely");
        doc.Undo(); await Idle();
        Check(original.AsSpan().SequenceEqual(doc.Snapshot()), "Closing a pane keeps shared edits until undo");
        first.ActivateMap(); first.SelectCell(0); first.MapGrid.Focus();
        Check(first.MapGrid.BeginEdit(), "Begin a real grid edit in a tiled pane"); await Idle();
        var input = first.MapGrid.Columns[0].GetCellContent(first.MapGrid.Items[0]) as TextBox ?? throw new InvalidOperationException("Grid editor did not open.");
        input.Text = "NaN";
        Check(!first.FinishEdit(), "Invalid grid entry blocks export preflight");
        int oldBaseline = first.BaselineBox.SelectedIndex; first.BaselineBox.SelectedIndex = oldBaseline == 0 ? 1 : 0;
        Check(first.BaselineBox.SelectedIndex == oldBaseline && input.Text == "NaN", "Baseline switching cannot discard invalid grid input");
        int oldView = first.ViewMode; first.PaneViewBox.SelectedIndex = 3;
        Check(first.ViewMode == oldView && first.PaneViewBox.SelectedIndex == oldView && input.Text == "NaN", "View switching cannot hide invalid grid input");
        second.SelectCell(0); second.OperandBox.Text = "1"; second.Add_Click(second, new()); await Idle();
        Check(first.gridDraft && input.Text == "NaN", "Another pane cannot erase a pending grid entry");
        first.MapGrid.CancelEdit(DataGridEditingUnit.Row); second.Undo_Click(second, new()); await Idle();
        OpenMap(maps[2]); OpenMap(maps[3]); await Idle();
        Check(mapSlots.Count == 4 && mapSlots.All(s => PaneGrips(s).Count() == 8), "Four maps have independent edge and corner handles");
        double topHeight = mapSlots.First(s => ReferenceEquals(s.Editor, first)).Frame.ActualHeight;
        var otherSizes = mapSlots.Where(s => !ReferenceEquals(s.Editor, first)).ToDictionary(s => s.Editor, s => DockedBounds(s.Editor));
        DragPaneEdge(first, MapResizeEdge.Bottom, 0, -20); await Idle();
        Check(DockedBounds(first).Height <= topHeight && first.MapGrid.FontSize == 13 * zoom && otherSizes.All(p => SameBounds(p.Value, DockedBounds(p.Key))), "Height resizing preserves other maps and chosen text size");
        var curve = doc.Definition.Tables.FirstOrDefault(t => t.Rows == 1 && t.Columns > 16 && !t.IsChoice);
        if (curve is not null)
        {
            var curveEditor = OpenMap(curve); await Idle();
            curveEditor.SelectCell(curve.Count - 1); curveEditor.Graph.ZoomCurve(.5); await Idle();
            Check(curveEditor.CurveList && curveEditor.Graph.SelectedCell == curve.Count - 1 && curveEditor.MapGrid.Columns[0].IsReadOnly, "Long curve keeps list editing and graph selection");
            Check(CloseMap(curveEditor), "Close long curve without discarding document");
        }
        var choice = doc.Definition.Tables.First(t => t.IsChoice);
        var choiceEditor = OpenMap(choice); await Idle();
        Check(choiceEditor.ScalarMap ? choiceEditor.ScalarChoice.IsVisible : choiceEditor.ChoiceBox.IsVisible, "Named state controls work in map panes");
        Check(CloseMap(choiceEditor), "State pane closes safely");
        first.ActivateMap(); first.RestoreMap(first, firstSnapshot); await Idle();
        second.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        SaveMapWorkspace();
        string calibration = doc.Definition.Calibration;
        var saved = MapWorkspacePreferences.Load(mapsPath!).Single(p => p.Calibration == calibration).Pages.Single();
        Check(saved.Maps.Length == mapSlots.Count && saved.Maps.Any(p => p.Floating) && saved.Maps[0].Zoom == zoom, "Workspace persistence includes maps, bounds, zoom, and selection");
        CaptureHost(Path.Combine(folder, "MultipleMaps.png"));
        var fresh = new Window { Width = 1280, Height = 850, ShowInTaskbar = false, Title = "SpeedLink · layout restart check" };
        var restarted = new EditorWorkspace(inspector, codeGuard) { Definitions = Definitions };
        fresh.Content = restarted; restarted.ConfigureLayout(layoutPath!, () => true);
        try
        {
            fresh.Show(); restarted.OpenRom(doc.SourcePath); await Idle();
            var restoredState = restarted.mapSlots.Select(s => new
            {
                name = s.Editor.table?.Name, s.Editor.cellZoom, selected = s.Editor.SelectedIndices(),
                rawSelection = s.Editor.MapGrid.SelectedCells.Select(c => new { item = c.Item?.GetType().Name, display = c.Column?.DisplayIndex, valid = c.IsValid }),
                loaded = s.Editor.MapGrid.IsLoaded, visible = s.Editor.MapGrid.IsVisible, frame = s.Frame.Visibility.ToString(), count = s.Editor.MapGrid.Columns.Count
            });
            File.WriteAllText(Path.Combine(folder, "RestoredMapState.json"), System.Text.Json.JsonSerializer.Serialize(restoredState));
            Check(restarted.mapSlots.Count == mapSlots.Count, "Fresh workspace restores all maps after opening the same calibration");
            Check(restarted.mapSlots.Any(s => s.Window is { IsVisible: true }), "Fresh workspace restores detached window");
            Check(restarted.mapSlots[0].Editor.cellZoom == zoom && restarted.mapSlots[0].Editor.SelectedIndices().SequenceEqual(firstSnapshot.Selection), $"Fresh workspace restores zoom and selection (zoom {restarted.mapSlots[0].Editor.cellZoom}/{zoom}; cells {string.Join(',', restarted.mapSlots[0].Editor.SelectedIndices())}/{string.Join(',', firstSnapshot.Selection)})");
        }
        finally { restarted.CloseOfflineTest(); restarted.PrepareHostClose(); fresh.Close(); }
        Check(original.AsSpan().SequenceEqual(doc.Snapshot()), "Presentation actions leave ROM bytes unchanged");
        var export = Path.Combine(folder, "MAP_PANES_VERIFIED_COPY.bin");
        Check(FinishAllMapEdits(), "All panes commit before export"); doc.SaveNew(export);
        Check(inspector.Inspect(File.ReadAllBytes(export)).ChecksumValid == true, "Multi-map export passes mandatory integrity gate");
        first.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        var primaryPopout = mapSlots.Single(s => s.Window is not null && ReferenceEquals(s.Editor.table, maps[0]));
        Check(primaryPopout.Window is { IsVisible: true } && ReferenceEquals(primaryPopout.Editor.document, doc), "The primary map can detach without detaching the ROM document");
        primaryPopout.Editor.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(primaryPopout.Window is null && primaryPopout.Editor.MapGrid.IsVisible, "Primary map docks back into the workspace");
        CloseMap(primaryPopout.Editor); OpenMap(maps[0]); await Idle();
        Check(ReferenceEquals(table, maps[0]) && ReferenceEquals(document, doc), "Main pane remains usable after primary map detaches");
        var floatingScalar = OpenMap(scalar); floatingScalar.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        var scalarSlot = mapSlots.Single(s => ReferenceEquals(s.Editor, floatingScalar));
        var scalarWindow = scalarSlot.Window ?? throw new InvalidOperationException("Scalar window did not detach.");
        scalarWindow.Width = 300; scalarWindow.Height = 500; await Idle();
        var unitsBounds = floatingScalar.ScalarUnits.TransformToAncestor(floatingScalar.DataLayout).TransformBounds(new Rect(floatingScalar.ScalarUnits.RenderSize));
        Check(unitsBounds.Right <= floatingScalar.DataLayout.ActualWidth + 1 && floatingScalar.ScalarInput.IsVisible, "Scalar units remain reachable in the minimum window");
        floatingScalar.ScalarInput.Text = "NaN"; scalarWindow.Close(); await Idle();
        Check(scalarSlot.Window == scalarWindow && scalarWindow.IsVisible, "Native window close preserves and blocks invalid input");
        floatingScalar.ScalarCancel_Click(floatingScalar, new()); scalarWindow.Close(); await Idle();
        Check(!mapSlots.Contains(scalarSlot) && ReferenceEquals(document, doc), "Native window close removes only the view");
        ClearMapWindows(); MapTabs.Items.Clear(); OpenMap(maps[0]);
        var remaining = OpenMap(maps[1]); remaining.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        var remainingWindow = mapSlots.Single(s => ReferenceEquals(s.Editor, remaining)).Window;
        CloseMap(this); await Idle();
        Check(remainingWindow is { IsVisible: true } && mapSlots.Any(s => ReferenceEquals(s.Editor, remaining) && s.Window == remainingWindow), "Closing the final docked map leaves detached maps in place");
        Check(NoMapPanel.IsVisible && MapWorkspace.IsVisible && document == doc, "An empty dock keeps its browse prompt while maps float");
        if (calibration == "L38KEH000") await CaptureMapWindowGallery(folder);
        ClearMapWindows(); MapTabs.Items.Clear(); await Idle();
        Check(NoMapPanel.IsVisible && document == doc, "Closing all maps retains ROM and browse prompt");
        cases.AddRange(await ExerciseCompactScalarPanes(folder));
        cases.AddRange(await ExerciseIndependentMapResize(folder));
        cases.AddRange(await ExerciseWorkspaceTabs(folder));
        return new { passed = true, cases, calibration, hardwareAccessed = false, synthetic = true };
    }
    private async Task CaptureMapWindowGallery(string folder)
    {
        ClearMapWindows(); MapTabs.Items.Clear();
        var timing = document!.Definition.Tables.First(t => t.Name == "Closed Loop Max Ignition Timing");
        var vvt = document.Definition.Tables.First(t => t.Name == "Intake Cam Advance (VVT)");
        var left = OpenMap(timing); var right = OpenMap(vvt);
        left.SetViewMode(2); right.SetViewMode(3); FocusButton.IsChecked = true;
        document.Apply(Enumerable.Range(0, timing.Count).Select(i => new CellEdit(timing, i, 38 - i / timing.Columns * 1.6 + Math.Sin(i % timing.Columns * .25) * 8)), "Synthetic timing display");
        document.Apply(Enumerable.Range(0, vvt.Count).Select(i => new CellEdit(vvt, i, Math.Max(0, 30 - Math.Abs(i % vvt.Columns - 4) * 4 - i / vvt.Columns * .6))), "Synthetic VVT display");
        left.ActivateMap(); left.SelectCell(timing.Columns + 2);
        EditorStatus.Text = "Synthetic display examples · two maps share one ROM";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout();
        await Settle();
        await Task.Delay(100);
        await Settle();
        CaptureHost(Path.Combine(folder, "MapWindowsGallery.png"));
        var galleryMetrics = mapSlots.Select(s => new
        {
            frame = s.Frame.ActualWidth, editor = s.Editor.ActualWidth, map = s.Editor.MapWorkspace.ActualWidth, data = s.Editor.DataLayout.ActualWidth,
            close = s.Editor.CloseMapButton.TransformToAncestor(s.Frame).TransformBounds(new Rect(s.Editor.CloseMapButton.RenderSize)).ToString(),
            scrollViewport = mapDockScroll.ViewportWidth, dock = mapDock.ActualWidth
        });
        File.WriteAllText(Path.Combine(folder, "MapGalleryMetrics.json"), System.Text.Json.JsonSerializer.Serialize(galleryMetrics));
        right.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var window = mapSlots.Single(s => ReferenceEquals(s.Editor, right)).Window!;
        CaptureWindow(window, Path.Combine(folder, "DetachedMapGallery.png"));
        right.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        document.Undo(); document.Undo();
    }
}

public partial class MainWindow
{
    internal async Task<object> ExerciseMapWindowsOffline(string path, string folder)
    {
        Show(); Navigate(1); EditorPane.OpenRom(path); UpdateLayout();
        return await EditorPane.ExerciseMapWindows(folder);
    }
}
