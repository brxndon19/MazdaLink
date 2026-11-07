using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private EditorLayoutPreferences preferences = new();
    private string? layoutPath;
    private Func<bool>? navigationCollapsed;
    private Window? browserWindow, graphWindow, infoWindow;
    private double browserWidth = 200, curveWidth = 300, tableFraction = .6;
    private double? previewWidth;
    private bool fitTable, restoringLayout, layoutClosing, resizingData;
    private bool workspaceActive = true;
    internal void SetWorkspaceActive(bool active)
    {
        workspaceActive = active; UpdateFloatingVisibility();
        if (mapOwner is null)
            foreach (var slot in mapSlots.Where(s => !ReferenceEquals(s.Editor, this)))
            {
                slot.Editor.SetWorkspaceActive(active);
                if (slot.Window is { } window) { if (active) window.Show(); else window.Hide(); }
            }
    }
    private void UpdateFloatingVisibility()
    {
        if (layoutClosing) return;
        SetVisible(browserWindow, workspaceActive && !FocusButton.IsChecked);
        SetVisible(graphWindow, workspaceActive && table is { Count: > 1, IsChoice: false });
        SetVisible(infoWindow, workspaceActive && table is not null && DetailsButton.IsChecked);
        static void SetVisible(Window? window, bool visible)
        {
            if (window is null || window.IsVisible == visible) return;
            if (visible) window.Show(); else window.Hide();
        }
    }
    internal int ViewMode { get; private set; }
    internal bool CurveList => table is { Rows: 1, Columns: > 16, IsChoice: false };
    internal bool BrowserFloating => browserWindow is not null;
    internal bool GraphFloating => graphWindow is not null;
    internal void ConfigureLayout(string path, Func<bool> getNavigationCollapsed)
    {
        layoutPath = path; navigationCollapsed = getNavigationCollapsed; preferences = EditorLayoutPreferences.Load(path);
        mapsPath = Path.ChangeExtension(path, ".maps.json"); mapProfiles = MapWorkspacePreferences.Load(mapsPath);
        restoringLayout = true; browserWidth = preferences.BrowserWidth; curveWidth = preferences.CurveWidth; tableFraction = preferences.TableFraction; previewWidth = preferences.PreviewWidth; fitTable = preferences.FitTable;
        FocusButton.IsChecked = preferences.BrowserHidden; SetViewMode(preferences.FitTable && preferences.View == 2 ? 4 : preferences.View);
        Loaded += RestoreFloating;
    }
    private void RestoreFloating(object sender, RoutedEventArgs e)
    {
        Loaded -= RestoreFloating;
        if (preferences.BrowserFloating) PopBrowser_Click(this, new());
        if (preferences.GraphFloating) PopGraph_Click(this, new());
        restoringLayout = false; ApplyLayout();
    }
    internal void SaveLayout()
    {
        if (mapOwner is null) SaveMapWorkspace();
        if (layoutPath is null || restoringLayout || layoutClosing) return;
        preferences = preferences with { BrowserHidden = FocusButton.IsChecked, BrowserFloating = browserWindow is not null,
            GraphFloating = graphWindow is not null, BrowserWidth = browserWidth, CurveWidth = curveWidth, TableFraction = tableFraction, PreviewWidth = previewWidth, FitTable = fitTable,
            NavigationCollapsed = navigationCollapsed?.Invoke() ?? true, View = ViewMode,
            BrowserBounds = Bounds(browserWindow, preferences.BrowserBounds), GraphBounds = Bounds(graphWindow, preferences.GraphBounds) };
        try { preferences.Save(layoutPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { EditorStatus.ToolTip = "Layout settings could not be saved: " + error.Message; }
    }
    internal void SaveNavigationLayout()
    {
        if (!restoringLayout) { SaveLayout(); return; }
        if (layoutPath is null || layoutClosing) return;
        preferences = preferences with { NavigationCollapsed = navigationCollapsed?.Invoke() ?? true };
        try { preferences.Save(layoutPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { EditorStatus.ToolTip = "Layout settings could not be saved: " + error.Message; }
    }
    private static PanelBounds Bounds(Window? window, PanelBounds fallback) => window is null ? fallback : new(window.Left, window.Top, window.Width, window.Height);
    internal void PrepareHostClose()
    {
        SaveLayout(); layoutClosing = true; mapSaveTimer?.Stop();
        if (mapOwner is null) ClearMapWindows();
        browserWindow?.Close(); graphWindow?.Close(); infoWindow?.Close();
    }
    private Window Floating(string title, double width, double height, PanelBounds? bounds = null)
    {
        var owner = Window.GetWindow(this);
        var result = new Window { Owner = owner, Title = title, Width = width, Height = height, MinWidth = 260, MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        result.SetResourceReference(StyleProperty, typeof(Window));
        if (bounds?.Left is double left && bounds.Top is double top)
        {
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (screen.IntersectsWith(new Rect(left, top, Math.Min(100, bounds.Width), 40)))
            { result.WindowStartupLocation = WindowStartupLocation.Manual; result.Left = left; result.Top = top; result.Width = bounds.Width; result.Height = bounds.Height; }
        }
        return result;
    }
    private void Layout_Changed(object sender, RoutedEventArgs e)
    {
        if (BrowserColumn is null || DetailsPanel is null || FocusButton is null || ChangesButton is null) return;
        ApplyLayout(); SaveLayout();
    }
    private void MapInfo_Click(object sender, RoutedEventArgs e) => ShowMapInformation();
    private void ShowMapInformation()
    {
        if (table is null) return;
        DetailsButton.IsChecked = true;
        if (infoWindow?.WindowState == WindowState.Minimized) infoWindow.WindowState = WindowState.Normal;
        infoWindow?.Activate();
    }

    private void RefreshGuideTopic(TuningDefinition? definition, TuningTable? map)
    {
        string? topic = map?.Name switch
        {
            "BCS Correction Mode" or "BCS DC Base" or "BCS Throttle Input" or
            "BCS DC Adjustment - Load Target Correction - Initial" or "BCS DC Adjustment - Load Target Correction - Incremental" or
            "BCS DC Adjustment Boost Target Correction (Boost Targeting Mode)" or "BCS DC Adjustment - Boost Limit Exceeded" or
            "BCS DC Accumulated Correction Limit - Increase" or "BCS DC Accumulated Correction Limit - Decrease" or
            "BCS DC Adjustment Limit - Increase" or "BCS DC Adjustment Limit - Decrease" or
            "Overboost Pressure Limit - Base" or "Overboost Pressure Limit Hysteresis" or "Boost Limit Barometer Adjustment" or
            "MAP Targets (Boost Targeting Mode)" or "Load Targets (Alternate Mode)"
                => "Boost and wastegate control",
            "Overboost Protection Fuel Cut" or "Overload Protection Fuel Cut" or "Rev Limiter Normal" or
            "Cold Coolant Rev Limiter - 1" or "Cold Coolant Rev Limiter - 2" or
            "Absolute Rev Limiter - 1" or "Absolute Rev Limiter - 2" or "Absolute Rev Limiter - 3"
                => "Load limits and fuel cut",
            "Knock Retard Additive" or "Knock Retard Multiplier"
                => "Knock reference and spark timing",
            "Injector Base Pulsewidth - 1" or "Injector Base Pulsewidth - 2" or "Injector Pulsewidth Offset" or "Injector Short Pulsewidth Correction"
                => "Injector pulsewidth",
            "MAF Calibration" or "MAF Calibration 1" or "MAF Calibration 2" or "Closed Loop AFR Target" or
            "MAP Scale Additive - DTC Reporting" or "MAP Scale Additive - ECU Boost Mgmt" or "MAP Scale Additive - Logging" or
            "MAP Scale Base Multiplier - Control and Diagnostics" or "MAP Scale Base Multiplier - Logging" or
            "MAP Scale Second Multiplier - Control and Diagnostics" or "MAP Scale Second Multiplier - Logging" or
            "MAP Scale Subtractive Offset - Control and Diagnostics" or "MAP Scale Subtractive Offset - Logging" or
            "Open Loop Part Throttle AFR Target Normal" or "Open Loop Part Throttle AFR Target Knock Present" or "Open Loop Part Throttle AFR Target Overload"
                => "Sensor scaling and feedback",
            _ => null
        };
        // A local definition can replace the help for the same map name.
        if (topic is not { } section || definition is null || map is null ||
            !TuningDefinition.Known.Any(known => known.Calibration == definition.Calibration && known.Tables.Any(bundled =>
                bundled.Name == map.Name && bundled.Category == map.Category && bundled.Description == map.Description)))
        {
            GuideTopic.Visibility = Visibility.Collapsed;
            GuideLink.Tag = null;
            return;
        }
        GuideLinkText.Text = "Tuning guide · " + section;
        GuideLink.Tag = section;
        GuideTopic.Visibility = Visibility.Visible;
    }

    private void GuideLink_Click(object sender, RoutedEventArgs e)
    {
        if (GuideLink.Tag is string heading)
            MainWindow.HelpWindow("Tuning guide", heading, infoWindow).Show();
    }

    private void ApplyLayout()
    {
        bool hidden = FocusButton.IsChecked;
        BrowserColumn.Width = new(hidden || browserWindow is not null ? 0 : browserWidth);
        BrowserDivider.Width = new(hidden || browserWindow is not null ? 0 : 5);
        BrowserPanel.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        BrowserSplitter.Visibility = hidden || browserWindow is not null ? Visibility.Collapsed : Visibility.Visible;
        bool changes = ChangesButton.IsChecked; ChangesColumn.Width = new(changes ? 220 : 0); ChangesDivider.Width = new(changes ? 5 : 0);
        ChangesPanel.Visibility = ChangesSplitter.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
        if (DetailsButton.IsChecked && infoWindow is null)
        {
            var window = Floating("SpeedLink · Map information", 540, 400); infoWindow = window;
            EditorLayout.Children.Remove(DetailsPanel); DetailsPanel.Visibility = Visibility.Visible; window.Content = DetailsPanel;
            window.Closed += (_, _) => { window.Content = null; EditorLayout.Children.Add(DetailsPanel); DetailsPanel.Visibility = Visibility.Collapsed; infoWindow = null; DetailsButton.IsChecked = false; };
            UpdateFloatingVisibility();
        }
        else if (!DetailsButton.IsChecked && infoWindow is not null) infoWindow.Close();
        ApplyView();
    }
    internal void SetViewMode(int mode)
    {
        ViewMode = Math.Clamp(mode, 0, 4);
        if (DataLayout is null) return;
        ApplyView(); SaveLayout();
    }
    private void ApplyView()
    {
        bool surface = table is { Rows: > 1, Columns: > 1, IsChoice: false };
        bool graphable = table is { Count: > 1, IsChoice: false };
        bool floating = graphWindow is not null, preview = ViewMode == 4 && graphable && !floating && (mapOwner is null && mapSlots.Count <= 1 || DataLayout.ActualWidth >= 460);
        bool beside = ViewMode == 1 && graphable && !floating, graphOnly = ViewMode == 3 && graphable && !floating;
        bool below = ViewMode == 0 && graphable && !floating;
        bool vertical = preview || beside;
        PreviewItem.Header = surface ? "Table with 3D preview" : "Table with curve preview";
        PreviewItem.IsChecked = ViewMode == 4 && graphable; PreviewItem.IsEnabled = graphable;
        BelowItem.IsEnabled = BesideItem.IsEnabled = GraphItem.IsEnabled = graphable;
        BelowItem.IsChecked = ViewMode == 0; BesideItem.IsChecked = ViewMode == 1; TableItem.IsChecked = ViewMode == 2; GraphItem.IsChecked = ViewMode == 3;
        Grid.SetRow(GraphPanel, preview || beside || graphOnly ? 0 : 2); Grid.SetColumn(GraphPanel, preview || beside ? 2 : 0);
        TableRow.Height = new(below ? tableFraction : 1, GridUnitType.Star); GraphRow.Height = below ? new(1 - tableFraction, GridUnitType.Star) : new(0);
        DataDividerRow.Height = new(below ? 5 : 0); DataDividerColumn.Width = new(vertical ? 7 : 0);
        TableColumn.MinWidth = vertical ? Math.Min(200, Math.Max(80, DataLayout.ActualWidth * .45)) : 0; GraphColumn.MinWidth = vertical ? Math.Min(160, Math.Max(80, DataLayout.ActualWidth * .35)) : 0;
        GraphColumn.MaxWidth = preview ? 800 : double.PositiveInfinity;
        TableColumn.Width = beside && CurveList ? new(Math.Min(curveWidth, Math.Max(180, DataLayout.ActualWidth - 200))) : new(beside ? tableFraction : 1, GridUnitType.Star);
        var thumbnail = TablePreviewSize.Calculate(DataLayout.ActualWidth, DataLayout.ActualHeight, table?.Columns ?? 1,
            MapGrid.RowHeaderWidth + AxisLabelColumn.Width.Value + SystemParameters.VerticalScrollBarWidth, previewWidth, MinimumCellWidth());
        double previewSize = !surface && previewWidth is null ? Math.Min(360, Math.Max(260, DataLayout.ActualWidth * .32)) : thumbnail.Width;
        GraphColumn.Width = preview ? new(previewSize) : beside ? new(1 - tableFraction, GridUnitType.Star) : new(0);
        var compact = table is null ? null : CompactMapSize.Calculate(table.Columns, table.Rows, MapGrid.RowHeaderWidth, AxisLabelColumn.Width.Value, AxisTitleRow.Height.Value, table.IsChoice);
        WorkspaceSlackColumn.Width = new(0);
        if (preview && compact is { } natural)
        {
            double automatic = surface ? TablePreviewSize.Calculate(DataLayout.ActualWidth, DataLayout.ActualHeight, table!.Columns, MapGrid.RowHeaderWidth + AxisLabelColumn.Width.Value).Width
                : Math.Min(360, Math.Max(260, DataLayout.ActualWidth * .32));
            double section = Math.Max(367, Math.Min(DataLayout.ActualWidth, natural.Width + automatic + 7));
            double maximum = Math.Max(160, Math.Min(800, section - 207));
            double graphWidth = Math.Clamp(previewWidth ?? automatic, 160, maximum);
            TableColumn.Width = new(section - graphWidth - 7, GridUnitType.Star); GraphColumn.Width = new(graphWidth, GridUnitType.Star); GraphColumn.MaxWidth = maximum;
            WorkspaceSlackColumn.Width = new(Math.Max(0, DataLayout.ActualWidth - section), GridUnitType.Star);
            thumbnail = new(graphWidth, Math.Min(DataLayout.ActualHeight, Math.Clamp(graphWidth * .8 + 28, 156, previewWidth is null ? 208 : 480)));
        }
        GraphPanel.Height = preview ? surface ? thumbnail.Height : Math.Min(DataLayout.ActualHeight, Math.Max(220, thumbnail.Height)) : double.NaN; GraphPanel.VerticalAlignment = preview ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        Graph.SetCompactPreview(preview); FitViewButton.Content = preview ? "↺" : "Fit view";
        if (preview) GraphTitle.Text = surface ? "3D preview" : "Curve preview";
        else GraphTitle.Text = surface ? "3D · drag to rotate · wheel to zoom" : "Curve · wheel to zoom · Shift+drag to pan · click a point";
        TablePanel.Visibility = table is null || graphOnly || ScalarMap ? Visibility.Collapsed : Visibility.Visible;
        ScalarPanel.Visibility = ScalarMap ? Visibility.Visible : Visibility.Collapsed;
        EditBar.Visibility = ScalarMap ? Visibility.Collapsed : Visibility.Visible;
        MapWorkspace.RowDefinitions[1].Height = ScalarMap ? new(0) : GridLength.Auto;
        GraphPanel.Visibility = graphable && (floating || ViewMode != 2 && (ViewMode != 4 || preview)) ? Visibility.Visible : Visibility.Collapsed;
        UpdateFloatingVisibility();
        Grid.SetRow(DataSplitter, vertical ? 0 : 1); Grid.SetColumn(DataSplitter, vertical ? 1 : 0);
        DataSplitter.Width = vertical ? 7 : double.NaN; DataSplitter.Height = vertical ? double.NaN : 5;
        DataSplitter.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        DataSplitter.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        DataSplitter.ResizeDirection = vertical ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        DataSplitter.Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS;
        DataSplitter.Visibility = below || vertical ? Visibility.Visible : Visibility.Collapsed;
        UpdateCompactMap();
        if (preview && TablePanel.VerticalAlignment == VerticalAlignment.Top)
        {
            DataSplitter.Height = Math.Min(DataLayout.ActualHeight, Math.Max(TablePanel.Height, GraphPanel.Height));
            DataSplitter.VerticalAlignment = VerticalAlignment.Top;
        }
        UpdateTableSizing();
        RefreshMapControls();
        if (mapOwner is null) RefreshMapCommandState();
    }
    private void ViewMenu_Click(object sender, RoutedEventArgs e) { if (sender is MenuItem { Tag: string mode } && CommandMap.FinishEdit()) { CommandMap.fitTable = false; CommandMap.SetViewMode(int.Parse(mode, System.Globalization.CultureInfo.InvariantCulture)); } }
    private void FitMap_Click(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(CommandMap, this)) { CommandMap.FitMap_Click(sender, e); return; }
        if (!FinishEdit()) return;
        if (table is null) { ShowMapBrowser(); return; }
        autoFitCells = true;
        var root = MapRoot;
        if (root.mapSlots.Count(s => s.Editor.table is not null) > 1)
        {
            var slot = root.mapSlots.First(s => ReferenceEquals(s.Editor, this));
            if (slot.Window is null) { root.expandedMap = slot; root.ArrangeMaps(); }
            root.FocusButton.IsChecked = true;
        }
        previewWidth = null; FocusButton.IsChecked = true; ChangesButton.IsChecked = false; DetailsButton.IsChecked = false; fitTable = true;
        SetViewMode(CurveList ? 1 : table is { Count: > 1, IsChoice: false } ? 4 : 2); Graph.ResetView();
        Dispatcher.BeginInvoke(new Action(() => { UpdateLayout(); UpdateTableSizing(); SaveLayout(); }));
    }
    private void HideBrowser_Click(object sender, RoutedEventArgs e) => FocusButton.IsChecked = true;
    private void ShowBrowser_Click(object sender, RoutedEventArgs e) => ShowMapBrowser();
    private void NoMapAction_Click(object sender, RoutedEventArgs e)
    {
        if (document is null) Open_Click(sender, e);
        else ShowMapBrowser();
    }
    private void ShowMapBrowser()
    {
        FocusButton.IsChecked = false; ApplyLayout();
        if (browserWindow is not null)
        {
            if (browserWindow.WindowState == WindowState.Minimized) browserWindow.WindowState = WindowState.Normal;
            browserWindow.Activate();
        }
        SearchBox.Focus(); SaveLayout();
    }
    private void PopBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (browserWindow is not null) { FocusButton.IsChecked = false; browserWindow.Activate(); return; }
        var window = Floating("SpeedLink · Maps", 340, 650, preferences.BrowserBounds); browserWindow = window;
        EditorLayout.Children.Remove(BrowserPanel); window.Content = BrowserPanel;
        window.Closed += (_, _) =>
        {
            preferences = preferences with { BrowserBounds = Bounds(window, preferences.BrowserBounds) };
            window.Content = null; EditorLayout.Children.Add(BrowserPanel); browserWindow = null; FocusButton.IsChecked = false; ApplyLayout(); SaveLayout();
        };
        FocusButton.IsChecked = false; ApplyLayout(); SaveLayout();
    }
    private void DockBrowser_Click(object sender, RoutedEventArgs e) { browserWindow?.Close(); FocusButton.IsChecked = false; }
    private void PopGraph_Click(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(CommandMap, this)) { CommandMap.PopGraph_Click(sender, e); return; }
        if (ScalarMap || table?.IsChoice == true) return;
        if (graphWindow is not null) { graphWindow.Activate(); return; }
        var window = Floating("SpeedLink · Graph", 700, 520, preferences.GraphBounds); graphWindow = window;
        DataLayout.Children.Remove(GraphPanel); window.Content = GraphPanel;
        window.Closed += (_, _) =>
        {
            preferences = preferences with { GraphBounds = Bounds(window, preferences.GraphBounds) };
            window.Content = null; DataLayout.Children.Add(GraphPanel); graphWindow = null;
            if (ViewMode == 2) SetViewMode(table?.Rows > 1 ? 4 : 0); ApplyLayout(); SaveLayout();
        };
        ApplyView(); SaveLayout();
    }
    private void DockGraph_Click(object sender, RoutedEventArgs e) => CommandMap.graphWindow?.Close();
    private void LayoutSplitter_Completed(object sender, DragCompletedEventArgs e)
    {
        resizingData = false;
        if (e.Canceled) { ApplyLayout(); return; }
        if (ReferenceEquals(sender, BrowserSplitter)) browserWidth = Math.Clamp(BrowserColumn.ActualWidth, 160, 480);
        else if (ViewMode == 4 && table is { Count: > 1, IsChoice: false }) previewWidth = Math.Clamp(GraphColumn.ActualWidth, 160, 800);
        else if (ViewMode == 0) tableFraction = Math.Clamp(TableRow.ActualHeight / Math.Max(1, TableRow.ActualHeight + GraphRow.ActualHeight), .2, .8);
        else if (ViewMode == 1 && CurveList) curveWidth = Math.Clamp(TableColumn.ActualWidth, 180, 600);
        else if (ViewMode == 1) tableFraction = Math.Clamp(TableColumn.ActualWidth / Math.Max(1, TableColumn.ActualWidth + GraphColumn.ActualWidth), .2, .8);
        ApplyLayout(); SaveLayout();
    }
    private void DataSplitter_Started(object sender, DragStartedEventArgs e) => resizingData = true;
    private void TableViewport_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateCompactMap(); UpdateTableSizing(); }
    private void DataLayout_SizeChanged(object sender, SizeChangedEventArgs e) { if (resizingData) return; if (ViewMode == 4) ApplyView(); else { UpdateCompactMap(); UpdateTableSizing(); } }
    private void UpdateCompactMap()
    {
        var size = table is null ? null : CompactMapSize.Calculate(table.Columns, table.Rows, MapGrid.RowHeaderWidth, AxisLabelColumn.Width.Value, AxisTitleRow.Height.Value, table.IsChoice);
        bool fillPreview = ViewMode == 4 && graphWindow is null && table is { IsChoice: false, Count: > 1 };
        TablePanel.HorizontalAlignment = size is null || fillPreview ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        TablePanel.VerticalAlignment = size is null ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        TablePanel.Width = fillPreview ? double.NaN : size?.Width ?? double.NaN; TablePanel.Height = size?.Height ?? double.NaN;
        TablePanel.MaxWidth = Math.Max(1, TableViewport.ActualWidth); TablePanel.MaxHeight = Math.Max(1, TableViewport.ActualHeight);
    }
    private void UpdateTableSizing()
    {
        if (table is null || MapGrid.ActualWidth < 10 || MapGrid.Columns.Count == 0) return;
        QueueAxisGeometry();
        MapGrid.FontSize = 13 * cellZoom;
        double minimum = MinimumCellWidth();
        if (CurveList)
        {
            foreach (var column in MapGrid.Columns) { column.MinWidth = minimum; column.Width = autoFitCells ? new(1, DataGridLengthUnitType.Star) : new(Math.Max(minimum, 120 * cellZoom)); }
            MapGrid.RowHeight = Math.Ceiling(25 * cellZoom); return;
        }
        // Leave room for a transient horizontal scrollbar so it cannot keep the
        // vertical scrollbar alive and prevent an otherwise fitting grid from settling.
        double availableHeight = MapGrid.ActualHeight - (fitTable ? SystemParameters.HorizontalScrollBarHeight : 0);
        var size = ReadableTableFit.Calculate(MapGrid.ActualWidth, availableHeight, table.Columns, table.Rows, MapGrid.RowHeaderWidth, MapGrid.ColumnHeaderHeight, measuredCellWidth, MapGrid.FontSize, cellZoom, autoFitCells);
        double rowHeight = size.RowHeight;
        if (table.Rows * rowHeight + MapGrid.ColumnHeaderHeight > MapGrid.ActualHeight)
            size = ReadableTableFit.Calculate(MapGrid.ActualWidth - SystemParameters.VerticalScrollBarWidth, availableHeight, table.Columns, table.Rows, MapGrid.RowHeaderWidth, MapGrid.ColumnHeaderHeight, measuredCellWidth, MapGrid.FontSize, cellZoom, autoFitCells);
        bool compact = CompactMapSize.Calculate(table.Columns, table.Rows, MapGrid.RowHeaderWidth, 0, 0) is not null;
        double compactCell = Math.Clamp(Math.Floor((MapGrid.ActualWidth - MapGrid.RowHeaderWidth - 4) / table.Columns), Math.Min(64, minimum), previewWidth is not null && ViewMode == 4 ? 160 : table.Columns <= 4 ? 112 : 88);
        foreach (var column in MapGrid.Columns) { column.MinWidth = minimum; column.Width = new(Math.Max(minimum, compact && autoFitCells ? compactCell : size.ColumnWidth)); }
        MapGrid.RowHeight = compact ? Math.Max(size.RowHeight, 28 * cellZoom) : size.RowHeight;
    }
    private double MinimumCellWidth()
    {
        var typeface = new Typeface(MapGrid.FontFamily, MapGrid.FontStyle, MapGrid.FontWeight, MapGrid.FontStretch);
        if (!double.IsFinite(measuredCellWidth))
        {
            var strings = MapGrid.Items.OfType<EditorRow>().SelectMany(r => r.Values).Concat(MapGrid.Columns.Select(c => c.Header?.ToString() ?? "")).Distinct();
            measuredCellWidth = strings.Select(s => new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, MapGrid.FontSize, UiTheme.Text, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace).DefaultIfEmpty(0).Max();
        }
        return Math.Ceiling(Math.Max(44 * cellZoom, measuredCellWidth + 14 * cellZoom));
    }
}
