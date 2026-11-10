using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public sealed record EditorRow(int Index, string[] Values);
public partial class EditorWorkspace : UserControl
{
    internal RomEditor? Document => document;
    internal void RequestOpen() => Open_Click(this, new());
    private void RefreshChanges()
    {
        if (ChangesList is null) return;
        var changed = document?.Definition.Tables.Where(t => document.TableChanged(t, Baseline)).ToArray() ?? [];
        ChangesList.ItemsSource = changed; ChangesButton.Header = $"Changes ({changed.Length})";
    }
    private void Changes_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ChangesList.SelectedItem is TuningTable changed) Attempt(() => BrowseMap(changed));
    }
    private readonly RomInspector inspector;
    private readonly TuningCodeGuard codeGuard;
    internal RomInspector TestInspector => inspector;
    internal TuningCodeGuard TestGuard => codeGuard;
    private RomEditor? document;
    internal IReadOnlyList<TuningDefinition>? Definitions { get; set; }
    private TuningTable? table;
    private bool refreshing, closingForTest;
    private TuningBaseline Baseline => (TuningBaseline)Math.Max(0, BaselineBox.SelectedIndex);
    public EditorWorkspace() : this(new(), new()) { }
    internal EditorWorkspace(RomInspector inspector, TuningCodeGuard codeGuard, EditorWorkspace? mapOwner = null)
    {
        this.inspector = inspector; this.codeGuard = codeGuard;
        UiTheme.Ensure(); InitializeComponent(); InitializeMapWindows(mapOwner); BaselineBox.ItemsSource = new[] { "Original ROM", "Last save", "Comparison ROM" }; BaselineBox.SelectedIndex = 0;
        Graph.CellPicked += index => SelectCell(index); RefreshControls(); SetViewMode(4);
    }
    public void OpenRom(string path)
    {
        var opened = new RomEditor(File.ReadAllBytes(path), path, inspector, codeGuard, Definitions);
        SaveMapWorkspace();
        restoringMaps = true;
        try
        {
            ClearMapWindows();
            if (document is not null) document.Changed -= Document_Changed;
            document = opened; document.Changed += Document_Changed; table = null;
            MapTabs.Items.Clear(); BaselineBox.SelectedIndex = 0; SearchBox.Text = ""; ChangedOnly.IsChecked = false;
            FileText.Text = Path.GetFileName(path); FileText.ToolTip = path;
            RefreshBrowser(); RefreshTable(); RefreshControls();
            ShowMapBrowser();
        }
        finally { restoringMaps = false; }
        RestoreMapWorkspace();
        EditorStatus.Text = "ROM loaded. Choose a map to edit. Static axes are read-only.";
    }
    private void Attempt(Action action)
        => ExecuteEditorAction(action, message => { EditorStatus.Text = message; MessageBox.Show(Window.GetWindow(this), message, "SpeedLink ROM editor", MessageBoxButton.OK, MessageBoxImage.Information); });
    internal static void ExecuteEditorAction(Action action, Action<string> report)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Xml.XmlException or FormatException or OverflowException)
        { report(error.Message); }
    }
    private bool FinishEdit() => CommitScalar() && MapGrid.CommitEdit(DataGridEditingUnit.Row, true);
    private bool MayReplace() => document?.IsDirty != true || MessageBox.Show(Window.GetWindow(this), "Discard unsaved changes? Saved copies and the original ROM are kept.", "Unsaved ROM edits", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (mapOwner is not null) { mapOwner.Open_Click(sender, e); return; }
        if (!FinishAllMapEdits() || !MayReplace()) return;
        var dialog = new OpenFileDialog { Title = "Open ROM for editing", Filter = "ROM files|*.bin;*.rom|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Attempt(() => OpenRom(dialog.FileName));
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (mapOwner is not null) { mapOwner.Save_Click(sender, e); return; }
        if (document is null || !FinishAllMapEdits()) return;
        string stem = Path.GetFileNameWithoutExtension(document.SourcePath) + "_edited";
        string folder = Path.GetDirectoryName(document.LastSavePath ?? document.SourcePath)!;
        string suggestion = stem + ".bin"; int revision = 2;
        while (File.Exists(Path.Combine(folder, suggestion))) suggestion = stem + "_" + revision++ + ".bin";
        var dialog = new SaveFileDialog { Title = "Save a new edited ROM copy", Filter = "Binary ROM|*.bin", FileName = suggestion, InitialDirectory = folder, OverwritePrompt = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Attempt(() => { document.SaveNew(dialog.FileName); EditorStatus.Text = "Saved · bytes and checksum verified: " + dialog.FileName; });
    }
    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(CommandMap, this)) { CommandMap.Compare_Click(sender, e); return; }
        if (document is null || !FinishEdit()) return;
        var dialog = new OpenFileDialog { Title = "Compare with the same ROM calibration", Filter = "ROM files|*.bin;*.rom|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Attempt(() => { document.LoadComparison(File.ReadAllBytes(dialog.FileName), dialog.FileName); BaselineBox.SelectedIndex = 2; });
    }
    private void Document_Changed(object? sender, EventArgs e)
    {
        if (refreshing) return;
        string? draft = scalarDraft ? ScalarInput.Text : null;
        RefreshControls();
        if (!gridDraft) RefreshTable();
        if (draft is not null) ScalarInput.Text = draft;
        RefreshBrowser();
    }
    private void RefreshControls()
    {
        SaveButton.IsEnabled = document is not null; CompareButton.IsEnabled = document is not null;
        UndoButton.IsEnabled = document?.CanUndo == true; RedoButton.IsEnabled = document?.CanRedo == true;
        UndoButton.ToolTip = document?.UndoDescription; RedoButton.ToolTip = document?.RedoDescription;
        foreach (var control in new Control[] { OperandBox, SetMapButton, AddMapButton, MultiplyMapButton, InterpolationBox, InterpolateMapButton })
            control.IsEnabled = table is not null && !table.IsChoice;
        FitMapButton.IsEnabled = CommandMap.table is not null;
        NoMapPanel.Visibility = table is null ? Visibility.Visible : Visibility.Collapsed;
        NoMapTitle.Text = document is null ? "Open a ROM to begin" : "Choose a map to edit";
        NoMapText.Text = document is null ? "Choose a supported ROM to browse and edit its maps." :
            $"{document.Definition.Calibration} is loaded with {document.Definition.Tables.Count:N0} controls. Choose a map from the list or search by name.";
        NoMapAction.Content = document is null ? "Open ROM…" : "Browse maps";
        DetailsButton.IsEnabled = table is not null;
        if (table is null) DetailsButton.IsChecked = false;
        RefreshChanges();
        RefreshMapControls();
        if (document is not null) ProfileText.Text = document.Definition.Calibration + " · " + document.Definition.Model + (document.IsDirty ? " · Unsaved changes" : " · Saved state");
    }
    private void RefreshBrowser()
    {
        if (MapTree is null) return;
        var expanded = MapTree.Items.OfType<TreeViewItem>().Where(i => i.IsExpanded).Select(i => i.Tag as string).ToHashSet();
        MapTree.Items.Clear(); if (document is null) return;
        string query = SearchBox.Text.Trim();
        var filtered = document.Definition.Tables.Where(t => (query.Length == 0 || (t.Name + " " + t.Category + " " + t.Description).Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (ChangedOnly.IsChecked != true || document.TableChanged(t, Baseline))).ToArray();
        foreach (var group in filtered.GroupBy(t => t.Category))
        {
            var category = new TreeViewItem { Header = group.Key + "  (" + group.Count() + ")", Tag = group.Key, IsExpanded = query.Length > 0 || ChangedOnly.IsChecked == true || expanded.Contains(group.Key), Padding = new(2, 5, 0, 5) };
            foreach (var item in group)
            {
                var entry = new TreeViewItem { Header = (document.TableChanged(item, Baseline) ? "● " : "") + item.Name, Tag = item, ToolTip = item.Name, Padding = new(2, 4, 0, 4) };
                entry.PreviewMouseLeftButtonDown += (_, _) => { if (entry.IsSelected) Attempt(() => BrowseMap(item)); };
                category.Items.Add(entry);
            }
            MapTree.Items.Add(category);
        }
        MapCountText.Text = $"{filtered.Length:N0} / {document.Definition.Tables.Count:N0} controls";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshBrowser();
    private void Filter_Changed(object sender, RoutedEventArgs e) => RefreshBrowser();
    private void Baseline_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (revertingBaseline) return;
        if (!FinishEdit())
        {
            revertingBaseline = true;
            if (e.RemovedItems.Count > 0) BaselineBox.SelectedItem = e.RemovedItems[0];
            revertingBaseline = false;
            return;
        }
        if (BaselineBox.SelectedIndex == 2 && document?.HasComparison != true) { BaselineBox.SelectedIndex = 0; if (EditorStatus is not null) EditorStatus.Text = "Load a comparison ROM first."; return; }
        if (MapTree is not null) { RefreshBrowser(); RefreshTable(); RefreshChanges(); }
    }
    private void Map_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: TuningTable selected }) Attempt(() => BrowseMap(selected));
    }
    internal void OpenTable(TuningTable selected)
    {
        var tab = MapTabs.Items.OfType<TabItem>().FirstOrDefault(t => ReferenceEquals(t.Tag, selected));
        if (tab is null)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal }; header.Children.Add(new TextBlock { Text = selected.Name, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            tab = new TabItem { Header = header, Tag = selected, ToolTip = selected.Name };
            var close = new Button { Content = "×", Padding = new(3, 0, 3, 0), MinHeight = 18, Margin = new(9, 0, 0, 0) }; var closing = tab;
            close.Click += (_, _) => { if (FinishEdit()) MapTabs.Items.Remove(closing); }; header.Children.Add(close); MapTabs.Items.Add(tab);
        }
        MapTabs.SelectedItem = tab;
    }
    private void Tab_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MapTabs) || revertingTab) return;
        if (!FinishEdit())
        {
            revertingTab = true;
            MapTabs.SelectedItem = MapTabs.Items.OfType<TabItem>().FirstOrDefault(t => ReferenceEquals(t.Tag, table));
            revertingTab = false; return;
        }
        refreshing = true; MapGrid.SelectedCells.Clear(); refreshing = false;
        bool wasCurve = CurveList;
        table = (MapTabs.SelectedItem as TabItem)?.Tag as TuningTable;
        if (table is null && MapTree.SelectedItem is TreeViewItem selectedMap) selectedMap.IsSelected = false;
        if (CurveList && !wasCurve) SetViewMode(1);
        else if (wasCurve && !CurveList) SetViewMode(table?.Rows > 1 ? 4 : 0);
        RefreshTable(); RefreshControls(); ApplyView();
        QueueMapSave();
        Dispatcher.BeginInvoke(new Action(() => { if (MapTabs.SelectedItem is TabItem active) { active.UpdateLayout(); active.BringIntoView(); } }));
    }
    private void MapTabs_Wheel(object sender, MouseWheelEventArgs e)
    {
        var scroll = Descendants(MapTabs).OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null || scroll.ScrollableWidth <= 0) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta); e.Handled = true;
    }
    private int NativeIndex(EditorRow row, int column) => CurveList ? row.Index : row.Index * table!.Columns + column;
    private int[] SelectedIndices() => table is null ? [] : ScalarMap ? [0] : MapGrid.SelectedCells.Where(c => c.Item is EditorRow && c.Column.DisplayIndex >= 0).Select(c => NativeIndex((EditorRow)c.Item, c.Column.DisplayIndex)).Distinct().Order().ToArray();
    private void RefreshTable()
    {
        if (MapGrid is null || refreshing) return;
        measuredCellWidth = double.NaN;
        refreshing = true;
        try
        {
            int[] selection = SelectedIndices().Where(i => table is not null && i >= 0 && i < table.Count).ToArray();
            MapGrid.Columns.Clear(); MapGrid.ItemsSource = null; ChoiceBox.Visibility = Visibility.Collapsed;
            RefreshGuideTopic(document?.Definition, table);
            if (document is null || table is null)
            {
                MapTitle.Text = "Select a map"; RefreshAxes(); HelpText.Text = "Choose a map from the list.";
                HelpUnitsText.Text = ""; HelpAxesText.Text = "";
                StorageText.Text = ""; CellText.Text = ""; GraphTitle.Text = "2D curve / 3D surface"; Graph.SetData(null, [], -1); return;
            }
            MapTitle.Text = table.Name;
            InterpolationBox.Visibility = CurveList ? Visibility.Collapsed : Visibility.Visible;
            HelpText.Text = table.Description;
            HelpUnitsText.Text = "Values: " + (string.IsNullOrWhiteSpace(table.Units) ? "Units not specified by this definition" : table.Units);
            HelpAxesText.Text = string.Join("\n", new[] { table.Columns > 1 ? "Columns: " + table.X.Name : "", table.Rows > 1 ? "Rows: " + table.Y.Name : "" }.Where(s => s.Length > 0));
            StorageText.Text = $"{table.Units}\n{table.Columns} × {table.Rows} · {table.Storage} · {(table.BigEndian ? "big endian" : "little endian")}\nAddress 0x{table.Address:X}" + (table.Bit is int b ? $" · bit {b}" : "") + "\nStock definition · " + document.Definition.Version + "\nAxes: static display values";
            var values = Enumerable.Range(0, table.Count).Select(i => document.Read(table, i)).ToArray();
            var finite = values.Where(double.IsFinite).ToArray(); double min = finite.DefaultIfEmpty(0).Min(), max = finite.DefaultIfEmpty(1).Max();
            for (int col = 0; col < (CurveList ? 2 : table.Columns); col++)
            {
                var converter = new EditorCellConverter(document, table, col, Baseline, min, max, CurveList);
                var style = new Style(typeof(DataGridCell));
                style.Setters.Add(new Setter(BackgroundProperty, new Binding { Converter = converter, ConverterParameter = "color" }));
                style.Setters.Add(new Setter(ToolTipProperty, new Binding { Converter = converter, ConverterParameter = "tip" }));
                style.Setters.Add(new Setter(BorderBrushProperty, new Binding { Converter = converter, ConverterParameter = "border" }));
                style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
                if (CurveList && col == 0)
                {
                    style.Setters.Add(new Setter(BackgroundProperty, FindResource("AxisBandBrush")));
                    style.Setters.Add(new Setter(BorderBrushProperty, FindResource("AxisBorderBrush")));
                    style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 2, 1)));
                }
                var trigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true }; trigger.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(93, 70, 36)))); trigger.Setters.Add(new Setter(ForegroundProperty, UiTheme.Text)); style.Triggers.Add(trigger);
                string header = CurveList ? (col == 0 ? table.X.Name : table.Units) : table.Columns == 1 ? "Value" : table.X.Values[col].ToString("G5", CultureInfo.InvariantCulture);
                MapGrid.Columns.Add(new DataGridTextColumn { Header = header, Width = new DataGridLength(70), Binding = new Binding($"Values[{col}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit }, IsReadOnly = table.IsChoice || CurveList && col == 0, CellStyle = style });
            }
            MapGrid.ItemsSource = CurveList ? Enumerable.Range(0, table.Columns).Select(i => new EditorRow(i, [table.X.Values[i].ToString("0.#####", CultureInfo.InvariantCulture), table.Compact(values[i])])).ToArray() :
                Enumerable.Range(0, table.Rows).Select(row => new EditorRow(row, Enumerable.Range(0, table.Columns).Select(col => table.Compact(values[row * table.Columns + col])).ToArray())).ToArray();
            if (table.IsChoice)
            {
                double value = document.Read(table, selection.FirstOrDefault(0));
                var options = table.Bit is not null ? new[] { new TuningState("Disabled / 0", 0), new TuningState("Enabled / 1", 1) } : table.States;
                ChoiceBox.ItemsSource = options; ChoiceBox.DisplayMemberPath = "Name"; ChoiceBox.SelectedItem = options.FirstOrDefault(s => s.Value == value);
                ChoiceBox.Visibility = Visibility.Visible;
            }
            foreach (int index in selection.Where(i => i < table.Count)) MapGrid.SelectedCells.Add(new(MapGrid.Items[CurveList ? index : index / table.Columns], MapGrid.Columns[CurveList ? 1 : index % table.Columns]));
            MapGrid.RowHeaderWidth = table.Bits.Length > 0 ? 230 : CurveList ? 36 : 64;
            RefreshAxes();
            Graph.SetData(table.IsChoice ? null : table, values, selection.FirstOrDefault(-1));
            GraphTitle.Text = table.IsChoice ? "Choose a row, then a named state" : table.Rows > 1 ? "3D · drag to rotate · wheel to zoom" : table.Columns > 1 ? "Curve · wheel to zoom · Shift+drag to pan · click a point" : "Scalar";
            UpdateCellText();
        }
        finally { refreshing = false; RefreshScalar(); UpdateCompactMap(); UpdateTableSizing(); }
    }
    private void Grid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (table is not null && e.Row.Item is EditorRow row)
        {
            e.Row.Header = table.Bits.Length > 0 ? table.BitNames[row.Index] : CurveList ? (row.Index + 1).ToString(CultureInfo.InvariantCulture) : table.Rows > 1 ? table.Y.Values[row.Index].ToString("G5", CultureInfo.InvariantCulture) : "Value";
            SetIsAxisSelected(e.Row, selectedAxisRows.Contains(row.Index));
        }
    }
    private void Grid_PreparingEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        gridDraft = true;
        if (document is not null && table is not null && e.Row.Item is EditorRow row && e.EditingElement is TextBox input)
        { input.Text = document.Read(table, NativeIndex(row, e.Column.DisplayIndex)).ToString("R", CultureInfo.InvariantCulture); input.SelectAll(); }
    }
    private void Grid_EditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) { gridDraft = false; ClearPendingEntryStatus(); return; }
        if (refreshing || document is null || table is null || e.EditAction != DataGridEditAction.Commit || e.EditingElement is not TextBox input) return;
        var row = (EditorRow)e.Row.Item; var selected = table; int index = NativeIndex(row, e.Column.DisplayIndex);
        if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) { e.Cancel = true; EditorStatus.Text = "Enter a finite numeric value using a decimal point."; return; }
        try { refreshing = true; document.Apply([new(selected, index, value)], "Edit " + selected.Name); }
        catch (ArgumentException error) { e.Cancel = true; EditorStatus.Text = error.Message; }
        finally { refreshing = false; if (!e.Cancel) { gridDraft = false; ClearPendingEntryStatus(); } }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { RefreshTable(); RefreshBrowser(); RefreshControls(); }));
    }
    private void Choice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing && document is not null && table is not null && ChoiceBox.SelectedItem is TuningState state) Attempt(() => document.Apply([new(table, SelectedIndices().FirstOrDefault(0), state.Value)], "Switch " + table.Name));
    }
    private void Grid_SelectionChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (refreshing) return; UpdateSelectedAxes(); UpdateCellText(); Graph.Select(SelectedIndices().FirstOrDefault(-1));
        QueueMapSave();
        if (table?.IsChoice == true && document is not null)
        {
            refreshing = true; double value = document.Read(table, SelectedIndices().FirstOrDefault(0)); ChoiceBox.SelectedItem = ChoiceBox.Items.Cast<TuningState>().FirstOrDefault(s => s.Value == value); refreshing = false;
        }
    }
    private void UpdateCellText()
    {
        if (document is null || table is null) return; var selected = SelectedIndices();
        if (selected.Length == 0) { CellText.Text = "Select a cell to see its reference value."; return; }
        int index = selected[0]; double value = document.Read(table, index), reference = document.Read(table, index, Baseline), delta = value - reference;
        string percent = reference == 0 ? "n/a" : (delta / reference * 100).ToString("G6", CultureInfo.InvariantCulture) + "%";
        CellText.Text = $"{selected.Length} selected · {table.Compact(value)} {table.Units} · Δ {table.Compact(delta)}";
        CellText.ToolTip = $"Current: {value:R}\nBaseline: {reference:R}\nDifference: {delta:R} ({percent})";
    }
    private void SelectCell(int index)
    {
        if (table is null || index < 0 || index >= table.Count) return;
        MapGrid.SelectedCells.Clear(); var item = MapGrid.Items[CurveList ? index : index / table.Columns]; var column = MapGrid.Columns[CurveList ? 1 : index % table.Columns];
        MapGrid.SelectedCells.Add(new(item, column)); MapGrid.CurrentCell = new(item, column); MapGrid.ScrollIntoView(item, column);
    }
    private (int Left, int Top, int Right, int Bottom) Rectangle()
    {
        var selected = SelectedIndices(); if (table is null || selected.Length == 0) throw new InvalidOperationException("Select cells first.");
        int left = selected.Min(i => i % table.Columns), right = selected.Max(i => i % table.Columns), top = selected.Min(i => i / table.Columns), bottom = selected.Max(i => i / table.Columns);
        if (selected.Length != (right - left + 1) * (bottom - top + 1)) throw new InvalidOperationException("Select a contiguous rectangle.");
        return (left, top, right, bottom);
    }
    private void Transform(TuningOperation operation)
    {
        if (document is null || table is null || !FinishEdit()) return;
        Attempt(() => { if (!double.TryParse(OperandBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double operand)) throw new ArgumentException("Enter a numeric amount."); var selected = SelectedIndices(); if (selected.Length == 0) throw new InvalidOperationException("Select cells first."); document.Transform(table, selected, operation, operand); });
    }
    private void Set_Click(object sender, RoutedEventArgs e) => Transform(TuningOperation.Set);
    private void Add_Click(object sender, RoutedEventArgs e) => Transform(TuningOperation.Add);
    private void Multiply_Click(object sender, RoutedEventArgs e) => Transform(TuningOperation.Multiply);
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(CommandMap, this)) { CommandMap.Copy_Click(sender, e); return; }
        if (document is null || table is null) return; Attempt(() =>
        {
            if (CurveList) Clipboard.SetText(string.Join('\n', SelectedIndices().Select(i => table.X.Values[i].ToString("R", CultureInfo.InvariantCulture) + "\t" + document.Read(table, i).ToString("R", CultureInfo.InvariantCulture))));
            else { var r = Rectangle(); Clipboard.SetText(string.Join('\n', Enumerable.Range(r.Top, r.Bottom - r.Top + 1).Select(y => string.Join('\t', Enumerable.Range(r.Left, r.Right - r.Left + 1).Select(x => document.Read(table, y * table.Columns + x).ToString("R", CultureInfo.InvariantCulture)))))); }
            EditorStatus.Text = CurveList ? "Breakpoints and values copied at full precision" : "Values copied at full precision";
        });
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(CommandMap, this)) { CommandMap.Paste_Click(sender, e); return; }
        if (document is null || table is null) return; Attempt(() => { var r = Rectangle(); if (CurveList) document.PasteCurve(table, r.Left, Clipboard.GetText()); else document.Paste(table, r.Top, r.Left, Clipboard.GetText()); });
    }
    private void Interpolate_Click(object sender, RoutedEventArgs e)
    {
        if (document is null || table is null) return; Attempt(() => { var r = Rectangle(); document.Interpolate(table, r.Left, r.Top, r.Right, r.Bottom, CurveList ? "Horizontal" : ((ComboBoxItem)InterpolationBox.SelectedItem).Content.ToString()!); });
    }
    private void Reset_Click(object sender, RoutedEventArgs e) { if (!ReferenceEquals(CommandMap, this)) { CommandMap.Reset_Click(sender, e); return; } if (document is not null && table is not null) { RefreshScalar(); Attempt(() => document.Reset(table, SelectedIndices(), Baseline)); } }
    private void Undo_Click(object sender, RoutedEventArgs e) { if (!ReferenceEquals(CommandMap, this)) { CommandMap.Undo_Click(sender, e); return; } MapGrid.CancelEdit(); RefreshScalar(); document?.Undo(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (!ReferenceEquals(CommandMap, this)) { CommandMap.Redo_Click(sender, e); return; } MapGrid.CancelEdit(); RefreshScalar(); document?.Redo(); }
    private void ResetView_Click(object sender, RoutedEventArgs e) => Graph.ResetView();
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        using var stream = typeof(EditorWindow).Assembly.GetManifestResourceStream("SpeedLink.EditorHelp.md")!; using var reader = new StreamReader(stream);
        new Window { Owner = Window.GetWindow(this), Title = "SpeedLink · Editor guide", Width = 830, Height = 650, Content = new TextBox { Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(20) } }.Show();
    }
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(CommandMap, this) && e.Key is not (Key.S or Key.O or Key.B)) { CommandMap.Editor_KeyDown(sender, e); return; }
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (table is not null) { ShowMapInformation(); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        bool editingText = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.S) Save_Click(sender, e);
        else if (e.Key == Key.O) Open_Click(sender, e);
        else if (!editingText && e.Key == Key.B) MapRoot.FocusButton.IsChecked = !MapRoot.FocusButton.IsChecked;
        else if (!editingText && e.Key is Key.D0 or Key.NumPad0) FitMap_Click(sender, e);
        else if (!editingText && e.Key == Key.Z) Undo_Click(sender, e);
        else if (!editingText && e.Key == Key.Y) Redo_Click(sender, e);
        else if (!editingText && e.Key == Key.C) Copy_Click(sender, e);
        else if (!editingText && e.Key == Key.V) Paste_Click(sender, e);
        else return; e.Handled = true;
    }
    internal bool MayClose() => closingForTest || (FinishAllMapEdits() && MayReplace());
    internal void CloseOfflineTest() => closingForTest = true;
    internal async Task<object> ExerciseOffline(string path, string folder, Action? switchWorkspaces = null)
    {
        Directory.CreateDirectory(folder);
        FocusButton.IsChecked = true; SaveLayout();
        OpenRom(path); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await CheckMapBrowserAccess(path, folder);
        CheckNoMapHelp();
        var doc = document!; var scalar = doc.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        OpenTable(scalar); SelectCell(0); UpdateLayout();
        if (!ScalarInput.IsVisible || TablePanel.IsVisible || GraphPanel.IsVisible) throw new InvalidOperationException("Single value editor is not active.");
        ScalarInput.Text = "8123";
        if (!FinishEdit() || doc.Read(scalar, 0) != 8123) throw new InvalidOperationException("Direct cell commit did not edit ROM bytes.");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var dirtySnapshot = doc.Snapshot(); switchWorkspaces?.Invoke();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (!doc.IsDirty || !doc.Snapshot().AsSpan().SequenceEqual(dirtySnapshot)) throw new InvalidOperationException("Workspace navigation lost edits.");
        ChangesButton.IsChecked = true; if (!ChangesList.Items.Cast<TuningTable>().Contains(scalar)) throw new InvalidOperationException("Changes drawer missed the edited map.");
        ChangesList.SelectedItem = scalar; if (!ReferenceEquals(table, scalar)) throw new InvalidOperationException("Changed-map navigation failed.");
        doc.LoadComparison(doc.Snapshot(), path); BaselineBox.SelectedIndex = 2;
        if (ChangesList.Items.Count != 0) throw new InvalidOperationException("Changes drawer ignored comparison baseline.");
        BaselineBox.SelectedIndex = 0; if (!ChangesList.Items.Cast<TuningTable>().Contains(scalar)) throw new InvalidOperationException("Changes drawer did not restore original baseline.");
        ChangesButton.IsChecked = false;
        doc.Undo(); if (doc.IsDirty) throw new InvalidOperationException("Direct cell edit undo failed.");
        SelectCell(0); OperandBox.Text = "1"; Add_Click(this, new()); if (!doc.IsDirty || !UndoButton.IsEnabled) throw new InvalidOperationException("Scalar UI edit failed.");
        Undo_Click(this, new()); if (doc.IsDirty) throw new InvalidOperationException("UI undo failed."); Redo_Click(this, new());
        var curve = doc.Definition.Tables.First(t => t.Kind == "2D"); OpenTable(curve); SelectCell(1); UpdateLayout(); Capture(Path.Combine(folder, "curve.png"));
        var surface = doc.Definition.Tables.First(t => t.Kind == "3D"); OpenTable(surface); SelectCell(surface.Columns + 2); UpdateLayout(); Capture(Path.Combine(folder, "surface.png"));
        var bit = doc.Definition.Tables.FirstOrDefault(t => t.Bit is not null) ?? doc.Definition.Tables.First(t => t.IsChoice); OpenTable(bit); double before = doc.Read(bit, 0);
        var selector = bit.Count == 1 ? ScalarChoice : ChoiceBox;
        selector.SelectedItem = selector.Items.Cast<TuningState>().First(s => s.Value != before); if (doc.Read(bit, 0) == before) throw new InvalidOperationException("State selector did not edit.");
        doc.Undo(); doc.Undo(); if (doc.IsDirty) throw new InvalidOperationException("Undo failed to restore original bytes.");
        doc.LoadComparison(doc.Snapshot(), path); BaselineBox.SelectedIndex = 2; ChangedOnly.IsChecked = true; if (MapTree.Items.Count != 0) throw new InvalidOperationException("Changed filter failed.");
        ChangedOnly.IsChecked = false; BaselineBox.SelectedIndex = 0; OpenTable(surface); SelectCell(surface.Columns + 2);
        string export = Path.Combine(folder, "UI_UNEDITED_COPY.bin"); doc.SaveNew(export); if (!File.ReadAllBytes(export).AsSpan().SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("No-edit roundtrip differs.");
        FocusButton.IsChecked = true; DetailsButton.IsChecked = false; SetViewMode(1); UpdateLayout();
        if (BrowserPanel.IsVisible || Grid.GetColumn(GraphPanel) != 2 || MapGrid.ActualWidth < 100 || GraphPanel.ActualWidth < 100) throw new InvalidOperationException("Focused split layout failed.");
        Capture(Path.Combine(folder, "focused.png"));
        SetViewMode(2); UpdateLayout(); if (GraphPanel.IsVisible) throw new InvalidOperationException("Table-only view failed.");
        SetViewMode(3); UpdateLayout(); if (MapGrid.IsVisible || !GraphPanel.IsVisible) throw new InvalidOperationException("Graph-only view failed.");
        SetViewMode(0); FocusButton.IsChecked = false; DetailsButton.IsChecked = true; UpdateLayout();
        if (!BrowserPanel.IsVisible || !DetailsPanel.IsVisible || Grid.GetRow(GraphPanel) != 2) throw new InvalidOperationException("Workbench layout did not restore.");
        Capture(Path.Combine(folder, "editor.png")); closingForTest = true;
        MapTabs.Items.Clear();
        var documentationCurve = doc.Definition.Tables.FirstOrDefault(t => t.Name.Contains("Injector Base Pulsewidth", StringComparison.OrdinalIgnoreCase) && t.Kind == "2D") ?? curve;
        OpenTable(documentationCurve); SelectCell(0);
        foreach (var category in MapTree.Items.OfType<TreeViewItem>()) category.IsExpanded = (string?)category.Tag == documentationCurve.Category;
        UpdateLayout(); Capture(Path.Combine(folder, "CurveEditor.png"));
        var illustrated = doc.Definition.Tables.FirstOrDefault(t => t.Name == "Intake Cam Advance (VVT)") ?? surface;
        OpenTable(illustrated); FocusButton.IsChecked = true; DetailsButton.IsChecked = false; FitMap_Click(this, new()); SelectCell(illustrated.Columns + 2); UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
        Graph.CheckSurfaceDrag();
        Graph.CheckSurfaceAxes();
        Capture(Path.Combine(folder, "SurfaceEditor.png"));
        CaptureElement(GraphPanel, Path.Combine(folder, "CompactSurface.png"));
        PopGraph_Click(this, new()); graphWindow!.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        CaptureWindow(graphWindow, Path.Combine(folder, "VvtSurface.png")); DockGraph_Click(this, new());
        await CheckCompactEditors(folder);
        await CheckMapInformation(folder);
        await CheckStockScrolling(folder);
        return new { passed = true, calibration = doc.Definition.Calibration, controls = doc.Definition.Tables.Count, mapBrowserAccessibleOnOpen = true, emptyMapPrompt = true, mapsButtonRestoresBrowser = true, lastMapClosedRecovery = true, minimizedBrowserRestored = true, grid = true, directCellCommit = true, scalarEditor = true, compactMaps = true, curve = true, surface = true, surfaceFollowsDrag = true, surfaceAxesFollowCamera = true, mapInformationShowsUnits = true, axisFrameJoined = true, mapInfoDirectAccess = true, mapInfoF1 = true, themedScrollbars = true, scrollbarInputAndEdges = true, scrollbarLayoutScales = new[] { 1d, 1.25 }, bitSelector = true, undoRedo = true, comparison = true, changedFilter = true, noEditExportIdentical = true, focusedAndAllViews = true, changesDrawer = true, workspaceEditsPreserved = true };
    }
    private void Capture(string path) => CaptureElement(this, path);
    private static void CaptureElement(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(element.RenderSize)); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}

