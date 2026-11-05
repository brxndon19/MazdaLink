using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private async Task MenuIdle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }

    internal async Task<object> ExerciseMenusOffline(string folder)
    {
        Directory.CreateDirectory(folder); Show(); Activate(); Navigate(0); await MenuIdle();
        var menu = ApplicationMenu.Items.OfType<MenuItem>().ToArray();
        if (!menu.Select(m => m.Header.ToString()!.Replace("_", "")).SequenceEqual(["File", "Edit", "View", "Tools", "Help"]))
            throw new InvalidOperationException("The permanent application menus are incomplete.");
        if (!menu[3].Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Gen 1 _flashing and recovery…")) ||
            !menu[4].Items.OfType<MenuItem>().Any(item => Equals(item.Tag, "Gen 1 flashing and recovery")) ||
            !Resource("SpeedLink.FlashingHelp.md").Contains("Gen 2 flashing is planned", StringComparison.Ordinal))
            throw new InvalidOperationException("Gen 1 flashing or the built-in guide is missing from the menus.");
        if (ApplicationSaveMenu.IsEnabled || EditorPane.UndoButton.IsEnabled || EditorPane.CompareButton.IsEnabled)
            throw new InvalidOperationException("Editing is available without a ROM.");
        var labels = new[] { NavLabel0, NavLabel1, NavLabel2, NavLabel3, NavLabel4, NavLabel5 };
        if (navigationIsCollapsed || labels.Any(l => !l.IsVisible) || !NavigationLabelsMenu.IsChecked || NavigationColumn.ActualWidth is < 140 or > 160)
            throw new InvalidOperationException("Fresh navigation does not explain its destinations.");
        foreach (var button in Navigation.Children.OfType<RadioButton>())
            if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) || button.ToolTip is not string tip || tip.Length < 20)
                throw new InvalidOperationException("An icon lacks a descriptive accessible name or tooltip.");
        foreach (int page in Enumerable.Range(0, 6))
        {
            Navigate(page); await MenuIdle();
            if (!ApplicationMenu.IsVisible || ApplicationMenu.ActualHeight < 20 || menu.Any(m => !m.IsVisible) ||
                ApplicationMapViewMenu.IsEnabled != (page == 1))
                throw new InvalidOperationException("Application navigation disappears or uses the wrong workspace state.");
        }
        Navigate(0); var app = Application.Current;
        app.Resources[SystemColors.MenuBrushKey] = Brushes.White;
        app.Resources[SystemColors.MenuTextBrushKey] = Brushes.Black;
        var results = new List<object>();
        try
        {
            foreach (var item in menu)
            {
                item.IsSubmenuOpen = true; await MenuIdle();
                if (item.Template.FindName("PART_Popup", item) is not Popup { IsOpen: true, Placement: PlacementMode.Bottom, Child: Border surface } ||
                    surface.Background is not SolidColorBrush background || background.Color != UiTheme.Panel.Color)
                    throw new InvalidOperationException("Application menu surface is unreadable: " + item.Header);
                foreach (var command in item.Items.OfType<MenuItem>())
                {
                    command.ApplyTemplate();
                    if (command.Template.FindName("MenuHeader", command) is not ContentPresenter label || label.ActualWidth < 5 || label.ActualHeight < 5 ||
                        command.Foreground is not SolidColorBrush text || EditorWorkspace.Contrast(text.Color, background.Color) < 4.5)
                        throw new InvalidOperationException("Application menu label is missing or low contrast: " + command.Header);
                    if (command.InputGestureText.Length > 0 && (command.Template.FindName("MenuGesture", command) is not TextBlock gesture || gesture.ActualWidth < 10))
                        throw new InvalidOperationException("Application menu shortcut is missing.");
                }
                string name = item.Header.ToString()!.Replace("_", "");
                EditorWorkspace.SaveMenuImage(surface, Path.Combine(folder, name + ".png"));
                results.Add(new { menu = name, items = item.Items.OfType<MenuItem>().Count(), surfaceDark = true, labelsReadable = true, shortcutsVisible = true });
                item.IsSubmenuOpen = false;
            }
            ApplicationViewMenu.IsSubmenuOpen = true; WorkspaceMenu.IsSubmenuOpen = true; await MenuIdle();
            var logger = WorkspaceMenu.Items.OfType<MenuItem>().Single(m => m.Tag as string == "2");
            logger.Focus(); EditorWorkspace.SendMenuKey(logger, Key.Enter); await MenuIdle();
            if (Pages.SelectedIndex != 2 || !logger.IsChecked) throw new InvalidOperationException("Keyboard menu navigation did not select Logger.");
            menu[4].IsSubmenuOpen = true; await MenuIdle();
            var help = menu[4].Items.OfType<MenuItem>().First(); help.Focus(); EditorWorkspace.SendMenuKey(help, Key.Escape); await MenuIdle();
            if (menu[4].IsSubmenuOpen) throw new InvalidOperationException("Escape did not close the application menu.");
        }
        finally
        {
            foreach (var item in menu) item.IsSubmenuOpen = false;
            app.Resources.Remove(SystemColors.MenuBrushKey); app.Resources.Remove(SystemColors.MenuTextBrushKey);
        }
        NavigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await MenuIdle();
        if (!navigationIsCollapsed || labels.Any(l => l.IsVisible) || NavigationLabelsMenu.IsChecked || NavigationColumn.ActualWidth > 60)
            throw new InvalidOperationException("The sidebar did not collapse consistently.");
        if (!EditorLayoutPreferences.Load(Path.Combine(vault, "editor-layout.json")).NavigationCollapsed)
            throw new InvalidOperationException("The compact navigation preference was not saved.");
        var restored = new MainWindow(vault);
        try { restored.Show(); await MenuIdle(); if (!restored.navigationIsCollapsed) throw new InvalidOperationException("Navigation was not restored on restart."); }
        finally { restored.CloseWorkbenchTest(); }
        NavigationLabelsMenu.IsChecked = true; NavigationLabelsMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await MenuIdle();
        if (navigationIsCollapsed || labels.Any(l => !l.IsVisible) || EditorLayoutPreferences.Load(Path.Combine(vault, "editor-layout.json")).NavigationCollapsed)
            throw new InvalidOperationException("The View menu did not restore and save labeled navigation.");
        Navigate(0); CaptureWorkbench(Path.Combine(folder, "Navigation.png"));
        Width = MinWidth; Height = MinHeight; await MenuIdle();
        if (ApplicationMenu.TranslatePoint(new(ApplicationMenu.ActualWidth, 0), this).X > ActualWidth || labels.Any(l => l.ActualWidth < 30))
            throw new InvalidOperationException("Application navigation clips at the minimum window size.");
        Width = 1440; Height = 880; await MenuIdle();
        return new { passed = true, hardwareAccessed = false, menus = results, hostColorsIndependent = true, checkedStatesVisible = true,
            disabledSave = true, keyboardActivation = true, escapeCloses = true, permanentMenus = true,
            labeledNavigation = true, navigationRemembered = true, minimumLayout = true };
    }

    internal async Task<object> ExerciseNavigationOffline(string path, string folder)
    {
        var menuChecks = await ExerciseMenusOffline(folder);
        Navigate(1); EditorPane.OpenRom(path); await MenuIdle();
        var doc = EditorPane.Document!;
        var map = doc.Definition.Tables.Where(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice && t.Storage == "float")
            .OrderByDescending(t => t.Name.Contains("Intake Cam Advance", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.Name.Contains("VVT", StringComparison.OrdinalIgnoreCase)).First();
        doc.Apply(Enumerable.Range(0, map.Count).Select(i => new CellEdit(map, i,
            Math.Round(4 + 28 * Math.Sin(Math.PI * (i % map.Columns) / (map.Columns - 1)) * (1 - .75 * (i / map.Columns) / (map.Rows - 1)), 1))), "Synthetic preview values");
        string previewPath = Path.Combine(folder, "SYNTHETIC_PREVIEW_NOT_ECU_FIRMWARE.bin");
        doc.SaveNew(previewPath); EditorPane.OpenRom(previewPath); doc = EditorPane.Document!;
        EditorPane.OpenMap(map); await MenuIdle();
        var category = EditorPane.MapTree.Items.OfType<TreeViewItem>().Single(c => c.Tag as string == map.Category); category.IsExpanded = true;
        category.Items.OfType<TreeViewItem>().Single(i => ReferenceEquals(i.Tag, map)).IsSelected = true;
        EditorPane.EditorStatus.Text = "Preview · synthetic map values · no ECU connected";
        EditorPane.FileText.Text = "Synthetic preview";
        await MenuIdle();
        if (!ApplicationSaveMenu.IsEnabled || !ApplicationEditMenu.IsEnabled || !ApplicationMapViewMenu.IsEnabled)
            throw new InvalidOperationException("Loaded-ROM application menus were not enabled.");
        var undo = (MenuItem)ApplicationEditMenu.Items[0]; var redo = (MenuItem)ApplicationEditMenu.Items[1];
        doc.Transform(map, [0], TuningOperation.Add, 1);
        byte[] edited = doc.Snapshot(); undo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await MenuIdle();
        if (!redo.IsEnabled || edited.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Application Undo did not reach the editor.");
        redo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await MenuIdle();
        if (!edited.AsSpan().SequenceEqual(doc.Snapshot())) throw new InvalidOperationException("Application Redo did not restore the map edit.");
        undo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await MenuIdle(); edited = doc.Snapshot();
        foreach (var item in WorkspaceMenu.Items.OfType<MenuItem>())
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await MenuIdle();
            if (!ReferenceEquals(EditorPane.Document, doc) || !edited.AsSpan().SequenceEqual(doc.Snapshot()) || !ApplicationSaveMenu.IsEnabled)
                throw new InvalidOperationException("Application menu navigation lost ROM state.");
        }
        Navigate(1); await MenuIdle(); CaptureApplicationPreview(Path.Combine(folder, "EditorNavigation.png"));
        ApplicationFileMenu.IsSubmenuOpen = true; await MenuIdle();
        if (ApplicationFileMenu.Template.FindName("PART_Popup", ApplicationFileMenu) is Popup { Child: Border file })
            EditorWorkspace.SaveMenuImage(file, Path.Combine(folder, "LoadedFileMenu.png"));
        CaptureApplicationPreview(Path.Combine(folder, "EditorFileMenu.png"), ApplicationFileMenu);
        ApplicationFileMenu.IsSubmenuOpen = false;
        Navigate(2); await MenuIdle(); CaptureApplicationPreview(Path.Combine(folder, "LoggerNavigation.png"));
        Navigate(1); Width = MinWidth; Height = MinHeight; await MenuIdle();
        if (EditorPane.SaveButton.TranslatePoint(new(), this).X > ActualWidth - 70) throw new InvalidOperationException("Editor actions are clipped at minimum size.");
        CaptureWorkbench(Path.Combine(folder, "MinimumNavigation.png"));
        Width = 1440; Height = 880; await MenuIdle();
        return new { passed = true, hardwareAccessed = false, menuChecks, editorCommands = true, navigationPreservesEdits = true, minimumLayout = true, previewMap = map.Name };
    }

    private void CaptureApplicationPreview(string path, MenuItem? menu = null)
    {
        var root = (FrameworkElement)Content;
        RenderTargetBitmap Render(FrameworkElement element)
        {
            var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(element); return image;
        }
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)FindResource("ShellBrush"), null, new Rect(root.RenderSize));
            context.DrawImage(Render(root), new Rect(root.RenderSize));
            if (menu?.Template.FindName("PART_Popup", menu) is Popup { IsOpen: true, Child: FrameworkElement surface })
            {
                var fromDevice = PresentationSource.FromVisual(root)!.CompositionTarget!.TransformFromDevice;
                var offset = fromDevice.Transform(surface.PointToScreen(new())) - fromDevice.Transform(root.PointToScreen(new()));
                context.DrawImage(Render(surface), new Rect(new Point(offset.X, offset.Y), surface.RenderSize));
            }
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
