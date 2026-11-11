using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal void CheckGridActionsAndFolders(TuningTable map)
    {
        var doc = document!;
        var entry = RevealMapEntry(map);
        if (entry.Header is not MapBrowserLabel { Icon: not null } || entry.HeaderTemplate is null ||
            MapTreeEntries(MapTree).Count(i => i.Tag is TuningTable) != doc.Definition.Tables.Count)
            throw new InvalidOperationException("The map browser dropped entries or failed to show the map type.");
        var nested = doc.Definition.Tables.FirstOrDefault(t => t.Category.Contains(" / ", StringComparison.Ordinal));
        if (nested is not null)
        {
            var folder = MapTreeEntries(MapTree).Single(i => Equals(i.Tag, nested.Category));
            if (!MapTreeEntries(MapTree).Any(parent => parent.Items.Contains(folder)) ||
                !folder.Items.OfType<TreeViewItem>().Any(i => ReferenceEquals(i.Tag, nested)))
                throw new InvalidOperationException("A nested definition category did not open as a subfolder.");
            SearchBox.Text = nested.Name;
            if (!MapTreeEntries(MapTree).Any(i => ReferenceEquals(i.Tag, nested)) ||
                !MapTreeEntries(MapTree).Single(i => Equals(i.Tag, nested.Category)).IsExpanded)
                throw new InvalidOperationException("Search did not reveal the nested map.");
            SearchBox.Clear();
        }

        SelectCell(0);
        MapGrid.Focus();
        byte[] before = doc.Snapshot();
        double value = doc.Read(map, 0);
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(MapGrid)!, 0, Key.Q)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        MapGrid.RaiseEvent(key);
        if (!key.Handled || !(doc.Read(map, 0) > value) || !doc.CanUndo)
            throw new InvalidOperationException("The grid increase shortcut did not make an undoable edit.");
        doc.Undo();
        if (!before.AsSpan().SequenceEqual(doc.Snapshot()))
            throw new InvalidOperationException("Undo did not restore the exact bytes of the shortcut edit.");
    }
}