internal sealed class EditorCellConverter(RomEditor document, TuningTable table, int column, TuningBaseline baseline, double min, double max, bool curveList = false) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not EditorRow row) return UiTheme.Panel;
        int index = curveList ? row.Index : row.Index * table.Columns + column; double current = document.Read(table, index);
        if (curveList && column == 0) return (string)parameter == "tip" ? "Static breakpoint · read-only" : (string)parameter == "border" ? Brushes.Transparent : UiTheme.Panel;
        if ((string)parameter == "border") return document.CellChanged(table, index, baseline) ? Brushes.DarkOrange : Brushes.Transparent;
        if ((string)parameter == "tip") { double reference = document.Read(table, index, baseline); return $"Current: {current:R}\nBaseline: {reference:R}\nDifference: {current - reference:R} {table.Units}"; }
        if (!double.IsFinite(current)) return Brushes.LightCoral;
        double scale = Math.Max(1, Math.Max(Math.Abs(min), Math.Abs(max))), span = max / scale - min / scale;
        double fraction = span < 1e-15 ? .5 : Math.Clamp((current / scale - min / scale) / span, 0, 1);
        return new SolidColorBrush(Color.FromRgb((byte)(39 + 48 * fraction), (byte)(43 + 23 * fraction), (byte)(46 - 7 * fraction)));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
