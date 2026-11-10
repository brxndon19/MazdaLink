using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private readonly List<MapPagePreferences> workspacePages = [];
    private int currentPage;
    private bool updatingWorkspaceTabs;

    private void CaptureActivePage()
    {
        workspacePages[currentPage] = workspacePages[currentPage] with
        {
            Maps = mapSlots.Where(s => s.Editor.table is not null).Select(s => CaptureMap(s.Editor)).ToArray(),
            Active = activeMap?.table is { } selected ? MapKey(selected) : "",
            Expanded = expandedMap?.Editor.table is { } expanded ? MapKey(expanded) : "",
            Columns = dockColumns, ColumnFraction = dockColumnFraction, RowFraction = dockRowFraction,
            RowWeights = dockRowWeights, ScalarColumnWidths = scalarColumnWidths
        };
    }

    private void RestoreMapWorkspace()
    {
        if (document is null) return;
        var profile = mapProfiles.FirstOrDefault(p => p.Calibration == document.Definition.Calibration);
        workspacePages.Clear();
        if (profile is { Pages.Length: > 0 }) workspacePages.AddRange(profile.Pages);
        else workspacePages.Add(profile ?? new MapPagePreferences());
        currentPage = Math.Max(0, workspacePages.FindIndex(p => p.Name == profile?.SelectedPage));
        restoringMaps = true;
        try { RestorePage(workspacePages[currentPage]); }
        finally { restoringMaps = false; }
        RebuildWorkspaceTabs();
    }

    private void ShowWorkspace(int index)
    {
        restoringMaps = true;
        try
        {
            graphWindow?.Close();
            infoWindow?.Close();
            ClearMapWindows();
            MapTabs.Items.Clear();
            RestoreMap(this, new() { FitTable = false });
            currentPage = index;
            RestorePage(workspacePages[index]);
            mapDockScroll.ScrollToHome();
            if (activeMap is { table: not null } active)
            {
                var slot = mapSlots.First(s => ReferenceEquals(s.Editor, active));
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (currentPage == index && mapSlots.Contains(slot) && slot.Window is null)
                        slot.Frame.BringIntoView();
                }));
            }
        }
        finally { restoringMaps = false; }
    }

    private void RebuildWorkspaceTabs()
    {
        updatingWorkspaceTabs = true;
        try
        {
            WorkspaceTabs.Items.Clear();
            foreach (var page in workspacePages)
            {
                var label = new TextBlock { Text = page.Name, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
                WorkspaceTabs.Items.Add(new TabItem { Header = label, ToolTip = page.Name, Padding = new(9, 4, 9, 4) });
            }
            WorkspaceTabsBar.Visibility = workspacePages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NewWorkspaceButton.IsEnabled = workspacePages.Count < MapWorkspacePreferences.MaxPages;
            DeleteWorkspaceButton.IsEnabled = workspacePages.Count > 1;
            WorkspaceTabs.SelectedIndex = currentPage;
        }
        finally { updatingWorkspaceTabs = false; }
        BringWorkspaceTabIntoView();
    }

    private void BringWorkspaceTabIntoView()
    {
        if (WorkspaceTabs.SelectedItem is TabItem tab)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => tab.BringIntoView()));
    }

    private void WorkspaceTab_Changed(object sender, SelectionChangedEventArgs e)
    {
        int target = WorkspaceTabs.SelectedIndex;
        if (!ReferenceEquals(e.Source, WorkspaceTabs) || updatingWorkspaceTabs || restoringMaps ||
            target < 0 || target >= workspacePages.Count || currentPage == target) return;
        if (!FinishAllMapEdits())
        {
            updatingWorkspaceTabs = true;
            try { WorkspaceTabs.SelectedIndex = currentPage; }
            finally { updatingWorkspaceTabs = false; }
            BringWorkspaceTabIntoView();
            return;
        }
        CaptureActivePage();
        ShowWorkspace(target);
        BringWorkspaceTabIntoView();
        SaveMapWorkspace();
    }

    private void NewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (workspacePages.Count >= MapWorkspacePreferences.MaxPages || !FinishAllMapEdits()) return;
        if (PromptWorkspaceName("New workspace tab", "", -1) is { } name) AddWorkspace(name);
    }

    private void AddWorkspace(string name)
    {
        CaptureActivePage();
        workspacePages.Add(new() { Name = name });
        ShowWorkspace(workspacePages.Count - 1);
        RebuildWorkspaceTabs();
        SaveMapWorkspace();
    }

    private void RenameWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (PromptWorkspaceName("Rename workspace tab", workspacePages[currentPage].Name, currentPage) is { } name)
            RenameWorkspace(name);
    }

    private void RenameWorkspace(string name)
    {
        workspacePages[currentPage] = workspacePages[currentPage] with { Name = name };
        RebuildWorkspaceTabs();
        SaveMapWorkspace();
    }

    private void DeleteWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (workspacePages.Count <= 1 || !FinishAllMapEdits()) return;
        var choice = MessageBox.Show(Window.GetWindow(this),
            $"Remove the “{workspacePages[currentPage].Name}” tab and its layout? Applied ROM edits are kept.",
            "Delete workspace tab", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (choice == MessageBoxResult.Yes) DeleteWorkspace();
    }

    private void DeleteWorkspace()
    {
        workspacePages.RemoveAt(currentPage);
        ShowWorkspace(Math.Min(currentPage, workspacePages.Count - 1));
        RebuildWorkspaceTabs();
        SaveMapWorkspace();
    }

    private string? PromptWorkspaceName(string title, string initial, int renamedIndex)
    {
        var dialog = Floating(title, 360, 200);
        dialog.MinHeight = 0;
        dialog.ResizeMode = ResizeMode.NoResize;
        dialog.SizeToContent = SizeToContent.Height;
        var form = new StackPanel { Margin = new(18) };
        form.Children.Add(new TextBlock { Text = "Workspace name", Margin = new(0, 0, 0, 6) });
        var input = new TextBox { Text = initial, MaxLength = MapPagePreferences.MaxNameLength, MinHeight = 28 };
        System.Windows.Automation.AutomationProperties.SetName(input, "Workspace name");
        form.Children.Add(input);
        var error = new TextBlock { Foreground = UiTheme.Amber, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        form.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var confirm = new Button { Content = renamedIndex < 0 ? "Create" : "Rename", IsDefault = true };
        actions.Children.Add(confirm);
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new(6, 0, 0, 0) });
        form.Children.Add(actions);
        confirm.Click += (_, _) =>
        {
            string name = input.Text.Trim();
            bool duplicate = workspacePages.Where((_, i) => i != renamedIndex)
                .Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!MapPagePreferences.ValidName(name) || duplicate)
            {
                error.Text = duplicate ? "That workspace name is already in use." : "Enter a workspace name.";
                error.Visibility = Visibility.Visible;
                input.Focus();
                return;
            }
            dialog.Tag = name;
            dialog.DialogResult = true;
        };
        dialog.Content = form;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? (string)dialog.Tag : null;
    }
}
