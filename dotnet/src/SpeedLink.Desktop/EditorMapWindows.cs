using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private sealed class MapSlot(EditorWorkspace editor, Border frame)
    {
        internal readonly EditorWorkspace Editor = editor;
        internal readonly Border Frame = frame;
        internal Window? Window;
        internal bool DockOnClose;
        internal bool Pinned;
        internal PanelBounds Bounds = DefaultMapBounds;
        internal MapDockBounds? DockBounds;
    }
    private EditorWorkspace? mapOwner, activeMap;
    private static readonly PanelBounds DefaultMapBounds = new(Width: 840, Height: 650);
    private readonly List<MapSlot> mapSlots = [];
    private readonly Grid mapDock = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer mapDockScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private MapSlot? expandedMap;
    private double dockColumnFraction = .5, dockRowFraction = .5;
    private int dockColumns = 2;
    private double[] dockRowWeights = [];
    private double[] scalarColumnWidths = [];
    private bool scalarDock;
    private bool resizingDock;
    private bool independentMapLayout;
    private Action? cancelPaneGesture;
    private int scalarDockColumns = 1;
    private const double ScalarCardWidth = 300;
    private double cellZoom = 1;
    private bool autoFitCells = true, updatingMapControls, rebuildingDock, restoringMaps, disposingMaps, gridDraft;
    private double measuredCellWidth = double.NaN;
    private string? mapsPath;
    private MapWorkspacePreferences[] mapProfiles = [];
    private DispatcherTimer? mapSaveTimer;
    private EditorWorkspace MapRoot => mapOwner ?? this;
    private EditorWorkspace CommandMap => mapOwner is null ? activeMap ?? this : this;
    private static string MapKey(TuningTable t) => t.Address.ToString("X", CultureInfo.InvariantCulture) + ":" + t.Name;

    private void InitializeMapWindows(EditorWorkspace? owner)
    {
        mapOwner = owner;
        MapGrid.FontSize = 13;
        MapTabs.Visibility = Visibility.Collapsed;
        MapWorkspace.PreviewMouseDown += (_, _) => ActivateMap();
        MapWorkspace.GotKeyboardFocus += (_, _) => ActivateMap();
        Point? dragStart = null; MapDockBounds? moveStart = null;
        PaneDragArea.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { dragStart = null; ExpandMap_Click(this, new()); e.Handled = true; }
            else { dragStart = e.GetPosition(MapRoot.mapDock); moveStart = null; PaneDragArea.CaptureMouse(); }
        };
        void EndMove()
        {
            dragStart = null; moveStart = null; PaneDragArea.ReleaseMouseCapture();
            MapRoot.cancelPaneGesture = null; MapRoot.resizingDock = false; MapRoot.UpdateDockExtent(); MapRoot.QueueMapSave();
        }
        PaneDragArea.PreviewMouseLeftButtonUp += (_, _) => EndMove();
        PaneDragArea.LostMouseCapture += (_, _) => { if (dragStart is not null) EndMove(); };
        PaneDragArea.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) { if (dragStart is not null) EndMove(); return; }
            var root = MapRoot;
            if (dragStart is not Point start || table is null) return;
            var point = e.GetPosition(root.mapDock);
            if (moveStart is null && (point - start).Length < 8) return;
            var selected = table;
            var slot = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
            if (slot is null) return;
            if (slot.Window is null && new Rect(root.mapDockScroll.RenderSize).Contains(e.GetPosition(root.mapDockScroll)))
            {
                if (root.expandedMap is not null) return;
                root.FreezeMapPositions(); moveStart ??= slot.DockBounds; root.resizingDock = true;
                root.cancelPaneGesture = () => { if (moveStart is not null) { slot.DockBounds = moveStart; PlaceMapFrame(slot, moveStart); } EndMove(); };
                root.MoveMapPane(slot, moveStart!, point.X - start.X, point.Y - start.Y); return;
            }
            EndMove();
            root.DetachMap(this);
            var moved = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor.table, selected));
            if (moved?.Window is not Window window) return;
            try { window.DragMove(); } catch (InvalidOperationException) { return; }
            var origin = root.mapDockScroll.PointToScreen(new Point());
            var corner = root.mapDockScroll.PointToScreen(new Point(root.mapDockScroll.ActualWidth, root.mapDockScroll.ActualHeight));
            if (new Rect(origin, corner).Contains(window.PointToScreen(Mouse.GetPosition(window)))) { moved.DockOnClose = true; window.Close(); }
        };
        if (owner is not null)
        {
            EditorToolbar.Visibility = Visibility.Collapsed;
            WorkspaceRoot.RowDefinitions[0].Height = new(0);
            EditorLayout.ColumnDefinitions[2].MinWidth = 0;
            WorkspaceRoot.Margin = new(3);
            FocusButton.IsChecked = true;
            MapsButton.Visibility = Visibility.Collapsed;
            FileText.Visibility = Visibility.Collapsed;
            ExportIntegrityText.Visibility = EditorStatus.Visibility = Visibility.Collapsed;
            return;
        }
        Grid.SetColumn(mapDockScroll, 2); mapDockScroll.Content = mapDock;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && cancelPaneGesture is { } cancel) { cancel(); e.Handled = true; } };
        EditorLayout.Children.Remove(MapWorkspace);
        var frame = MapFrame(MapWorkspace);
        mapSlots.Add(new(this, frame));
        EditorLayout.Children.Add(mapDockScroll);
        mapDockScroll.SizeChanged += (_, _) => UpdateDockExtent();
        mapDockScroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) UpdateDockExtent(); };
        ArrangeMaps();
    }
    private Border MapFrame(UIElement content)
    {
        var surface = new Grid(); surface.Children.Add(content);
        var border = new Border { Child = surface, BorderThickness = new(1), Padding = new(3), CornerRadius = new(3), ClipToBounds = true };
        AddPaneResizeGrips(border, surface);
        border.PreviewMouseDown += (_, _) => MapRoot.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Frame, border))?.Editor.ActivateMap();
        border.SetResourceReference(Border.BackgroundProperty, "ShellBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        border.SizeChanged += (_, _) =>
        {
            if (content is FrameworkElement element && border.ActualWidth > 8)
                element.MaxWidth = border.ActualWidth - 8;
        };
        return border;
    }
    private void ActivateMap()
    {
        var root = MapRoot;
        root.activeMap = this;
        foreach (var slot in root.mapSlots)
        {
            slot.Frame.SetResourceReference(Border.BorderBrushProperty, ReferenceEquals(slot.Editor, this) ? "AmberBrush" : "LineBrush");
        }
        int z = 0;
        foreach (var slot in root.mapSlots.OrderBy(s => Panel.GetZIndex(s.Frame)).Where(s => !ReferenceEquals(s.Editor, this))) Panel.SetZIndex(slot.Frame, z++);
        var current = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
        if (current is not null) Panel.SetZIndex(current.Frame, z);
        root.RefreshMapCommandState();
        root.QueueMapSave();
    }
    private void RefreshMapCommandState()
    {
        if (mapOwner is not null) return;
        var current = CommandMap; bool graphable = current.table is { Count: > 1, IsChoice: false };
        FitMapButton.IsEnabled = DetailsButton.IsEnabled = current.table is not null;
        PreviewItem.IsEnabled = BelowItem.IsEnabled = BesideItem.IsEnabled = GraphItem.IsEnabled = graphable;
        PreviewItem.IsChecked = graphable && current.ViewMode == 4; BelowItem.IsChecked = current.ViewMode == 0;
        BesideItem.IsChecked = current.ViewMode == 1; TableItem.IsChecked = current.ViewMode == 2; GraphItem.IsChecked = current.ViewMode == 3;
    }
    private void MenuMapInfo_Click(object sender, RoutedEventArgs e) => CommandMap.ShowMapInformation();
    /// <summary>Each map is a view over the same document, not a second ROM copy.</summary>
    internal EditorWorkspace OpenMap(TuningTable selected) => OpenMap(selected, pinned: true);
    private EditorWorkspace OpenMap(TuningTable selected, bool pinned)
    {
        var root = MapRoot;
        if (!ReferenceEquals(root, this)) return root.OpenMap(selected, pinned);
        var existing = mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor.table, selected));
        if (existing is not null)
        {
            if (expandedMap is not null && !ReferenceEquals(expandedMap, existing)) { expandedMap = null; ArrangeMaps(); }
            existing.Window?.Activate(); existing.Editor.ActivateMap(); existing.Editor.MapGrid.Focus();
            return existing.Editor;
        }
        if (mapSlots.Count(s => s.Editor.table is not null) >= 24) throw new InvalidOperationException("Close a map before opening another; up to 24 maps can remain open.");
        MapSlot slot;
        if (table is null)
        {
            slot = mapSlots[0];
            slot.Pinned = pinned;
            OpenTable(selected);
        }
        else slot = AddMapSlot(selected, pinned, mapSlots.Count);
        expandedMap = null; ArrangeMaps(); slot.Editor.ActivateMap(); QueueMapSave();
        return slot.Editor;
    }
    private MapSlot AddMapSlot(TuningTable selected, bool pinned, int index)
    {
        if (document is null) throw new InvalidOperationException("Open a ROM first.");
        var editor = new EditorWorkspace(inspector, codeGuard, this)
        { document = document, Definitions = Definitions, workspaceActive = workspaceActive };
        document.Changed += editor.Document_Changed;
        var slot = new MapSlot(editor, MapFrame(editor)) { Pinned = pinned };
        mapSlots.Insert(index, slot);
        editor.OpenTable(selected);
        return slot;
    }
    private void BrowseMap(TuningTable selected)
    {
        var preview = mapSlots.FirstOrDefault(s => s.Editor.table is not null && s.Window is null && !s.Pinned);
        if (preview is null || mapSlots.Any(s => ReferenceEquals(s.Editor.table, selected)))
        {
            var opened = OpenMap(selected, pinned: false);
            var slot = mapSlots.First(s => ReferenceEquals(s.Editor, opened));
            if (slot.Window is null) slot.Frame.BringIntoView();
            return;
        }

        var editor = preview.Editor;
        if (!editor.FinishEdit()) { ShowPendingEntry(preview); return; }
        if (!independentMapLayout && mapSlots.Count(s => s.Window is null && s.Editor.table is not null) > 1)
        {
            var expanded = expandedMap;
            expandedMap = null;
            if (expanded is not null) ArrangeMaps();
            FreezeMapPositions();
            expandedMap = ReferenceEquals(expanded, preview) ? preview : null;
        }
        else if (!ReferenceEquals(expandedMap, preview)) expandedMap = null;
        editor.graphWindow?.Close();
        editor.infoWindow?.Close();
        editor.OpenTable(selected);
        // The hidden per-pane selector must no longer be able to select the previous map.
        foreach (var item in editor.MapTabs.Items.OfType<TabItem>().Where(item => !ReferenceEquals(item.Tag, selected)).ToArray())
            editor.MapTabs.Items.Remove(item);
        ArrangeMaps(); editor.ActivateMap(); preview.Frame.BringIntoView(); QueueMapSave();
    }
    private void PinMap_Click(object sender, RoutedEventArgs e)
    {
        var root = MapRoot;
        var slot = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
        if (slot is null || slot.Window is not null || table is null) return;
        if (slot.Pinned)
        {
            var preview = root.mapSlots.FirstOrDefault(s => s.Editor.table is not null && !s.Pinned && s.Window is null);
            if (preview is not null) { preview.Pinned = true; preview.Editor.RefreshPinButton(); }
        }
        slot.Pinned = !slot.Pinned;
        RefreshPinButton(); root.QueueMapSave();
    }
    private void ArrangeMaps()
    {
        if (mapOwner is not null || rebuildingDock) return;
        rebuildingDock = true;
        try
        {
            // Keep existing views parented: rebuilding the container would end
            // keyboard focus and could discard an unfinished cell edit.
            foreach (var splitter in mapDock.Children.OfType<GridSplitter>().ToArray()) mapDock.Children.Remove(splitter);
            mapDock.ColumnDefinitions.Clear(); mapDock.RowDefinitions.Clear();
            bool emptyDock = !mapSlots.Any(s => s.Window is null && s.Editor.table is not null);
            var docked = mapSlots.Where(s => s.Window is null && (s.Editor.table is not null || emptyDock && ReferenceEquals(s.Editor, this))).ToArray();
            var visible = expandedMap is not null ? docked.Where(s => ReferenceEquals(s, expandedMap)).ToArray() : docked;
            foreach (var slot in mapSlots.Where(s => s.Window is null)) slot.Frame.Visibility = visible.Contains(slot) ? Visibility.Visible : Visibility.Collapsed;
            foreach (var slot in mapSlots) RefreshPaneResizeGrips(slot);
            if (independentMapLayout)
            {
                ArrangeIndependentMaps(visible, emptyDock);
                return;
            }
            mapDockScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scalarDock = expandedMap is null && visible.Length > 0 && visible.All(s => s.Editor.ScalarMap);
            int columns = scalarDock ? ScalarColumns(visible.Length) : visible.Length <= 1 || mapDockScroll.ActualWidth is > 0 and < 540 ? 1 : dockColumns;
            scalarDockColumns = columns;
            int rows = Math.Max(1, (visible.Length + columns - 1) / columns);
            double available = Math.Max(1, (mapDockScroll.ViewportWidth > 0 ? mapDockScroll.ViewportWidth : mapDockScroll.ActualWidth) - (columns - 1) * 7);
            var widths = Enumerable.Range(0, columns).Select(c => scalarColumnWidths.Length == columns ? scalarColumnWidths[c] : ScalarCardWidth).ToArray();
            double widthScale = Math.Min(1, available / widths.Sum());
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) mapDock.ColumnDefinitions.Add(new() { Width = new(7) });
                mapDock.ColumnDefinitions.Add(scalarDock
                    ? new() { Width = new(Math.Max(220, widths[c] * widthScale), GridUnitType.Star), MinWidth = 220 }
                    : new() { Width = new(columns == 1 ? 1 : c == 0 ? dockColumnFraction : 1 - dockColumnFraction, GridUnitType.Star), MinWidth = 260 });
            }
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) mapDock.RowDefinitions.Add(new() { Height = new(7) });
                double weight = dockRowWeights.Length == rows ? dockRowWeights[r] : rows == 2 ? r == 0 ? dockRowFraction : 1 - dockRowFraction : 1;
                bool scalarOnly = visible.Skip(r * columns).Take(columns).All(s => s.Editor.ScalarMap);
                // Values start at their content height. A deliberate divider resize
                // still restores its saved proportions, including for scalar rows.
                mapDock.RowDefinitions.Add(new() { Height = scalarOnly && dockRowWeights.Length != rows ? GridLength.Auto : new(weight, GridUnitType.Star), MinHeight = scalarOnly ? 100 : 260 });
            }
            for (int i = 0; i < visible.Length; i++)
            {
                var slot = visible[i];
                slot.Frame.Width = slot.Frame.Height = double.NaN; slot.Frame.Margin = new(0);
                slot.Frame.HorizontalAlignment = HorizontalAlignment.Stretch; slot.Frame.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetRow(slot.Frame, (i / columns) * 2); Grid.SetColumn(slot.Frame, (i % columns) * 2);
                if (!mapDock.Children.Contains(slot.Frame)) mapDock.Children.Add(slot.Frame);
                bool expanded = ReferenceEquals(slot, expandedMap);
                slot.Editor.ExpandMapButton.Content = slot.Editor.ScalarMap ? expanded ? "⤡" : "⤢" : expanded ? "Restore" : "Expand";
                System.Windows.Automation.AutomationProperties.SetName(slot.Editor.ExpandMapButton, expanded ? "Restore map layout" : "Expand map");
            }
            UpdateDockExtent();
        }
        finally { rebuildingDock = false; }
    }
    private int ScalarColumns(int count)
    {
        double available = mapDockScroll.ViewportWidth > 0 ? mapDockScroll.ViewportWidth : mapDockScroll.ActualWidth;
        return dockColumns == 1 ? 1 : Math.Min(count, Math.Clamp((int)((available + 7) / (ScalarCardWidth + 7)), 1, 6));
    }
    private void UpdateDockExtent()
    {
        if (resizingDock) return;
        if (independentMapLayout) { UpdateIndependentExtent(); return; }
        int rows = Math.Max(1, (mapDock.RowDefinitions.Count + 1) / 2);
        if (scalarDock && !rebuildingDock && ScalarColumns(mapSlots.Count(s => s.Window is null && s.Editor.table is not null)) != scalarDockColumns) { ArrangeMaps(); return; }
        if (mapDockScroll.ViewportWidth is > 0 and < 100000)
        {
            if (scalarDock)
            {
                var widths = Enumerable.Range(0, scalarDockColumns).Select(c => scalarColumnWidths.Length == scalarDockColumns ? scalarColumnWidths[c] : ScalarCardWidth).ToArray();
                double scale = Math.Min(1, Math.Max(1, mapDockScroll.ViewportWidth - (scalarDockColumns - 1) * 7) / widths.Sum());
                for (int c = 0; c < widths.Length; c++) mapDock.ColumnDefinitions[c * 2].Width = new(Math.Max(220, widths[c] * scale), GridUnitType.Star);
            }
            mapDock.Width = scalarDock ? Math.Min(mapDockScroll.ViewportWidth, mapDock.ColumnDefinitions.Sum(c => c.Width.Value)) : mapDockScroll.ViewportWidth;
        }
        mapDock.Height = Math.Max(0, Math.Max(mapDockScroll.ActualHeight - 2, mapDock.RowDefinitions.Sum(r => r.MinHeight) + (rows - 1) * 7));
        if (mapSlots.Count(s => s.Window is null && s.Editor.table is not null) > 1 && expandedMap is null && !rebuildingDock)
        {
            bool narrow = mapDockScroll.ActualWidth < 540;
            if (!scalarDock && (narrow && mapDock.ColumnDefinitions.Count == 3 || !narrow && mapDock.ColumnDefinitions.Count == 1 && dockColumns == 2)) ArrangeMaps();
        }
    }
    private void ArrangeMaps_Click(object sender, RoutedEventArgs e)
    {
        var root = MapRoot; root.expandedMap = null;
        root.independentMapLayout = false;
        foreach (var slot in root.mapSlots) slot.DockBounds = null;
        root.dockColumns = sender is MenuItem { Tag: "1" } ? 1 : 2;
        root.dockColumnFraction = root.dockRowFraction = .5;
        root.dockRowWeights = [];
        root.scalarColumnWidths = [];
        root.ArrangeMaps(); root.QueueMapSave();
    }
    private void ExpandMap_Click(object sender, RoutedEventArgs e)
    {
        var root = MapRoot; var slot = root.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
        if (slot is null) return;
        if (slot.Window is not null) { slot.Window.WindowState = slot.Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return; }
        root.expandedMap = ReferenceEquals(root.expandedMap, slot) ? null : slot;
        root.ArrangeMaps(); ActivateMap();
    }
    private void UndockMap_Click(object sender, RoutedEventArgs e)
    {
        var slot = MapRoot.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
        if (slot?.Window is not null) { slot.DockOnClose = true; slot.Window.Close(); } else MapRoot.DetachMap(this);
    }
    private void DetachMap(EditorWorkspace editor)
    {
        if (!editor.FinishEdit()) return;
        var slot = mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, editor));
        if (slot is null || editor.table is null) return;
        if (slot.Window is not null) { slot.Window.Activate(); return; }
        slot.Pinned = true;
        // The primary view stays in the main editor; move its state to an identical
        // shared-document view before detaching, so closing it cannot close the ROM.
        if (ReferenceEquals(editor, this))
        {
            var state = CaptureMap(this);
            var tableToMove = table!;
            MapTabs.Items.Clear();
            slot.DockBounds = null;
            slot.Pinned = false;
            slot = AddMapSlot(tableToMove, true, 1);
            slot.DockBounds = state.DockBounds;
            slot.Bounds = state.Bounds;
            editor = slot.Editor;
            RestoreMap(editor, state);
        }
        mapDock.Children.Remove(slot.Frame);
        slot.Frame.Width = slot.Frame.Height = double.NaN; slot.Frame.Margin = new(0);
        slot.Frame.HorizontalAlignment = HorizontalAlignment.Stretch; slot.Frame.VerticalAlignment = VerticalAlignment.Stretch;
        var window = Floating("SpeedLink · " + editor.table!.Name, slot.Bounds.Width, slot.Bounds.Height, slot.Bounds);
        window.ShowActivated = !restoringMaps;
        if (editor.ScalarMap && slot.Bounds.Width == 840 && slot.Bounds.Height == 650) { window.Width = 320; window.Height = 200; }
        window.MinWidth = editor.ScalarMap ? 280 : 300; window.MinHeight = editor.ScalarMap ? 200 : 330; window.ShowInTaskbar = true;
        slot.Window = window; window.Content = slot.Frame; editor.RefreshMapControls();
        window.LocationChanged += (_, _) => QueueMapSave();
        window.SizeChanged += (_, _) => QueueMapSave();
        window.Closing += (_, args) =>
        {
            if (!disposingMaps && !editor.FinishEdit()) args.Cancel = true;
        };
        window.Closed += (_, _) =>
        {
            slot.Bounds = Bounds(window, slot.Bounds); window.Content = null; slot.Window = null;
            editor.RefreshMapControls();
            if (!disposingMaps)
            {
                expandedMap = null;
                if (!slot.DockOnClose) { RemoveMapSlot(slot); activeMap = mapSlots.Select(s => s.Editor).FirstOrDefault(p => p.table is not null) ?? this; activeMap.ActivateMap(); }
                slot.DockOnClose = false;
                // Leave the visuals disconnected until input teardown dismisses
                // tooltip owners from the old native window. Reparenting inside
                // Closed can leave a popup referring to its destroyed handle.
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (layoutClosing || disposingMaps || !mapSlots.Contains(slot) || slot.Window is not null) return;
                    ArrangeMaps(); editor.ActivateMap(); QueueMapSave();
                }));
            }
        };
        expandedMap = null; ArrangeMaps(); editor.ActivateMap();
        if (workspaceActive) window.Show();
        QueueMapSave();
    }
    private void CloseMap_Click(object sender, RoutedEventArgs e) => MapRoot.CloseMap(this);
    private bool CloseMap(EditorWorkspace editor)
    {
        if (!editor.FinishEdit()) return false;
        var slot = mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, editor)); if (slot is null) return true;
        if (ReferenceEquals(editor, this))
        {
            var next = mapSlots.FirstOrDefault(s => !ReferenceEquals(s.Editor, this) && s.Window is null);
            if (next is not null)
            {
                if (!next.Editor.FinishEdit()) return false;
                var state = CaptureMap(next.Editor); var selected = next.Editor.table;
                RemoveMapSlot(next); MapTabs.Items.Clear();
                slot.DockBounds = state.DockBounds; slot.Pinned = state.Pinned; slot.Bounds = state.Bounds;
                if (selected is not null) { OpenTable(selected); RestoreMap(this, state with { Floating = false }); }
            }
            else { MapTabs.Items.Clear(); slot.DockBounds = null; slot.Pinned = false; slot.Bounds = DefaultMapBounds; }
        }
        else RemoveMapSlot(slot);
        expandedMap = null; activeMap = mapSlots.Select(s => s.Editor).FirstOrDefault(p => p.table is not null) ?? this;
        ArrangeMaps(); activeMap.ActivateMap(); QueueMapSave(); return true;
    }
    private void RemoveMapSlot(MapSlot slot)
    {
        bool wasDisposing = disposingMaps; disposingMaps = true;
        slot.Editor.PrepareHostClose(); slot.Window?.Close(); mapDock.Children.Remove(slot.Frame);
        slot.Frame.Child = null; slot.Editor.document!.Changed -= slot.Editor.Document_Changed; mapSlots.Remove(slot);
        disposingMaps = wasDisposing;
    }
    private void ClearMapWindows()
    {
        if (mapOwner is not null) return;
        cancelPaneGesture?.Invoke(); cancelPaneGesture = null; resizingDock = false;
        disposingMaps = true;
        foreach (var slot in mapSlots.Where(s => !ReferenceEquals(s.Editor, this)).ToArray()) RemoveMapSlot(slot);
        independentMapLayout = false;
        foreach (var slot in mapSlots) { slot.DockBounds = null; slot.Pinned = false; slot.Bounds = DefaultMapBounds; }
        disposingMaps = false; expandedMap = null; activeMap = this; ArrangeMaps();
    }
    private bool FinishAllMapEdits()
    {
        var root = MapRoot;
        foreach (var slot in root.mapSlots.ToArray())
            if (!slot.Editor.FinishEdit())
            {
                root.ShowPendingEntry(slot);
                return false;
            }
        return true;
    }
    private void ShowPendingEntry(MapSlot slot)
    {
        if (Window.GetWindow(this) is MainWindow main) main.OpenEditorFile();
        SetWorkspaceActive(true);
        if (expandedMap is not null && !ReferenceEquals(expandedMap, slot)) { expandedMap = null; ArrangeMaps(); }
        EditorStatus.Text = slot.Editor.PendingEntryStatus;
        slot.Editor.ActivateMap(); slot.Window?.Activate();
    }
    private string PendingEntryStatus => "Correct or cancel the pending entry in " + table?.Name + " before continuing.";
    private void ClearPendingEntryStatus()
    {
        var status = MapRoot.EditorStatus;
        if (status.Text == PendingEntryStatus) status.Text = "Choose a map to edit. Static axes are read-only.";
    }
    private void RefreshMapControls()
    {
        if (PaneCaption is null) return;
        PaneCaption.Text = table?.Name ?? "Choose a map"; PaneCaption.ToolTip = PaneCaption.Text;
        PaneCaption.TextWrapping = ScalarMap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        PaneCaption.TextTrimming = ScalarMap ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        bool detached = MapRoot.mapSlots.Any(s => ReferenceEquals(s.Editor, this) && s.Window is not null);
        UndockMapButton.Content = ScalarMap ? detached ? "↙" : "↗" : detached ? "Dock" : "Undock";
        System.Windows.Automation.AutomationProperties.SetName(UndockMapButton, detached ? "Dock map" : "Undock map");
        UndockMapButton.ToolTip = detached ? "Dock this map back in the editor" : "Move this map into its own resizable window";
        UndockMapButton.Padding = ExpandMapButton.Padding = CloseMapButton.Padding = ScalarMap ? new(5, 2, 5, 2) : new(8, 3, 8, 3);
        foreach (var button in new[] { UndockMapButton, ExpandMapButton, CloseMapButton, ScalarInfoButton })
        { button.MinWidth = 0; button.Width = ScalarMap ? 24 : double.NaN; }
        bool expanded = ReferenceEquals(MapRoot.expandedMap?.Editor, this);
        ExpandMapButton.Content = ScalarMap ? expanded ? "⤡" : "⤢" : expanded ? "Restore" : "Expand";
        CloseMapButton.IsEnabled = UndockMapButton.IsEnabled = ExpandMapButton.IsEnabled = table is not null;
        RefreshPinButton();
        updatingMapControls = true;
        AutoFitBox.IsChecked = autoFitCells; CellZoomText.Text = cellZoom.ToString("P0", CultureInfo.InvariantCulture);
        PaneViewBox.SelectedIndex = ViewMode; CellZoomOut.IsEnabled = cellZoom > .85; CellZoomIn.IsEnabled = cellZoom < 1.5;
        var controls = table is { Count: > 1 } ? Visibility.Visible : Visibility.Collapsed;
        AutoFitBox.Visibility = CellZoomOut.Visibility = CellZoomIn.Visibility = CellZoomText.Visibility = PaneViewBox.Visibility = controls;
        ScalarInfoButton.Visibility = ScalarMap ? Visibility.Visible : Visibility.Collapsed;
        MapInfoButton.Visibility = ValueUnitsText.Visibility = ScalarMap ? Visibility.Collapsed : Visibility.Visible;
        CellText.Visibility = ScalarMap ? Visibility.Collapsed : Visibility.Visible;
        if (mapOwner is not null) WorkspaceRoot.RowDefinitions[3].Height = new(ScalarMap ? 0 : 24);
        PaneViewBox.IsEnabled = table is { IsChoice: false };
        updatingMapControls = false;
    }
    private void RefreshPinButton()
    {
        var slot = MapRoot.mapSlots.FirstOrDefault(s => ReferenceEquals(s.Editor, this));
        bool pinned = slot?.Pinned == true;
        PinMapButton.IsChecked = pinned;
        PinMapButton.IsEnabled = slot is { Window: null } && table is not null;
        PinMapButton.ToolTip = slot?.Window is not null ? "Undocked maps stay pinned" :
            pinned ? "Unpin this map to reuse its pane while browsing" : "Pin this map to keep it open while browsing";
        System.Windows.Automation.AutomationProperties.SetName(PinMapButton, pinned ? "Unpin map" : "Pin map");
    }
    private void CellFit_Changed(object sender, RoutedEventArgs e)
    {
        if (updatingMapControls || AutoFitBox is null) return;
        autoFitCells = AutoFitBox.IsChecked == true; UpdateTableSizing(); QueueMapSave();
    }
    private void CellZoom_Click(object sender, RoutedEventArgs e)
    {
        cellZoom = Math.Round(Math.Clamp(cellZoom + (ReferenceEquals(sender, CellZoomOut) ? -.05 : .05), .85, 1.5), 2);
        MapGrid.FontSize = 13 * cellZoom; MapGrid.ColumnHeaderHeight = Math.Ceiling(26 * cellZoom);
        measuredCellWidth = double.NaN;
        RefreshAxes(); RefreshMapControls(); UpdateCompactMap(); UpdateTableSizing(); QueueMapSave();
    }
    private void PaneView_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (updatingMapControls || PaneViewBox.SelectedIndex < 0) return;
        if (!FinishEdit()) { RefreshMapControls(); return; }
        SetViewMode(PaneViewBox.SelectedIndex); QueueMapSave();
    }

    private void QueueMapSave()
    {
        var root = MapRoot;
        if (root.restoringMaps || root.disposingMaps || root.layoutClosing || root.mapsPath is null) return;
        root.mapSaveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => { root.mapSaveTimer!.Stop(); root.SaveMapWorkspace(); }, root.Dispatcher);
        root.mapSaveTimer.Stop(); root.mapSaveTimer.Start();
    }
    private MapPanePreferences CaptureMap(EditorWorkspace editor)
    {
        var slot = mapSlots.First(s => ReferenceEquals(s.Editor, editor));
        var scroll = Descendants(editor.MapGrid).OfType<ScrollViewer>().FirstOrDefault();
        return editor.Graph.CaptureView(new MapPanePreferences
        {
            Key = editor.table is null ? "" : MapKey(editor.table), Zoom = editor.cellZoom, AutoFit = editor.autoFitCells, FitTable = editor.fitTable,
            View = editor.ViewMode, Baseline = Math.Max(0, editor.BaselineBox.SelectedIndex), Selection = editor.SelectedIndices(),
            HorizontalOffset = scroll?.HorizontalOffset ?? 0, VerticalOffset = scroll?.VerticalOffset ?? 0,
            PreviewWidth = editor.previewWidth, TableFraction = editor.tableFraction, Floating = slot.Window is not null,
            Bounds = Bounds(slot.Window, slot.Bounds), DockBounds = independentMapLayout ? slot.DockBounds : null,
            ZOrder = Panel.GetZIndex(slot.Frame), Pinned = slot.Pinned
        });
    }
    private void SaveMapWorkspace()
    {
        if (mapOwner is not null) { MapRoot.SaveMapWorkspace(); return; }
        if (mapsPath is null || document is null || restoringMaps || disposingMaps) return;
        mapSaveTimer?.Stop();
        if (workspacePages.Count == 0) return;
        CaptureActivePage();
        var state = new MapWorkspacePreferences { Calibration = document.Definition.Calibration,
            Pages = workspacePages.ToArray(), SelectedPage = workspacePages[currentPage].Name };
        mapProfiles = mapProfiles.Where(p => p.Calibration != state.Calibration).Append(state).TakeLast(32).ToArray();
        try { MapWorkspacePreferences.Save(mapsPath, mapProfiles); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { EditorStatus.ToolTip = "Map layout could not be saved: " + error.Message; }
    }
    private void RestoreMap(EditorWorkspace editor, MapPanePreferences state)
    {
        editor.cellZoom = state.Zoom; editor.autoFitCells = state.AutoFit; editor.previewWidth = state.PreviewWidth; editor.tableFraction = state.TableFraction;
        editor.fitTable = state.FitTable ?? editor.fitTable;
        editor.MapGrid.FontSize = 13 * state.Zoom; editor.MapGrid.ColumnHeaderHeight = Math.Ceiling(26 * state.Zoom);
        editor.measuredCellWidth = double.NaN;
        editor.BaselineBox.SelectedIndex = state.Baseline == 2 && editor.document?.HasComparison != true ? 0 : state.Baseline;
        editor.SetViewMode(state.View); editor.RefreshAxes(); editor.RefreshMapControls();
        void ApplySelection()
        {
            if (editor.layoutClosing || editor.table is null || MapKey(editor.table) != state.Key) return;
            editor.MapGrid.UpdateLayout();
            editor.refreshing = true;
            editor.MapGrid.SelectedCells.Clear();
            foreach (int index in state.Selection.Where(i => i < editor.table.Count))
                if (!editor.ScalarMap) editor.MapGrid.SelectedCells.Add(new(editor.MapGrid.Items[editor.CurveList ? index : index / editor.table.Columns], editor.MapGrid.Columns[editor.CurveList ? 1 : index % editor.table.Columns]));
            if (editor.MapGrid.SelectedCells.Count > 0)
                editor.MapGrid.CurrentCell = editor.MapGrid.SelectedCells[0];
            editor.refreshing = false;
            editor.UpdateSelectedAxes(); editor.UpdateCellText(); editor.Graph.Select(state.Selection.FirstOrDefault(-1)); editor.Graph.RestoreView(state);
            var scroll = Descendants(editor.MapGrid).OfType<ScrollViewer>().FirstOrDefault();
            scroll?.ScrollToHorizontalOffset(state.HorizontalOffset); scroll?.ScrollToVerticalOffset(state.VerticalOffset);
        }
        if (editor.MapGrid.IsLoaded) ApplySelection();
        // WPF assigns column display indices when the view is loaded. Restoring
        // cells earlier creates invalid DataGridCellInfo entries after startup.
        editor.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplySelection));
    }
    private void RestorePage(MapPagePreferences saved)
    {
        if (document is null) return;
        var definition = document.Definition;
        var tables = definition.Tables.ToDictionary(MapKey);
        string CurrentKey(string key)
        {
            if (tables.ContainsKey(key)) return key;
            int separator = key.IndexOf(':');
            if (separator < 0) return key;
            string? control = key[(separator + 1)..] switch
            {
                "Fuel Feedback Gain" => "Gain",
                "Fuel Feedback Response Period" => "Response Period",
                _ => null
            };
            // The former Gen 2 labels pointed to the rear loop. Preserve the
            // original address so upgrading never selects a different control.
            string sensor = definition.Calibration == "L3CGEE000" ? "Rear O2" : "Primary Wideband";
            return control is null ? key : key[..(separator + 1)] + $"Fuel Feedback - {sensor} {control}";
        }
        var maps = saved.Maps.Select(s => s with { Key = CurrentKey(s.Key) }).DistinctBy(s => s.Key).ToArray();
        string activeKey = CurrentKey(saved.Active), expandedKey = CurrentKey(saved.Expanded);
        dockColumns = saved.Columns; dockColumnFraction = saved.ColumnFraction; dockRowFraction = saved.RowFraction;
        dockRowWeights = saved.RowWeights;
        scalarColumnWidths = saved.ScalarColumnWidths;
        independentMapLayout = maps.Any(s => s.DockBounds is not null);
        foreach (var state in maps)
        {
            if (!tables.TryGetValue(state.Key, out var selected)) continue;
            var editor = OpenMap(selected, state.Pinned);
            RestoreMap(editor, state);
            var slot = mapSlots.First(s => ReferenceEquals(s.Editor, editor));
            slot.Bounds = state.Bounds;
            slot.DockBounds = state.DockBounds;
            if (state.Floating) DetachMap(editor);
        }
        var active = mapSlots.FirstOrDefault(s => s.Editor.table is { } t && MapKey(t) == activeKey);
        foreach (var slot in mapSlots.Where(s => s.Editor.table is not null))
            Panel.SetZIndex(slot.Frame, maps.FirstOrDefault(s => s.Key == MapKey(slot.Editor.table!))?.ZOrder ?? 0);
        expandedMap = mapSlots.FirstOrDefault(s => s.Window is null && s.Editor.table is { } t && MapKey(t) == expandedKey);
        (active?.Editor ?? this).ActivateMap();
        ArrangeMaps();
    }
}
