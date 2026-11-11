using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private async Task CheckTableAxes(TuningTable sample)
    {
        if (XAxisText.Text != sample.X.Name || YAxisText.Text != sample.Y.Name || ValueUnitsText.Text != "Values: " + sample.Units ||
            XAxisText.ToolTip?.ToString() != sample.X.Name || YAxisText.ToolTip?.ToString() != sample.Y.Name)
            throw new InvalidOperationException("Table labels do not match definition metadata.");
        SelectCell(5 * 16 + 4); UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var header = Descendants(MapGrid).OfType<DataGridColumnHeader>().Single(h => h.Column?.DisplayIndex == 4);
        var row = (DataGridRow)MapGrid.ItemContainerGenerator.ContainerFromIndex(5);
        var rowHeader = Descendants(row).OfType<DataGridRowHeader>().Single();
        if (header.BorderThickness.Bottom < 2 || rowHeader.BorderThickness.Right < 2 ||
            header.BorderBrush is not SolidColorBrush axisLine || axisLine.Color != Color.FromRgb(137, 154, 168) ||
            rowHeader.BorderBrush is not SolidColorBrush rowLine || rowLine.Color != axisLine.Color)
            throw new InvalidOperationException("The axis headers lack a distinct separator from editable map cells.");
        var selectionColor = UiTheme.Brush("CellSelectionBrush").Color;
        if (!GetIsAxisSelected(header.Column) || !GetIsAxisSelected(row) || header.Background is not SolidColorBrush brush || brush.Color != selectionColor)
            throw new InvalidOperationException("The selected cell's axis headers are not visibly highlighted.");
        MapGrid.SelectedCells.Clear();
        foreach (int r in new[] { 2, 3 }) foreach (int c in new[] { 6, 7 }) MapGrid.SelectedCells.Add(new(MapGrid.Items[r], MapGrid.Columns[c]));
        if (MapGrid.Columns.Count(GetIsAxisSelected) != 2 || selectedAxisRows.Count != 2 || GetIsAxisSelected(row))
            throw new InvalidOperationException("Axis highlighting did not follow a rectangular selection.");
        foreach (var column in MapGrid.Columns) column.Width = new(100);
        MapGrid.RowHeight = 34; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
        var scroll = Descendants(MapGrid).OfType<ScrollViewer>().First();
        CheckAxisFrame();
        // DataGrid reports vertical extent in rows when content scrolling is on.
        if (scroll.ScrollableWidth < 100 || scroll.ScrollableHeight < .5)
            throw new InvalidOperationException($"Axis scrolling fixture did not overflow: {scroll.ScrollableWidth}/{scroll.ScrollableHeight}.");
        var xBefore = XAxisText.TransformToAncestor(TablePanel).TransformBounds(new Rect(XAxisText.RenderSize));
        var yBefore = YAxisText.TransformToAncestor(TablePanel).TransformBounds(new Rect(YAxisText.RenderSize));
        CheckAxisRowAlignment();
        SelectCell(255);
        scroll.ScrollToRightEnd(); scroll.ScrollToBottom();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
        var xAfter = XAxisText.TransformToAncestor(TablePanel).TransformBounds(new Rect(XAxisText.RenderSize));
        var yAfter = YAxisText.TransformToAncestor(TablePanel).TransformBounds(new Rect(YAxisText.RenderSize));
        CheckAxisRowAlignment();
        // Titles stay in their bands. A clipped edge cell or the unused part
        // of the final row may change the visible data span by under one cell;
        // in that case the title must recenter instead of staying over blank space.
        if (Math.Abs(xAfter.Top - xBefore.Top) > .5 || Math.Abs(yAfter.Left - yBefore.Left) > .5 ||
            Math.Abs((xAfter.Left + xAfter.Right - xBefore.Left - xBefore.Right) / 2) > MapGrid.Columns.Max(c => c.ActualWidth) / 2 + 1 ||
            Math.Abs((yAfter.Top + yAfter.Bottom - yBefore.Top - yBefore.Bottom) / 2) > MapGrid.RowHeight / 2 + 1 ||
            scroll.HorizontalOffset == 0 || scroll.VerticalOffset == 0 ||
            Math.Abs(AxisHeaderSpacer.ActualWidth - MapGrid.RowHeaderWidth) > .5 || AxisScrollbarSpacer.ActualWidth < 10)
            throw new InvalidOperationException("Axis titles moved or lost their header alignment while scrolling.");
        var lastRow = (DataGridRow)MapGrid.ItemContainerGenerator.ContainerFromIndex(15);
        var lastHeader = Descendants(MapGrid).OfType<DataGridColumnHeader>().Single(h => h.Column?.DisplayIndex == 15);
        if (!GetIsAxisSelected(lastRow) || lastHeader.Background is not SolidColorBrush lastBrush || lastBrush.Color != selectionColor)
            throw new InvalidOperationException("Scrolled headers lost the current selection.");
        scroll.ScrollToLeftEnd(); scroll.ScrollToTop(); FitMap_Click(this, new());
        // Use the full pane for explicit scrollbar combinations: a compact
        // preview can leave less width than the readable cell minimum permits.
        SetViewMode(2); UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        foreach (var (horizontal, vertical) in new[] { (false, false), (true, false), (false, true) })
        {
            double fitting = Math.Floor((MapGrid.ActualWidth - MapGrid.RowHeaderWidth - SystemParameters.VerticalScrollBarWidth - 2) / sample.Columns);
            foreach (var column in MapGrid.Columns) column.Width = new(horizontal ? 100 : fitting);
            MapGrid.RowHeight = vertical ? 40 : 22;
            await Settle(); await Settle(); UpdateAxisGeometry(); UpdateLayout();
            if ((scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible) != horizontal ||
                (scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible) != vertical)
                throw new InvalidOperationException($"Axis frame fixture did not reach scrollbar combination {horizontal}/{vertical}: grid {MapGrid.ActualWidth}/{MapGrid.ActualHeight}, fitting {fitting}, minimum {MapGrid.Columns[0].MinWidth}, scroll {scroll.ScrollableWidth}/{scroll.ScrollableHeight}.");
            CheckAxisFrame();
        }
        FitMap_Click(this, new()); await Settle();
    }
}
