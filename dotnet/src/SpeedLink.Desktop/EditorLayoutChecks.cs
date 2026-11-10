using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal async Task<object> ExerciseLayoutAcceptance(string folder, Action<double, double> resize)
    {
        var known = document!.Definition;
        var sample = new TuningTable { Name = "16 × 16 layout check", Category = "Layout check", Kind = "3D", Storage = "float", Units = "psi absolute", Description = "Synthetic layout test data. Not a calibration table for vehicle use.", Format = "0.00",
            Address = 0x38000, Columns = 16, Rows = 16, BigEndian = true, Decode = x => x, Encode = x => x,
            X = new("Engine speed (RPM)", Enumerable.Range(0, 16).Select(i => 500d + i * 500).ToArray()),
            Y = new("Barometric pressure (psi absolute)", Enumerable.Range(0, 16).Select(i => 7d + i * .5).ToArray()) };
        var definition = new TuningDefinition { Calibration = known.Calibration, Model = known.Model, Version = "Layout test", IdentityAddress = known.IdentityAddress, Identity = known.Identity, Tables = known.Tables.Append(sample).ToArray() };
        var snapshot = document.Snapshot(); string path = document.SourcePath;
        document = new(snapshot, path, inspector, codeGuard, [definition]); document.Changed += Document_Changed;
        MapTabs.Items.Clear(); OpenTable(sample);
        document.Apply(Enumerable.Range(0, 256).Select(i => { double x = i % 16 / 15d, y = i / 16 / 15d; return new CellEdit(sample, i, 12 + 24 * Math.Exp(-18 * ((x - .45) * (x - .45) + (y - .6) * (y - .6))) + 7 * Math.Sin(x * 6) * Math.Cos(y * 4)); }), "Synthetic surface fixture");
        SelectCell(8 * 16 + 8);
        var measurements = new List<object>();
        foreach (var size in new[] { (1280d, 720d), (1024d, 700d) })
        {
            resize(size.Item1, size.Item2);
            foreach (var column in MapGrid.Columns) column.Width = new(90);
            MapGrid.RowHeight = 28; UpdateLayout();
            FitMap_Click(this, new()); UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateTableSizing(); UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
            var scroll = Descendants(MapGrid).OfType<ScrollViewer>().First();
            bool wholeMapVisible = scroll.ScrollableWidth <= .5 && scroll.ScrollableHeight <= .5;
            // At narrow sizes, measured text wins over the old fixed 44px
            // column assumption. Verify access rather than forcing smaller text.
            MapGrid.ScrollIntoView(MapGrid.Items[15], MapGrid.Columns[15]);
            await Settle();
            var last = MapGrid.Columns[15].GetCellContent(MapGrid.Items[15]) ?? throw new InvalidOperationException("Last table cell was virtualized.");
            var lastRect = last.TransformToAncestor(MapGrid).TransformBounds(new Rect(last.RenderSize));
            if (lastRect.Right > MapGrid.ActualWidth + .5 || lastRect.Bottom > MapGrid.ActualHeight + .5 ||
                MapGrid.FontSize != 13 || MapGrid.RowHeight < 23 || MapGrid.Columns.Any(c => c.ActualWidth < c.MinWidth - .5) || !XAxisText.IsVisible || !YAxisText.IsVisible || ViewMode != 4 || !GraphPanel.IsVisible || Grid.GetColumn(GraphPanel) != 2 || GraphPanel.ActualHeight > 210 || GraphPanel.ActualWidth < 150 || size.Item1 >= 1280 && !wholeMapVisible)
                throw new InvalidOperationException($"16×16 map does not fit at {size}: scroll {scroll.ScrollableWidth}/{scroll.ScrollableHeight}, grid {MapGrid.ActualWidth}/{MapGrid.ActualHeight}, last {lastRect}, columns {string.Join(',', MapGrid.Columns.Select(c => c.ActualWidth))}.");
            CheckMapHelpPlacement();
            CaptureHost(Path.Combine(folder, $"Fit16x16-{size.Item1:0}x{size.Item2:0}.png"));
            measurements.Add(new { windowWidth = size.Item1, windowHeight = size.Item2, actualWindowWidth = Window.GetWindow(this).ActualWidth, actualWindowHeight = Window.GetWindow(this).ActualHeight, gridWidth = MapGrid.ActualWidth, gridHeight = MapGrid.ActualHeight, rowHeight = MapGrid.RowHeight,
                columnWidth = MapGrid.Columns[0].ActualWidth, horizontalScroll = scroll.ScrollableWidth, verticalScroll = scroll.ScrollableHeight, wholeMapVisible, lastCellVisible = true, previewWidth = GraphPanel.ActualWidth, previewHeight = GraphPanel.ActualHeight });
        }
        await CheckPreviewSplitter(folder, resize);
        // Keep the compact fit checks above at their original window sizes.
        // The axis fixture needs room for the added tab strip so it can test a horizontal bar by itself.
        resize(1280, 720 + WorkspaceTabsBar.ActualHeight + WorkspaceTabsBar.Margin.Top + WorkspaceTabsBar.Margin.Bottom);
        await Settle();
        await CheckTableAxes(sample);
        resize(1280, 720);
        await Settle();
        Graph.CheckSurfaceDrag();
        Graph.CheckSurfaceAxes();
        SelectCell(0); byte[] precise = document.Snapshot(); double exact = document.Read(sample, 0);
        if (!MapGrid.BeginEdit()) throw new InvalidOperationException("Cannot enter a fitted table cell."); UpdateLayout();
        if (MapGrid.Columns[0].GetCellContent(MapGrid.Items[0]) is not TextBox input || input.Text != exact.ToString("R", CultureInfo.InvariantCulture))
            throw new InvalidOperationException("Editing did not restore the full stored precision.");
        if (!FinishEdit() || !precise.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("A no-op cell edit changed ROM bytes.");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        double height = MapGrid.ActualHeight; DetailsButton.IsChecked = true; UpdateLayout();
        if (!DetailsPanel.IsVisible || Math.Abs(MapGrid.ActualHeight - height) > 1) throw new InvalidOperationException("Map information takes space from the table.");
        CaptureWindow(infoWindow!, Path.Combine(folder, "MapInformation.png"));
        DetailsButton.IsChecked = false;
        PopGraph_Click(this, new());
        foreach (var size in new[] { (900d, 260d), (400d, 600d) })
        {
            graphWindow!.Width = size.Item1; graphWindow.Height = size.Item2; graphWindow.UpdateLayout(); Graph.ResetView();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Graph.CheckSurfaceDrag();
            Graph.CheckSurfaceAxes();
            var frame = Graph.SurfaceFrame; var viewport = Graph.SurfaceViewport;
            double fill = Math.Max(frame.Width / viewport.Width, frame.Height / viewport.Height);
            if (fill < .75 || fill > 1 || frame.Left < -.5 || frame.Top < -.5 || frame.Right > viewport.Width + .5 || frame.Bottom > viewport.Height + .5)
                throw new InvalidOperationException("Graph does not fit its viewport: " + frame);
        }
        graphWindow!.Width = 740; graphWindow.Height = 510; graphWindow.UpdateLayout();
        CaptureWindow(graphWindow, Path.Combine(folder, "SurfacePopout.png")); DockGraph_Click(this, new());
        UpdateLayout(); if (ViewMode != 4 || !GraphPanel.IsVisible || GraphPanel.ActualHeight > 210) throw new InvalidOperationException("Docking did not restore the compact preview.");
        SelectCell(8 * 16 + 8); if (Graph.SelectedCell != 8 * 16 + 8) throw new InvalidOperationException("Preview selection did not follow the table.");
        resize(1280, 720); FocusButton.IsChecked = false;
        var maf = document.Definition.Tables.First(t => t.Name.StartsWith("MAF Calibration", StringComparison.Ordinal) && t.Columns > 100);
        OpenTable(maf);
        document.Apply(Enumerable.Range(0, maf.Count).Select(i => new CellEdit(maf, i, 450 * Math.Pow((double)i / (maf.Count - 1), 2.4))), "Synthetic MAF fixture");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
        if (!CurveList || MapGrid.Columns.Count != 2 || !MapGrid.Columns[0].IsReadOnly || MapGrid.Items.Count != maf.Count || ViewMode != 1)
            throw new InvalidOperationException("Long curve did not use breakpoint/value rows.");
        if (XAxisBand.IsVisible || YAxisBand.IsVisible || AxisLabelColumn.ActualWidth != 0 || AxisTitleRow.ActualHeight != 0 ||
            MapGrid.Columns[0].Header?.ToString() != maf.X.Name || MapGrid.Columns[1].Header?.ToString() != maf.Units)
            throw new InvalidOperationException("Long curves did not retain their direct column labels.");
        var inputCell = (DataGridCell)MapGrid.Columns[0].GetCellContent(MapGrid.Items[0]).Parent;
        if (inputCell.BorderThickness.Right < 2 || inputCell.Background is not System.Windows.Media.SolidColorBrush inputBrush ||
            inputBrush.Color != System.Windows.Media.Color.FromRgb(48, 59, 69))
            throw new InvalidOperationException("Read-only curve breakpoints are not visually separated from editable values.");
        BrowserColumn.Width = new(50); UpdateLayout(); LayoutSplitter_Completed(BrowserSplitter, new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false)); UpdateLayout();
        if (BrowserColumn.ActualWidth < 160) throw new InvalidOperationException("Browser divider did not retain a usable width.");
        TableColumn.Width = new(340); UpdateLayout(); LayoutSplitter_Completed(DataSplitter, new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false)); UpdateLayout();
        if (Math.Abs(TableColumn.ActualWidth - 340) > 1) throw new InvalidOperationException("Curve divider width did not persist.");
        byte[] beforePop = document.Snapshot(); PopBrowser_Click(this, new()); browserWindow!.UpdateLayout();
        if (!beforePop.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Moving browser changed ROM edits.");
        Graph.PickCurvePoint(maf.Count - 1);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (SelectedIndices().Single() != maf.Count - 1 || MapGrid.CurrentCell.Item is not EditorRow { Index: var index } || index != maf.Count - 1)
            throw new InvalidOperationException("Curve selection did not reach the final MAF point.");
        Graph.ZoomCurve(.25); if (Graph.CurveVisibleFraction > .3) throw new InvalidOperationException("Curve zoom failed.");
        Graph.ResetView(); CaptureHost(Path.Combine(folder, "Maf129Points.png")); CaptureWindow(browserWindow!, Path.Combine(folder, "MapBrowserPopout.png")); DockBrowser_Click(this, new());
        SelectCell(maf.Count - 1); precise = document.Snapshot(); exact = document.Read(maf, maf.Count - 1);
        if (!MapGrid.BeginEdit()) throw new InvalidOperationException("Cannot edit the last curve point."); UpdateLayout();
        if (MapGrid.Columns[1].GetCellContent(MapGrid.Items[maf.Count - 1]) is not TextBox curveInput || curveInput.Text != exact.ToString("R", CultureInfo.InvariantCulture) || !FinishEdit() || !precise.AsSpan().SequenceEqual(document.Snapshot()))
            throw new InvalidOperationException("Curve editing lost stored precision.");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        MapGrid.SelectedCells.Clear(); foreach (int i in Enumerable.Range(maf.Count - 3, 3)) MapGrid.SelectedCells.Add(new(MapGrid.Items[i], MapGrid.Columns[1]));
        var selectedBefore = SelectedIndices().Select(i => document.Read(maf, i)).ToArray(); OperandBox.Text = "1.1"; Multiply_Click(this, new());
        if (SelectedIndices().Length != 3 || Math.Abs(document.Read(maf, maf.Count - 1) - selectedBefore[2] * 1.1) > .001) throw new InvalidOperationException("Curve bulk scale used the wrong indices.");
        Undo_Click(this, new()); if (!precise.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Curve bulk undo was not byte-exact.");
        Copy_Click(this, new()); Paste_Click(this, new()); if (!precise.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Curve clipboard roundtrip changed values.");
        Interpolate_Click(this, new()); double ratio = (maf.X.Values[^2] - maf.X.Values[^3]) / (maf.X.Values[^1] - maf.X.Values[^3]);
        if (Math.Abs(document.Read(maf, maf.Count - 2) - (selectedBefore[0] * (1 - ratio) + selectedBefore[2] * ratio)) > .001) throw new InvalidOperationException("Curve interpolation used the wrong axis.");
        string output = Path.Combine(folder, "LAYOUT_TEST_ONLY_checksum_verified.bin"); document.SaveNew(output);
        if (inspector.Inspect(File.ReadAllBytes(output)).ChecksumValid != true) throw new InvalidOperationException("Layout editor export checksum failed.");
        FocusButton.IsChecked = true; SetViewMode(2); browserWidth = 230; SaveLayout();
        var saved = EditorLayoutPreferences.Load(layoutPath!);
        if (!saved.BrowserHidden || saved.View != 2 || saved.BrowserWidth != 230) throw new InvalidOperationException("Layout did not persist.");
        curveWidth = 325; previewWidth = 365; PopBrowser_Click(this, new()); PopGraph_Click(this, new()); SaveLayout();
        return new { passed = true, hardwareAccessed = false, synthetic = true, full16x16 = measurements, precisionPreserved = true, mapInfoUsesNoTableSpace = true,
            popoutsPreserveEdits = true, previewResizeBothDirections = true, previewWidthPersisted = true, fitMapResetsPreview = true, surfaceFitsWideAndTallPanes = true, surfaceFollowsDrag = true, surfaceAxesFollowCamera = true, compactPreview = true, axisFrameJoined = true, axisLabelsAligned = true, axisTitlesFrozen = true, selectedHeadersHighlighted = true, mafPoints = maf.Count, mafVoltageReadOnly = true, mafLastPointLinked = true, curveZoom = true,
            mafBulkEditsAndClipboard = true, checksumVerified = true, layoutPersisted = true };
    }
    internal void CheckRestoredLayout()
    {
        if (browserWindow is null || graphWindow is null || !browserWindow.IsVisible || graphWindow.IsVisible || browserWidth != 230 || curveWidth != 325 || previewWidth != 365 || !fitTable)
            throw new InvalidOperationException("Saved popout layout did not restore in a fresh workspace.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private void CaptureHost(string path) => CaptureWindow(Window.GetWindow(this), path);
    private static void CaptureWindow(Window window, string path)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}

public partial class MainWindow
{
    internal async Task<object> ExerciseLayoutOffline(string path, string folder)
    {
        Show(); Navigate(1); EditorPane.OpenRom(path); UpdateLayout();
        if (!navigationIsCollapsed) { NavigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); }
        NavigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); if (NavigationColumn.ActualWidth < 100) throw new InvalidOperationException("Navigation did not expand.");
        NavigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout();
        if (NavigationColumn.ActualWidth > 60) throw new InvalidOperationException("Navigation did not collapse.");
        var result = await EditorPane.ExerciseLayoutAcceptance(folder, (width, height) => { Width = width; Height = height; UpdateLayout(); });
        var restored = new MainWindow(vault, EditorPane.TestInspector, EditorPane.TestGuard);
        try
        {
            restored.Show(); restored.Navigate(1); restored.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            restored.EditorPane.CheckRestoredLayout();
            restored.OpenEditorFile(path);
            restored.EditorPane.OpenTable(restored.EditorPane.Document!.Definition.Tables.First(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice));
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (!restored.EditorPane.GraphFloating || !restored.EditorPane.FloatingVisibilityForTest()[1]) throw new InvalidOperationException("Remembered graph did not return after opening a map.");
        }
        finally { restored.CloseWorkbenchTest(); }
        return new { acceptance = result, freshWorkspaceRestored = true };
    }
}
