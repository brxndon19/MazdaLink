using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private KeyEventArgs SendMapHelpKey()
    {
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(MapGrid), Environment.TickCount, Key.F1)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        MapGrid.RaiseEvent(key); return key;
    }
    private void CheckNoMapHelp()
    {
        if (MapInfoButton.IsEnabled || DetailsButton.IsEnabled || SendMapHelpKey().Handled || infoWindow is not null)
            throw new InvalidOperationException("Map help is available without an active map.");
        MapInfoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (infoWindow is not null) throw new InvalidOperationException("Map help opened without an active map.");
    }
    private void CheckMapHelpPlacement()
    {
        var bounds = MapInfoButton.TransformToAncestor(this).TransformBounds(new Rect(MapInfoButton.RenderSize));
        if (!MapInfoButton.IsVisible || !MapInfoButton.IsEnabled || bounds.Right > ActualWidth + 1 ||
            bounds.Bottom > DataLayout.TransformToAncestor(this).Transform(new Point()).Y + 1 || bounds.Width < 70)
            throw new InvalidOperationException("Map info is clipped or takes space from the table.");
    }
    private async Task CheckMapInformation(string folder)
    {
        var snapshot = document!.Snapshot();
        var previous = table;
        var sample = document.Definition.Tables.Single(t => t.Name == "Knock Retard Additive");
        OpenTable(sample); UpdateLayout(); CheckMapHelpPlacement();
        double height = MapGrid.ActualHeight;
        MapInfoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); infoWindow!.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (HelpUnitsText.Text != "Values: " + sample.Units || !HelpUnitsText.IsVisible ||
            !HelpAxesText.Text.Contains(sample.X.Name, StringComparison.Ordinal) ||
            !HelpAxesText.Text.Contains(sample.Y.Name, StringComparison.Ordinal) || HelpText.Text != sample.Description ||
            Descendants(DetailsPanel).OfType<Expander>().Single().IsExpanded)
            throw new InvalidOperationException("Map information hides units or does not match the selected map.");
        if (!DetailsButton.IsChecked || Math.Abs(MapGrid.ActualHeight - height) > 1)
            throw new InvalidOperationException("Map info changed the table height.");
        infoWindow.Close(); await Settle();
        if (DetailsButton.IsChecked || infoWindow is not null) throw new InvalidOperationException("Closing help retained an open state.");
        if (!SendMapHelpKey().Handled || infoWindow is null || !infoWindow.IsVisible)
            throw new InvalidOperationException("F1 did not open help for the active map.");
        var opened = infoWindow; opened.WindowState = WindowState.Minimized;
        SendMapHelpKey(); await Settle();
        MapInfoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!ReferenceEquals(infoWindow, opened) || opened.WindowState == WindowState.Minimized)
            throw new InvalidOperationException("Repeated help requests duplicate or leave the window minimized.");
        // Changing maps while the window is open must replace both help and units.
        var injector = document.Definition.Tables.First(t => t.Name.StartsWith("Injector Base Pulsewidth - ", StringComparison.Ordinal));
        OpenTable(injector); UpdateLayout();
        if (HelpUnitsText.Text != "Values: " + injector.Units || HelpText.Text != injector.Description ||
            !GuideTopic.IsVisible || GuideLink.NavigateUri?.Fragment != "#injector-pulsewidth")
            throw new InvalidOperationException("Map information retained the previous map's units or description.");
        var boost = document.Definition.Tables.Single(t => t.Name == "BCS Correction Mode");
        OpenTable(boost); UpdateLayout(); infoWindow.UpdateLayout();
        if (!GuideTopic.IsVisible || GuideLink.NavigateUri?.Fragment != "#boost-and-wastegate-control" || HelpText.Text != boost.Description)
            throw new InvalidOperationException("BCS Correction Mode help did not link to the boost guide.");
        CaptureWindow(infoWindow, Path.Combine(folder, "BoostModeHelp.png"));
        var unrelated = document.Definition.Tables.First(t => t.Category == "Idle");
        OpenTable(unrelated); UpdateLayout();
        if (GuideTopic.IsVisible || GuideLink.NavigateUri is not null)
            throw new InvalidOperationException("Map information retained the previous guide topic.");
        OpenTable(sample); UpdateLayout(); infoWindow.UpdateLayout();
        CaptureWindow(infoWindow, Path.Combine(folder, "MapInformation.png"));
        var active = MapTabs.SelectedItem; MapTabs.SelectedIndex = -1; await Settle(); CheckNoMapHelp();
        MapTabs.SelectedItem = active;
        if (previous is not null) OpenTable(previous);
        UpdateLayout();
        if (!snapshot.AsSpan().SequenceEqual(document.Snapshot()))
            throw new InvalidOperationException("Viewing map information changed ROM bytes.");
    }
}
