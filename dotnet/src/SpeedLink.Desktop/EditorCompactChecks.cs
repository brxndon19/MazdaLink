using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private void ScalarKey(Key key)
    {
        ScalarInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(ScalarInput), Environment.TickCount, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }
    private async Task CheckCompactEditors(string folder)
    {
        var doc = document!; var previous = table; int mode = ViewMode; bool fitted = fitTable;
        var originalTabs = MapTabs.Items.Cast<TabItem>().ToHashSet();
        byte[] before = doc.Snapshot();
        var scalar = doc.Definition.Tables.FirstOrDefault(t => t.Name.StartsWith("Absolute Rev Limiter", StringComparison.Ordinal) && t.Count == 1) ??
            doc.Definition.Tables.First(t => t.Count == 1 && !t.IsChoice && t.Storage == "float");
        OpenTable(scalar); SetViewMode(4); await Settle();
        if (!ScalarInput.IsVisible || !ScalarPanel.IsVisible || TablePanel.IsVisible || GraphPanel.IsVisible || DataSplitter.IsVisible || EditBar.IsVisible ||
            ScalarUnits.Text != scalar.Units || HelpText.Text != scalar.Description || !ScalarInfoButton.IsVisible || ScalarInput.FontSize != 13)
            throw new InvalidOperationException("Scalar values do not have a dedicated, labeled value editor.");
        ScalarInput.Focus(); await Settle();
        double original = doc.Read(scalar, 0);
        if (ScalarInput.Text != original.ToString("R", CultureInfo.InvariantCulture) || !FinishEdit() || !before.AsSpan().SequenceEqual(doc.Snapshot()))
            throw new InvalidOperationException("Opening the scalar editor changed stored precision.");
        ScalarInput.Text = "NaN";
        if (FinishEdit() || !ScalarError.IsVisible || !before.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Invalid scalar value was accepted.");
        var curve = doc.Definition.Tables.First(t => t.Rows == 1 && t.Columns is > 1 and <= 8 && !t.IsChoice);
        OpenTable(curve); await Settle();
        if (!ReferenceEquals(table, scalar) || ScalarInput.Text != "NaN") throw new InvalidOperationException("Changing maps discarded invalid scalar input.");
        BaselineBox.SelectedIndex = 1;
        if (BaselineBox.SelectedIndex != 0 || ScalarInput.Text != "NaN") throw new InvalidOperationException("Changing the baseline discarded invalid scalar input.");
        ScalarKey(Key.Escape);
        await Settle();
        if (ScalarError.IsVisible || scalarDraft || ScalarInput.Text != original.ToString("R", CultureInfo.InvariantCulture)) throw new InvalidOperationException("Escape did not cancel a scalar draft.");
        Capture(Path.Combine(folder, "ScalarEditor.png"));
        CaptureElement((FrameworkElement)ScalarPanel.Content, Path.Combine(folder, "ScalarCard.png"));
        ScalarInput.Text = (original + 1).ToString("R", CultureInfo.InvariantCulture); ScalarKey(Key.Enter);
        if (doc.Read(scalar, 0) != original + 1 || scalarDraft || !ScalarDifference.Text.Contains(scalar.Units, StringComparison.Ordinal))
            throw new InvalidOperationException("Enter did not apply a labeled scalar transaction.");
        Undo_Click(this, new()); if (!before.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Scalar undo was not byte-exact.");
        Redo_Click(this, new()); if (doc.Read(scalar, 0) != original + 1) throw new InvalidOperationException("Scalar redo failed.");
        Reset_Click(this, new()); if (!before.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Scalar reset did not use its comparison baseline.");
        ScalarInput.Text = (original + 2).ToString("R", CultureInfo.InvariantCulture); OpenTable(curve);
        if (!ReferenceEquals(table, curve) || doc.Read(scalar, 0) != original + 2) throw new InvalidOperationException("Changing maps lost a valid scalar draft.");
        doc.Undo();
        var integer = doc.Definition.Tables.FirstOrDefault(t => t.Count == 1 && !t.IsChoice && t.Storage != "float");
        if (integer is not null)
        {
            OpenTable(integer); ScalarInput.Text = "1e100";
            if (FinishEdit() || !ScalarError.IsVisible || !before.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Scalar storage overflow changed ROM bytes.");
            ScalarCancel_Click(this, new());
        }
        var choice = doc.Definition.Tables.First(t => t.Count == 1 && t.IsChoice);
        OpenTable(choice); await Settle();
        if (!ScalarChoice.IsVisible || ScalarInput.IsVisible || GraphPanel.IsVisible) throw new InvalidOperationException("Single-state control still displays a grid or graph.");
        double state = doc.Read(choice, 0);
        ScalarChoice.SelectedItem = ScalarChoice.Items.Cast<TuningState>().First(s => s.Value != state);
        if (doc.Read(choice, 0) == state) throw new InvalidOperationException("Scalar state selection did not edit.");
        doc.Undo(); Capture(Path.Combine(folder, "StateEditor.png"));
        var multi = doc.Definition.Tables.FirstOrDefault(t => t.IsChoice && t.Count > 1);
        if (multi is not null)
        {
            OpenTable(multi); await Settle();
            if (!TablePanel.IsVisible || GraphPanel.IsVisible || ScalarPanel.IsVisible || TablePanel.ActualHeight > 300)
                throw new InvalidOperationException("Small multi-state controls waste grid or graph space.");
            Capture(Path.Combine(folder, "SmallStates.png"));
        }
        OpenTable(curve); SetViewMode(4); FitMap_Click(this, new()); await Settle();
        if (!TablePanel.IsVisible || !GraphPanel.IsVisible || GraphTitle.Text != "Curve preview" || TablePanel.ActualHeight > 100 || MapGrid.RowHeight < 28)
            throw new InvalidOperationException("Short curves do not use a proportional grid and curve.");
        double originalGridWidth = TableColumn.ActualWidth, originalGraphWidth = GraphColumn.ActualWidth;
        foreach (double amount in new[] { -50d, 50d })
        {
            double gridWidth = TableColumn.ActualWidth, graphWidth = GraphColumn.ActualWidth;
            DataSplitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            DataSplitter.RaiseEvent(new DragDeltaEventArgs(amount, 0) { RoutedEvent = Thumb.DragDeltaEvent }); await Settle();
            DataSplitter.RaiseEvent(new DragCompletedEventArgs(amount, 0, false) { RoutedEvent = Thumb.DragCompletedEvent }); await Settle();
            if (Math.Abs(TableColumn.ActualWidth - gridWidth - amount) > 2 || Math.Abs(GraphColumn.ActualWidth - graphWidth + amount) > 2)
                throw new InvalidOperationException("The short-curve divider did not resize both panes or snapped back.");
        }
        if (Math.Abs(TableColumn.ActualWidth - originalGridWidth) > 1 || Math.Abs(GraphColumn.ActualWidth - originalGraphWidth) > 1)
            throw new InvalidOperationException("Short-curve resizing did not return to the original dimensions.");
        Capture(Path.Combine(folder, "ShortCurve.png"));
        var smallMaps = doc.Definition.Tables.Where(t => t.Rows is > 1 and <= 8 && t.Columns is > 1 and <= 8 && !t.IsChoice).ToArray();
        var small = smallMaps.FirstOrDefault(t => Enumerable.Range(0, t.Count).Select(i => doc.Read(t, i)).Where(double.IsFinite).Distinct().Take(2).Count() > 1) ?? smallMaps[0];
        OpenTable(small); SetViewMode(4); FitMap_Click(this, new()); await Settle();
        var scroll = Descendants(MapGrid).OfType<ScrollViewer>().First();
        if (TablePanel.ActualHeight > small.Rows * 28 + 60 || TablePanel.ActualWidth > small.Columns * 112 + 100 ||
            scroll.ScrollableWidth > 1 || scroll.ScrollableHeight > 1 || !GraphPanel.IsVisible || GraphTitle.Text != "3D preview")
            throw new InvalidOperationException($"Small surface layout is disproportionate or clipped: {TablePanel.RenderSize}; scroll {scroll.ScrollableWidth}/{scroll.ScrollableHeight}.");
        CheckAxisFrame(); Capture(Path.Combine(folder, "SmallSurface.png"));
        var host = Window.GetWindow(this); double hostWidth = host.Width, hostHeight = host.Height;
        var shapes = doc.Definition.Tables.Where(t => t.Count > 1 && t.Columns <= 8 && t.Rows <= 8)
            .GroupBy(t => (t.Columns, t.Rows, t.IsChoice)).Select(group => group.First()).ToArray();
        foreach (double width in new[] { 1024d, 1280d })
        {
            host.Width = width; host.Height = 700; host.UpdateLayout();
            foreach (var map in shapes)
            {
                OpenTable(map); SetViewMode(4); await Settle(); UpdateTableSizing(); await Settle();
                var header = PaneCaption.TransformToAncestor(MapWorkspace).TransformBounds(new Rect(PaneCaption.RenderSize));
                if (!PaneCaption.IsVisible || PaneCaption.Text != map.Name || header.Right > MapWorkspace.ActualWidth + 1 || header.Top < -1)
                    throw new InvalidOperationException("The map pane title is hidden or clipped outside its window.");
                scroll = Descendants(MapGrid).OfType<ScrollViewer>().First();
                var bounds = TablePanel.TransformToAncestor(TableViewport).TransformBounds(new Rect(TablePanel.RenderSize));
                if (bounds.Right > TableViewport.ActualWidth + 1 || bounds.Bottom > TableViewport.ActualHeight + 1 ||
                    scroll.ScrollableWidth > 1 || scroll.ScrollableHeight > 1 || GraphPanel.IsVisible == map.IsChoice)
                    throw new InvalidOperationException($"Small map {map.Name} ({map.Columns}×{map.Rows}) is clipped at width {width}: {bounds}; scroll {scroll.ScrollableWidth}/{scroll.ScrollableHeight}.");
            }
        }
        host.Width = hostWidth; host.Height = hostHeight; host.UpdateLayout();
        if (!before.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Compact editor checks left ROM edits behind.");
        if (previous is not null) OpenTable(previous);
        foreach (var extra in MapTabs.Items.Cast<TabItem>().Where(tab => !originalTabs.Contains(tab)).ToArray()) MapTabs.Items.Remove(extra);
        fitTable = fitted; SetViewMode(mode); await Settle();
    }
}
