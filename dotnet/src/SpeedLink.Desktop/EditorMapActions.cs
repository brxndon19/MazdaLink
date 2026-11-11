using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private int returnView = 2;

    private void EditTools_Click(object sender, RoutedEventArgs e) => ApplyView();
    private void ToggleGraph_Click(object sender, RoutedEventArgs e) => CommandMap.ToggleGraphView();
    private void TogglePaneGraph_Click(object sender, RoutedEventArgs e) => ToggleGraphView();

    private void ToggleGraphView()
    {
        if (table is not { Count: > 1, IsChoice: false } || !FinishEdit()) return;
        int currentView = ViewMode;
        graphWindow?.Close();
        if (currentView == 3)
        {
            SetViewMode(returnView);
            MapGrid.Focus();
        }
        else
        {
            returnView = currentView;
            SetViewMode(3);
            ToggleGraphButton.Focus();
        }
        QueueMapSave();
    }

    private void CloseOtherMaps_Click(object sender, RoutedEventArgs e) => CommandMap.CloseOtherMaps();

    private void CloseOtherMaps()
    {
        var root = MapRoot;
        if (table is null || root.mapSlots.Count(s => s.Editor.table is not null) < 2 ||
            !root.FinishAllMapEdits()) return;
        foreach (var slot in root.mapSlots.Where(s => !ReferenceEquals(s.Editor, root) && !ReferenceEquals(s.Editor, this)).ToArray())
            root.RemoveMapSlot(slot);
        if (!ReferenceEquals(this, root) && root.table is not null)
        {
            root.CloseMap(root);
            return;
        }
        root.expandedMap = null;
        root.ArrangeMaps();
        ActivateMap();
    }

    private static ContextMenu MapContextMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(BackgroundProperty, "PanelBrush");
        menu.SetResourceReference(ForegroundProperty, "TextBrush");
        menu.SetResourceReference(BorderBrushProperty, "LineBrush");
        return menu;
    }

    private void InitializeMapActions()
    {
        MapGrid.GotKeyboardFocus += (_, _) =>
        {
            if (!ReferenceEquals(MapRoot.activeMap, this)) ActivateMap();
        };
        MapGrid.PreviewMouseRightButtonDown += (_, e) =>
        {
            for (var source = e.OriginalSource as Visual; source is not null && source != MapGrid;
                source = VisualTreeHelper.GetParent(source) as Visual)
            {
                if (source is not DataGridCell cell || cell.Column is null) continue;
                var clicked = new DataGridCellInfo(cell.DataContext, cell.Column);
                if (!MapGrid.SelectedCells.Contains(clicked) && FinishEdit())
                {
                    MapGrid.SelectedCells.Clear();
                    MapGrid.CurrentCell = clicked;
                    MapGrid.SelectedCells.Add(clicked);
                }
                break;
            }
        };
        var actions = MapContextMenu();
        MapGrid.ContextMenu = actions;
        actions.Opened += (_, _) =>
        {
            ActivateMap();
            actions.Items.Clear();
            bool values = HasEditableCells;
            AddAction(actions, "Copy", "Ctrl+C", () => Copy_Click(this, new()), MapGrid.SelectedCells.Count > 0);
            AddAction(actions, "Paste", "Ctrl+V", () => Paste_Click(this, new()), values);
            AddAction(actions, "Reset to baseline", "", () => Reset_Click(this, new()), values);
            actions.Items.Add(new Separator());
            AddAction(actions, "Increase", "Q", () => Nudge(1), values);
            AddAction(actions, "Decrease", "A", () => Nudge(-1), values);
            AddAction(actions, "Increase by 10 steps", "Shift+Q", () => Nudge(10), values);
            AddAction(actions, "Decrease by 10 steps", "Shift+A", () => Nudge(-10), values);
            actions.Items.Add(new Separator());
            AddAction(actions, "Interpolate horizontally", "Shift+H", () => Interpolate("Horizontal"), values && table!.Columns > 1);
            AddAction(actions, "Interpolate vertically", "Shift+V", () => Interpolate("Vertical"), values && table!.Rows > 1);
        };
        var paneMenu = MapContextMenu();
        PaneDragArea.ContextMenu = paneMenu;
        AddAction(paneMenu, "Close other maps", "", CloseOtherMaps, true);
        paneMenu.Opened += (_, _) =>
            ((MenuItem)paneMenu.Items[0]).IsEnabled = MapRoot.mapSlots.Count(s => s.Editor.table is not null) > 1;
    }

    private static void AddAction(ContextMenu menu, string name, string shortcut, Action execute, bool enabled)
    {
        var item = new MenuItem { Header = name, InputGestureText = shortcut, IsEnabled = enabled };
        item.Click += (_, _) => execute();
        menu.Items.Add(item);
    }

    private bool HasEditableCells => table is { IsChoice: false } && MapGrid.SelectedCells.Count > 0 &&
        MapGrid.SelectedCells.All(cell => cell.Column is { IsReadOnly: false });

    private bool HandleMapShortcut(KeyEventArgs e)
    {
        var map = CommandMap;
        if (Keyboard.FocusedElement is TextBox or ComboBox) return false;
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.F4 && modifiers == ModifierKeys.None && map.table is { Count: > 1, IsChoice: false })
        {
            if (!e.IsRepeat) map.ToggleGraphView();
        }
        else if (map.MapGrid.IsKeyboardFocusWithin && map.HasEditableCells && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            int steps = modifiers == ModifierKeys.Shift ? 10 : 1;
            switch (e.Key)
            {
                case Key.Q: map.Nudge(steps); break;
                case Key.A: map.Nudge(-steps); break;
                case Key.H when modifiers == ModifierKeys.Shift && map.table!.Columns > 1:
                    map.Interpolate("Horizontal"); break;
                case Key.V when modifiers == ModifierKeys.Shift && map.table!.Rows > 1:
                    map.Interpolate("Vertical"); break;
                default: return false;
            }
            map.MapGrid.Focus();
        }
        else return false;
        e.Handled = true;
        return true;
    }

    private void Interpolate(string direction)
    {
        if (document is null || table is null || !HasEditableCells || !FinishEdit()) return;
        Attempt(() =>
        {
            var r = Rectangle();
            document.Interpolate(table, r.Left, r.Top, r.Right, r.Bottom, direction);
        });
    }

    private void Nudge(int steps)
    {
        if (document is null || table is null || !HasEditableCells || !FinishEdit()) return;
        var map = table;
        var doc = document;
        Attempt(() => doc.Apply(SelectedIndices().Select(index =>
        {
            double value = doc.Read(map, index);
            return new CellEdit(map, index, value + steps * AdjustmentStep(map, value));
        }), (steps > 0 ? "Increase " : "Decrease ") + map.Name));
    }

    private static double AdjustmentStep(TuningTable map, double value)
    {
        int point = map.Format.IndexOf('.');
        int places = point < 0 ? 0 : map.Format[(point + 1)..].TakeWhile(c => c is '0' or '#').Count();
        double displayStep = Math.Pow(10, -Math.Clamp(places, 0, 4));
        double raw = map.Encode(value);
        double previous, next;
        if (map.Storage == "float")
        {
            previous = float.BitDecrement((float)raw);
            next = float.BitIncrement((float)raw);
        }
        else
        {
            raw = Math.Round(raw, MidpointRounding.AwayFromZero);
            previous = raw - 1;
            next = raw + 1;
        }
        foreach (double neighbor in new[] { previous, next })
        {
            if (!double.IsFinite(neighbor)) continue;
            double spacing = Math.Abs(map.Decode(neighbor) - value);
            if (double.IsFinite(spacing)) displayStep = Math.Max(displayStep, spacing);
        }
        return displayStep;
    }
}
