using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private async Task<IReadOnlyList<string>> ExerciseWorkspaceTabs(string folder)
    {
        var cases = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            cases.Add(name);
        }
        async Task Idle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        void Choose(TuningTable map)
        {
            var category = MapTree.Items.OfType<TreeViewItem>().Single(i => (string)i.Tag == map.Category);
            category.IsExpanded = true;
            var entry = category.Items.OfType<TreeViewItem>().Single(i => ReferenceEquals(i.Tag, map));
            entry.IsSelected = false;
            entry.IsSelected = true;
        }
        static void Pin(EditorWorkspace editor) => editor.PinMapButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        MapSlot Slot(TuningTable map) => mapSlots.Single(s => ReferenceEquals(s.Editor.table, map));
        int OpenCount() => mapSlots.Count(s => s.Editor.table is not null);

        var doc = document!;
        byte[] original = doc.Snapshot();
        var maps = doc.Definition.Tables.Where(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice).Take(3).ToArray();
        var scalar = doc.Definition.Tables.First(t => t.Count == 1 && !t.IsChoice && t.Storage == "float");
        FocusButton.IsChecked = false;
        Choose(maps[0]); await Idle();
        Choose(maps[1]); await Idle();
        Check(OpenCount() == 1 && ReferenceEquals(table, maps[1]) && !Slot(maps[1]).Pinned, "Browsing replaces the reusable map pane");
        Pin(this);
        Choose(maps[0]); await Idle();
        var pinned = Slot(maps[1]);
        var preview = Slot(maps[0]);
        Check(OpenCount() == 2 && pinned.Pinned && !preview.Pinned, "Pinning opens a second pane for browsing");
        var bounds = DockedBounds(preview.Editor);
        var pinnedBounds = DockedBounds(this);
        Choose(maps[2]); await Idle();
        Check(OpenCount() == 2 && ReferenceEquals(Slot(maps[2]), preview) && SameBounds(bounds, DockedBounds(preview.Editor)) &&
            SameBounds(pinnedBounds, DockedBounds(this)), "Replacing the preview keeps pane sizes and pinned positions");
        Choose(maps[1]); await Idle();
        Check(ReferenceEquals(activeMap, this) && OpenCount() == 2, "Choosing a pinned map focuses it without replacing the preview");
        Pin(this);
        Check(!pinned.Pinned && preview.Pinned, "Unpinning makes that pane reusable while keeping the other map");
        Choose(scalar); await Idle();
        double before = doc.Read(scalar, 0);
        ScalarInput.Text = "invalid";
        Choose(maps[0]); await Idle();
        Check(ReferenceEquals(table, scalar) && scalarDraft && ScalarInput.Text == "invalid" && before == doc.Read(scalar, 0),
            "Invalid scalar input blocks preview replacement without losing the draft");
        ScalarInput.Text = (before + 1).ToString("R", CultureInfo.InvariantCulture);
        Choose(maps[0]); await Idle();
        Check(ReferenceEquals(table, maps[0]) && doc.Read(scalar, 0) != before && doc.CanUndo &&
            !EditorStatus.Text.StartsWith("Correct or cancel", StringComparison.Ordinal), "Valid input is applied before preview replacement");
        SelectCell(1);
        preview.Editor.UndockMapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle();
        var floating = preview.Window ?? throw new InvalidOperationException("Pinned map failed to undock.");
        floating.Width = 570; floating.Height = 450;
        await Idle();
        RenameWorkspace("Boost Control");
        AddWorkspace("Fuel Tuning"); await Idle();
        Check(currentPage == 1 && WorkspaceTabs.Items.Count == 2 && OpenCount() == 0 && !floating.IsVisible,
            "A new workspace is empty and hides the other workspace's detached maps");
        Choose(scalar); await Idle();
        Check(ReferenceEquals(document, doc) && doc.Read(scalar, 0) != before && UndoButton.IsEnabled,
            "Tabs share applied edits and undo history");
        if (doc.Definition.Calibration == "L38KEH000") CaptureHost(Path.Combine(folder, "NamedMapWorkspaces.png"));
        ScalarInput.Text = "invalid";
        WorkspaceTabs.SelectedIndex = 0; await Idle();
        Check(currentPage == 1 && WorkspaceTabs.SelectedIndex == 1 && scalarDraft && ScalarInput.Text == "invalid",
            "Invalid pending input keeps the current workspace selected");
        ScalarCancel_Click(this, new());
        Undo_Click(this, new()); await Idle();
        Check(original.AsSpan().SequenceEqual(doc.Snapshot()), "Undo in a different workspace restores the original ROM bytes");
        WorkspaceTabs.SelectedIndex = 0; await Idle();
        Check(OpenCount() == 2 && Slot(maps[2]).Pinned && Slot(maps[2]).Window is { IsVisible: true, Width: 570, Height: 450 } &&
            !Slot(maps[0]).Pinned && SelectedIndices().SequenceEqual(new[] { 1 }), "Returning to a tab restores its pins, selection and detached window bounds");
        MapGrid.Focus();
        Check(MapGrid.BeginEdit(), "A restored map supports direct grid editing");
        var input = MapGrid.Columns[1].GetCellContent(MapGrid.Items[0]) as TextBox ?? throw new InvalidOperationException("Grid editor did not open.");
        input.Text = "NaN";
        WorkspaceTabs.SelectedIndex = 1; await Idle();
        Check(currentPage == 0 && WorkspaceTabs.SelectedIndex == 0 && gridDraft && input.Text == "NaN",
            "Invalid grid input also blocks a workspace switch");
        MapGrid.CancelEdit(DataGridEditingUnit.Row); await Idle();
        WorkspaceTabs.SelectedIndex = 1; await Idle();
        Check(ReferenceEquals(table, scalar) && OpenCount() == 1, "Switching tabs restores a separate map set");
        SaveMapWorkspace();
        var fresh = new Window { Width = 1280, Height = 850, ShowInTaskbar = false };
        var restarted = new EditorWorkspace(inspector, codeGuard) { Definitions = Definitions };
        fresh.Content = restarted;
        restarted.ConfigureLayout(layoutPath!, () => true);
        try
        {
            fresh.Show(); restarted.OpenRom(doc.SourcePath); await Idle();
            Check(restarted.currentPage == 1 && restarted.workspacePages.Select(p => p.Name).SequenceEqual(new[] { "Boost Control", "Fuel Tuning" }) &&
                restarted.mapSlots[0] is { Pinned: false, Editor.table: { } restored } && MapKey(restored) == MapKey(scalar),
                "A new editor instance loads named workspaces from disk");
        }
        finally { restarted.CloseOfflineTest(); restarted.PrepareHostClose(); fresh.Close(); }
        OpenRom(doc.SourcePath); await Idle();
        Check(currentPage == 1 && workspacePages.Select(p => p.Name).SequenceEqual(new[] { "Boost Control", "Fuel Tuning" }) &&
            table is not null && MapKey(table) == MapKey(scalar) && !mapSlots[0].Pinned, "Reopening the ROM restores workspace names, selected tab and preview");
        WorkspaceTabs.SelectedIndex = 0; await Idle();
        Check(OpenCount() == 2 && mapSlots.Any(s => s.Pinned && s.Window is { IsVisible: true }), "An inactive saved workspace restores its pinned maps");
        var reopened = document!;
        var reopenedScalar = reopened.Definition.Tables.Single(t => MapKey(t) == MapKey(scalar));
        reopened.Apply([new(reopenedScalar, 0, before + 1)], "Workspace deletion fixture");
        byte[] edited = reopened.Snapshot();
        DeleteWorkspace(); await Idle();
        Check(WorkspaceTabs.Items.Count == 1 && !DeleteWorkspaceButton.IsEnabled && workspacePages[0].Name == "Fuel Tuning" &&
            edited.AsSpan().SequenceEqual(reopened.Snapshot()), "Deleting a tab removes only its layout and retains applied edits");
        reopened.Undo();
        Check(original.AsSpan().SequenceEqual(reopened.Snapshot()), "Workspace presentation and undo preserve the synthetic ROM");
        string sensor = reopened.Definition.Calibration == "L3CGEE000" ? "Rear O2" : "Primary Wideband";
        var gain = reopened.Definition.Tables.Single(t => t.Name == $"Fuel Feedback - {sensor} Gain");
        var period = reopened.Definition.Tables.Single(t => t.Name == $"Fuel Feedback - {sensor} Response Period");
        string oldGain = $"{gain.Address:X}:Fuel Feedback Gain", oldPeriod = $"{period.Address:X}:Fuel Feedback Response Period";
        workspacePages[0] = workspacePages[0] with
        {
            Maps = [new() { Key = oldGain, Pinned = true, Zoom = 1.15, Selection = [5] }, new() { Key = oldPeriod, Pinned = false }],
            Active = oldGain, Expanded = oldGain
        };
        ShowWorkspace(0); await Idle();
        Check(OpenCount() == 2 && Slot(gain).Pinned && !Slot(period).Pinned && ReferenceEquals(activeMap?.table, gain) &&
            ReferenceEquals(expandedMap?.Editor.table, gain) && Slot(gain).Editor.cellZoom == 1.15 &&
            Slot(gain).Editor.SelectedIndices().SequenceEqual(new[] { 5 }), "Old fuel feedback layouts reopen the same ROM addresses under the new names");
        return cases;
    }
}
