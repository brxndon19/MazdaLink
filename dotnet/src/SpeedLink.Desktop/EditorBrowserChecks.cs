using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private void CheckBrowserReady(bool floating)
    {
        if (FocusButton.IsChecked || !BrowserPanel.IsVisible || BrowserFloating != floating ||
            !floating && BrowserColumn.ActualWidth < 160 || !MapsButton.IsVisible || !MapsButton.IsEnabled ||
            !NoMapPanel.IsVisible || table is not null || FitMapButton.IsEnabled || TablePanel.IsVisible ||
            MapTreeEntries(MapTree).Count(entry => entry.Tag is TuningTable) != document!.Definition.Tables.Count ||
            !NoMapText.Text.Contains(document.Definition.Calibration, StringComparison.Ordinal))
            throw new InvalidOperationException("A loaded ROM has no accessible map browser or map-selection prompt.");
    }

    private async Task CheckMapBrowserAccess(string path, string folder)
    {
        UpdateLayout(); CheckBrowserReady(false);
        Capture(Path.Combine(folder, "MapBrowserReady.png"));
        FitMap_Click(this, new()); UpdateLayout(); CheckBrowserReady(false);
        FocusButton.IsChecked = true;
        MapsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); CheckBrowserReady(false);

        var selected = document!.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        RevealMapEntry(selected).IsSelected = true;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        if (!ReferenceEquals(table, selected) || NoMapPanel.IsVisible)
            throw new InvalidOperationException("Choosing a map from the browser did not open its editor.");
        document.Apply([new(selected, 0, document.Read(selected, 0) + 1)], "Browser recovery fixture");
        byte[] edited = document.Snapshot();
        FocusButton.IsChecked = true; MapTabs.Items.Clear(); UpdateLayout();
        if (!NoMapPanel.IsVisible || !document.IsDirty || !edited.AsSpan().SequenceEqual(document.Snapshot()))
            throw new InvalidOperationException("Closing the final map lost the selection prompt or ROM edits.");
        NoMapAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); CheckBrowserReady(false);
        if (!edited.AsSpan().SequenceEqual(document.Snapshot())) throw new InvalidOperationException("Showing maps changed ROM edits.");
        RevealMapEntry(selected).IsSelected = true;
        if (!ReferenceEquals(table, selected) || NoMapPanel.IsVisible)
            throw new InvalidOperationException("The last closed map cannot be selected again from the browser.");
        document.Undo();

        PopBrowser_Click(this, new()); browserWindow!.WindowState = WindowState.Minimized;
        FocusButton.IsChecked = true; OpenRom(path);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // Restored map views are covered separately. Close them deliberately to
        // verify that the empty-map prompt still recovers the floating browser.
        ClearMapWindows(); MapTabs.Items.Clear(); ArrangeMaps();
        UpdateLayout(); CheckBrowserReady(true);
        if (browserWindow!.WindowState != WindowState.Normal || !browserWindow.IsVisible)
            throw new InvalidOperationException("Opening a ROM did not restore its minimized floating map browser.");
        DockBrowser_Click(this, new()); UpdateLayout(); CheckBrowserReady(false);
        if (document!.IsDirty || !File.ReadAllBytes(path).AsSpan().SequenceEqual(document.Snapshot()))
            throw new InvalidOperationException("Map browser checks changed the input ROM.");
    }

    internal void CheckBrowserAfterRestart(bool floating)
    {
        UpdateLayout(); CheckBrowserReady(floating);
        if (layoutPath is null || EditorLayoutPreferences.Load(layoutPath).BrowserHidden)
            throw new InvalidOperationException("The recovered map browser stayed hidden in the saved layout.");
    }
}

public partial class MainWindow
{
    private async Task CheckMapBrowserRestart(string path, string folder)
    {
        foreach (bool floating in new[] { false, true })
        {
            string testVault = Path.Combine(folder, floating ? "floating-browser-restart" : "hidden-browser-restart");
            new EditorLayoutPreferences { BrowserHidden = true, BrowserFloating = floating }.Save(Path.Combine(testVault, "editor-layout.json"));
            var restarted = new MainWindow(testVault, EditorPane.TestInspector, EditorPane.TestGuard);
            try
            {
                restarted.Show(); restarted.Navigate(1); restarted.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                restarted.OpenEditorFile(path);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                restarted.EditorPane.CheckBrowserAfterRestart(floating);
            }
            finally { restarted.CloseWorkbenchTest(); }
        }
    }
}
