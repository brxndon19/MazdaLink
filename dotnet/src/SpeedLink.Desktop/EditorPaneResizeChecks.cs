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
    private MapDockBounds DockedBounds(EditorWorkspace editor)
    {
        var frame = mapSlots.Single(s => ReferenceEquals(s.Editor, editor)).Frame;
        var point = frame.TranslatePoint(new(), mapDock);
        return new(point.X, point.Y, frame.ActualWidth, frame.ActualHeight);
    }
    private static bool SameBounds(MapDockBounds a, MapDockBounds b) => Math.Abs(a.Left - b.Left) < .5 && Math.Abs(a.Top - b.Top) < .5 && Math.Abs(a.Width - b.Width) < .5 && Math.Abs(a.Height - b.Height) < .5;
    private void DragPaneEdge(EditorWorkspace editor, MapResizeEdge edge, double dx, double dy, bool cancel = false)
    {
        var grip = PaneGrips(mapSlots.Single(s => ReferenceEquals(s.Editor, editor))).Single(g => Equals(g.Tag, edge));
        grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        grip.RaiseEvent(new DragDeltaEventArgs(dx, dy) { RoutedEvent = Thumb.DragDeltaEvent });
        grip.RaiseEvent(new DragCompletedEventArgs(dx, dy, cancel) { RoutedEvent = Thumb.DragCompletedEvent });
    }
    internal async Task<string[]> ExerciseIndependentMapResize(string folder)
    {
        var cases = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            cases.Add(name);
            File.WriteAllText(Path.Combine(folder, "IndependentResizeProgress.json"), System.Text.Json.JsonSerializer.Serialize(new { cases }));
        }
        async Task Idle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        var doc = document!; byte[] before = doc.Snapshot();
        var host = Window.GetWindow(this); double oldWidth = host.Width, oldHeight = host.Height;
        bool oldFocus = FocusButton.IsChecked;
        ClearMapWindows(); MapTabs.Items.Clear(); dockColumns = 2; dockColumnFraction = .5; dockRowWeights = []; scalarColumnWidths = [];
        host.Width = 1280; host.Height = 850; FocusButton.IsChecked = true;
        var tables = doc.Definition.Tables.Where(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice).Take(5).ToArray();
        var first = OpenMap(tables[0]); var second = OpenMap(tables[1]); var third = OpenMap(tables[2]); await Idle();
        var thirdSlot = mapSlots.Single(s => ReferenceEquals(s.Editor, third));
        var dragArea = third.PaneDragArea;
        var pinButton = third.PinMapButton;
        bool Hits(FrameworkElement element, Point point)
        {
            var hit = thirdSlot.Frame.InputHitTest(element.TranslatePoint(point, thirdSlot.Frame)) as Visual;
            return hit is not null && (ReferenceEquals(hit, element) || element.IsAncestorOf(hit));
        }
        double emptyHeaderX = dragArea.ActualWidth - 8;
        Check(thirdSlot.Pinned && emptyHeaderX > third.PaneCaption.DesiredSize.Width &&
            Hits(dragArea, new(emptyHeaderX, dragArea.ActualHeight / 2)), "Blank map header space receives drag input while pinned");
        Check(Hits(pinButton, new(pinButton.ActualWidth / 2, pinButton.ActualHeight / 2)),
            "The pin button still receives pointer input");
        var upper = new[] { DockedBounds(first), DockedBounds(second) }; double thirdWidth = DockedBounds(third).Width;
        third.SelectCell(1); third.CellZoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); double zoom = third.cellZoom;
        DragPaneEdge(third, MapResizeEdge.Right, thirdWidth, 0); await Idle();
        Check(DockedBounds(third).Width > thirdWidth * 1.8 && SameBounds(upper[0], DockedBounds(first)) && SameBounds(upper[1], DockedBounds(second)), "Bottom map widens across empty space without moving either upper map");
        Check(third.cellZoom == zoom && third.SelectedIndices().SequenceEqual([1]), "Independent resizing preserves selected cells and text zoom");
        var secondFixed = DockedBounds(second);
        DragPaneEdge(first, MapResizeEdge.Right, secondFixed.Width / 2, 0); await Idle();
        var firstSlot = mapSlots.Single(s => ReferenceEquals(s.Editor, first));
        var secondSlot = mapSlots.Single(s => ReferenceEquals(s.Editor, second));
        Check(DockedBounds(first).Left + DockedBounds(first).Width > secondFixed.Left + 100 && SameBounds(secondFixed, DockedBounds(second)), "Manual resizing allows overlap without moving the covered map");
        Check(Panel.GetZIndex(firstSlot.Frame) > Panel.GetZIndex(secondSlot.Frame), "Resized map comes to the front");
        OpenMap(tables[1]); await Idle();
        Check(Panel.GetZIndex(secondSlot.Frame) > Panel.GetZIndex(firstSlot.Frame) && SameBounds(secondFixed, DockedBounds(second)), "Choosing a covered map brings it to the front without changing its bounds");
        var otherBeforeMove = new[] { DockedBounds(second), DockedBounds(third) };
        var firstBeforeMove = DockedBounds(first);
        MoveMapPane(firstSlot, firstBeforeMove, 80, 70); UpdateIndependentExtent(); await Idle();
        Check(SameBounds(firstBeforeMove.Move(80, 70), DockedBounds(first)) && SameBounds(otherBeforeMove[0], DockedBounds(second)) && SameBounds(otherBeforeMove[1], DockedBounds(third)), "Moving one pane allows overlap and leaves all other panes in place");
        var corner = PaneGrips(firstSlot).Single(g => Equals(g.Tag, MapResizeEdge.Right | MapResizeEdge.Bottom));
        var cornerPoint = corner.TranslatePoint(new(corner.ActualWidth / 2, corner.ActualHeight / 2), firstSlot.Frame);
        DependencyObject? hit = firstSlot.Frame.InputHitTest(cornerPoint) as DependencyObject;
        while (hit is not null && !ReferenceEquals(hit, corner)) hit = VisualTreeHelper.GetParent(hit);
        Check(ReferenceEquals(hit, corner), "The visible corner resize handle receives pointer input");
        var beforeEscape = DockedBounds(first);
        corner.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        corner.RaiseEvent(new DragDeltaEventArgs(50, 50) { RoutedEvent = Thumb.DragDeltaEvent }); await Idle();
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(second)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        second.MapGrid.RaiseEvent(escape); await Idle();
        Check(escape.Handled && SameBounds(beforeEscape, DockedBounds(first)) && !resizingDock && cancelPaneGesture is null, "Escape cancels a resize even when keyboard focus belongs to another map");
        string previousStatus = EditorStatus.Text;
        first.SelectCell(0); first.MapGrid.Focus();
        Check(first.MapGrid.BeginEdit(), "A pending grid entry can be checked during independent resizing"); await Idle();
        var input = first.MapGrid.Columns[0].GetCellContent(first.MapGrid.Items[0]) as TextBox ?? throw new InvalidOperationException("Grid editor did not open.");
        input.Text = "NaN";
        DragPaneEdge(first, MapResizeEdge.Right, -10, 0); await Idle();
        Check(input.Text == "NaN" && !FinishAllMapEdits() && before.AsSpan().SequenceEqual(doc.Snapshot()), "Resizing preserves invalid pending input and still blocks ROM export");
        first.MapGrid.CancelEdit(DataGridEditingUnit.Row); EditorStatus.Text = previousStatus; await Idle();
        var peers = new[] { first, second, third };
        foreach (var editor in peers)
        {
            foreach (var edge in new[] { MapResizeEdge.Left, MapResizeEdge.Top, MapResizeEdge.Right, MapResizeEdge.Bottom, MapResizeEdge.Left | MapResizeEdge.Top, MapResizeEdge.Right | MapResizeEdge.Top, MapResizeEdge.Left | MapResizeEdge.Bottom, MapResizeEdge.Right | MapResizeEdge.Bottom })
            {
                var others = peers.Where(p => !ReferenceEquals(p, editor)).ToDictionary(p => p, DockedBounds);
                double dx = edge.HasFlag(MapResizeEdge.Left) ? 10 : -10, dy = edge.HasFlag(MapResizeEdge.Top) ? 10 : -10;
                DragPaneEdge(editor, edge, dx, dy); await Idle();
                Check(others.All(p => SameBounds(p.Value, DockedBounds(p.Key))), $"Resizing {editor.table!.Name} at {edge} leaves every other map unchanged");
                var beforeCancel = DockedBounds(editor);
                DragPaneEdge(editor, edge, 30, 30, cancel: true); await Idle();
                Check(SameBounds(beforeCancel, DockedBounds(editor)) && others.All(p => SameBounds(p.Value, DockedBounds(p.Key))), $"Cancel at {edge} restores that pane and leaves other maps unchanged");
            }
        }
        var resized = peers.ToDictionary(p => p, DockedBounds);
        var fourth = OpenMap(tables[3]); await Idle();
        Check(resized.All(p => SameBounds(p.Value, DockedBounds(p.Key))), "Opening another map preserves independently sized panes");
        Check(CloseMap(fourth), "Additional independent pane closes"); await Idle();
        Check(resized.All(p => SameBounds(p.Value, DockedBounds(p.Key))), "Closing another map preserves independently sized panes");
        var thirdBeforeDetach = DockedBounds(third);
        third.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        var floating = mapSlots.Single(s => ReferenceEquals(s.Editor, third));
        Check(floating.Window is { IsVisible: true } && double.IsNaN(floating.Frame.Width) && double.IsNaN(floating.Frame.Height), "Independent pane stretches correctly in its native window");
        third.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
        Check(SameBounds(thirdBeforeDetach, DockedBounds(third)) && SameBounds(resized[first], DockedBounds(first)) && SameBounds(resized[second], DockedBounds(second)), "Docking back restores only that pane's bounds");
        var afterDock = peers.ToDictionary(p => p, DockedBounds);
        host.Width = 1024; host.Height = 768; await Idle();
        Check(afterDock.All(p => SameBounds(p.Value, DockedBounds(p.Key))) && mapDockScroll.ScrollableWidth > 0, "Smaller workspaces scroll without moving independently sized maps");
        host.Width = 1280; host.Height = 850; await Idle();
        third.ActivateMap(); var saved = mapSlots.ToDictionary(s => MapKey(s.Editor.table!), s => DockedBounds(s.Editor));
        var savedOrder = mapSlots.OrderBy(s => Panel.GetZIndex(s.Frame)).Select(s => MapKey(s.Editor.table!)).ToArray(); SaveMapWorkspace();
        OpenRom(doc.SourcePath); FocusButton.IsChecked = true; await Idle();
        Check(independentMapLayout && mapSlots.All(s => SameBounds(saved[MapKey(s.Editor.table!)], DockedBounds(s.Editor))), "Reopening a ROM restores each pane's independent position and size");
        Check(savedOrder.SequenceEqual(mapSlots.OrderBy(s => Panel.GetZIndex(s.Frame)).Select(s => MapKey(s.Editor.table!))), "Reopening a ROM restores overlapping map stacking order");
        Check(mapSlots.Single(s => s.Editor.table == tables[2]).Editor.SelectedIndices().SequenceEqual([1]), "Independent layout restoration retains map selections");
        Check(before.AsSpan().SequenceEqual(document!.Snapshot()), "Resizing and restoring independent panes never changes ROM bytes");
        CaptureHost(Path.Combine(folder, "IndependentMapResize.png"));
        ArrangeMaps_Click(new MenuItem { Tag = "2" }, new()); await Idle();
        Check(!independentMapLayout && mapDock.ColumnDefinitions.Count == 3, "Arrange two columns restores automatic tiling");
        ClearMapWindows(); MapTabs.Items.Clear();
        var scalars = document.Definition.Tables.Where(t => t.Count == 1 && !t.IsChoice && t.Storage == "float").Take(3).ToArray();
        var scalarEditors = scalars.Select(OpenMap).ToArray();
        var shortMaps = document.Definition.Tables.Where(t => t.Rows == 1 && t.Columns is > 1 and <= 16 && !t.IsChoice).Take(2).ToArray();
        Check(shortMaps.Length == 2, "Mixed layout includes three scalar maps and two short curves");
        OpenMap(shortMaps[0]); var bottom = OpenMap(shortMaps[1]); await Idle();
        var mixedPeers = mapSlots.Where(s => !ReferenceEquals(s.Editor, bottom)).ToDictionary(s => s.Editor, s => DockedBounds(s.Editor));
        double bottomWidth = DockedBounds(bottom).Width;
        DragPaneEdge(bottom, MapResizeEdge.Right, bottomWidth, 0); await Idle();
        Check(DockedBounds(bottom).Width > bottomWidth * 1.8 && mixedPeers.All(p => SameBounds(p.Value, DockedBounds(p.Key))), "Mixed scalar and table panes resize into empty space without shifting other rows");
        var beforeScalarShrink = mapSlots.Where(s => !ReferenceEquals(s.Editor, scalarEditors[2])).ToDictionary(s => s.Editor, s => DockedBounds(s.Editor));
        DragPaneEdge(scalarEditors[2], MapResizeEdge.Bottom, 0, 120 - DockedBounds(scalarEditors[2]).Height); await Idle();
        Check(Math.Abs(DockedBounds(scalarEditors[2]).Height - 120) < 1 && beforeScalarShrink.All(p => SameBounds(p.Value, DockedBounds(p.Key))), "A scalar beside a curve shrinks without changing the curve or other rows");
        CaptureHost(Path.Combine(folder, "IndependentMixedPanes.png"));
        ClearMapWindows(); MapTabs.Items.Clear(); host.Width = oldWidth; host.Height = oldHeight; FocusButton.IsChecked = oldFocus; await Idle();
        return cases.ToArray();
    }
}
