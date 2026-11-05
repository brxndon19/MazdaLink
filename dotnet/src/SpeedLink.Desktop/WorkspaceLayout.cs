using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private bool navigationIsCollapsed;
    private void ConfigureWorkspaceLayout()
    {
        string path = Path.Combine(vault, "editor-layout.json");
        navigationIsCollapsed = EditorLayoutPreferences.Load(path).NavigationCollapsed;
        ApplyNavigation(); EditorPane.ConfigureLayout(path, () => navigationIsCollapsed);
    }
    private void NavigationToggle_Click(object sender, RoutedEventArgs e)
    { navigationIsCollapsed = !navigationIsCollapsed; ApplyNavigation(); EditorPane.SaveNavigationLayout(); }
    private void ApplyNavigation()
    {
        NavigationColumn.Width = new(navigationIsCollapsed ? 52 : 155);
        foreach (var label in new[] { NavLabel0, NavLabel1, NavLabel2, NavLabel3, NavLabel4, NavLabel5, NavLabel6 }) label.Visibility = navigationIsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NavigationToggle.ToolTip = navigationIsCollapsed ? "Expand navigation" : "Collapse navigation";
        NavigationToggle.Content = navigationIsCollapsed ? "☰" : "☰  Navigation";
        NavigationToggle.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, navigationIsCollapsed ? "Show navigation labels" : "Hide navigation labels");
        NavigationLabelsMenu.IsChecked = !navigationIsCollapsed;
    }
}
