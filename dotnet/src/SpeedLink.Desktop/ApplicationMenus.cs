using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private readonly string appearancePath;
    private Appearance appearance = Appearance.Dark;

    private void ConfigureApplicationMenus()
    {
        EditorPane.AttachApplicationMenus(ApplicationEditMenu, ApplicationMapViewMenu);
        ApplicationSaveMenu.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = EditorPane.SaveButton });
        RefreshApplicationMenus();
        RefreshAppearanceMenu();
    }

    private void RefreshAppearanceMenu()
    {
        foreach (var item in AppearanceMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag as string == appearance.ToString();
    }

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string name } || !Enum.TryParse(name, out Appearance choice)) return;
        appearance = choice;
        UiTheme.Apply(appearance);
        try
        {
            UiTheme.Save(appearancePath, choice);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Appearance could not be saved. " + error.Message;
        }
        RefreshAppearanceMenu();
    }

    private void WindowsAppearanceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (appearance == Appearance.System) UiTheme.Apply(appearance);
        }));
    }

    private void RefreshApplicationMenus()
    {
        if (ApplicationMenu is null || EditorPane is null) return;
        ApplicationEditMenu.IsEnabled = Pages.SelectedIndex == 1;
        ApplicationMapViewMenu.IsEnabled = Pages.SelectedIndex == 1;
        foreach (var item in WorkspaceMenu.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag as string == Pages.SelectedIndex.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplicationOpenRom_Click(object sender, RoutedEventArgs e) { Navigate(1); EditorPane.RequestOpen(); }
    private void ApplicationSave_Click(object sender, RoutedEventArgs e) => EditorPane.RequestSave();
    private void ApplicationOpenRecording_Click(object sender, RoutedEventArgs e) { Navigate(3); RecordingsPane.RequestOpen(); }
    private void ApplicationExit_Click(object sender, RoutedEventArgs e) => Close();
    private void ApplicationGuide_Click(object sender, RoutedEventArgs e) => HelpWindow((sender as MenuItem)?.Tag as string ?? "Getting started").Show();
    private void NavigationLabels_Click(object sender, RoutedEventArgs e)
    {
        navigationIsCollapsed = !NavigationLabelsMenu.IsChecked;
        ApplyNavigation(); EditorPane.SaveNavigationLayout();
    }
    private void WorkspaceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out int page) && page >= 0 && page < Pages.Items.Count) Navigate(page);
    }
    private void Application_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || flashWorkspace?.HardwareInUse == true) return;
        if (e.Key == Key.O) ApplicationOpenRom_Click(sender, e);
        else if (e.Key == Key.S && ApplicationSaveMenu.IsEnabled) ApplicationSave_Click(sender, e);
        else if (e.Key is >= Key.D1 and <= Key.D7) Navigate(e.Key - Key.D1);
        else return;
        e.Handled = true;
    }
}

public partial class EditorWorkspace
{
    internal void RequestSave() => Save_Click(this, new());

    internal void AttachApplicationMenus(MenuItem edit, MenuItem view)
    {
        // Move the existing commands so the main window and each map continue to
        // use the same editing, pending-input and mandatory-save checks.
        var menus = EditorMenu.Items.OfType<MenuItem>().ToArray();
        MenuItem UndoRedo(string header, string shortcut, Button button, RoutedEventHandler action)
        {
            var item = new MenuItem { Header = header, InputGestureText = shortcut };
            item.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = button });
            item.Click += action; return item;
        }
        edit.Items.Add(UndoRedo("_Undo", "Ctrl+Z", UndoButton, Undo_Click));
        edit.Items.Add(UndoRedo("_Redo", "Ctrl+Y", RedoButton, Redo_Click));
        edit.Items.Add(new Separator());
        foreach (var item in menus[1].Items.OfType<MenuItem>().Take(3))
            item.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = FitMapButton });
        MoveItems(menus[1], edit); MoveItems(menus[2], view);
        EditorMenu.Visibility = Visibility.Collapsed;
        MapsButton.Margin = new(0);
        static void MoveItems(MenuItem source, MenuItem target)
        {
            while (source.Items.Count > 0) { var item = source.Items[0]; source.Items.RemoveAt(0); target.Items.Add(item); }
        }
    }
}
