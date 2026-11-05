using System.Windows;
using System.Windows.Controls;
namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal void OpenEditorFile(string? path = null) { Navigate(1); if (path is not null) EditorPane.OpenRom(path); }
    internal void CloseWorkbenchTest() { EditorPane.CloseOfflineTest(); Close(); }
    internal async Task<object> ExerciseWorkbenchOffline(string path, string folder, string? recording = null)
    {
        Show(); Navigate(1); UpdateLayout();
        await CheckMapBrowserRestart(path, folder);
        var report = await EditorPane.ExerciseOffline(path, folder, () => { Navigate(2); Navigate(3); Navigate(1); });
        if (transport is not null || busy) throw new InvalidOperationException("Offline workbench accessed a transport.");
        var document = EditorPane.Document;
        foreach (int page in Enumerable.Range(0, Pages.Items.Count))
        {
            Navigate(page); UpdateLayout();
            if (Pages.SelectedIndex != page || !ReferenceEquals(EditorPane.Document, document)) throw new InvalidOperationException("Workspace navigation lost the editor.");
        }
        if (recording is not null)
        {
            Navigate(3); await RecordingsPane.OpenAsync(recording); RecordingsPane.SetView(15, 2); UpdateLayout();
            if (RecordingsPane.PlottedChannels == 0 || RecordingsPane.Recording is null) throw new InvalidOperationException("Embedded recording did not load.");
            CaptureWorkbench(System.IO.Path.Combine(folder, "RecordedWorkbench.png"));
        }
        Navigate(1); EditorPane.DetailsButton.IsChecked = true; UpdateLayout();
        CaptureWorkbench(System.IO.Path.Combine(folder, "DetailsWorkbench.png"));
        EditorPane.DetailsButton.IsChecked = false; UpdateLayout();
        CaptureWorkbench(System.IO.Path.Combine(folder, "Workbench.png"));
        EditorPane.FocusButton.IsChecked = true; EditorPane.SetViewMode(1); UpdateLayout();
        CaptureWorkbench(System.IO.Path.Combine(folder, "FocusedWorkbench.png"));
        Width = MinWidth; Height = MinHeight; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (EditorPane.MapGrid.ActualWidth < 100 || EditorPane.GraphPanel.ActualWidth < 100 || EditorPane.SaveButton.TransformToAncestor(this).Transform(new Point(0, 0)).X > ActualWidth - 60)
            throw new InvalidOperationException("Minimum workbench layout is clipped.");
        CaptureWorkbench(System.IO.Path.Combine(folder, "MinimumWorkbench.png"));
        return new { passed = true, editor = report, embedded = true, savedHiddenBrowserRecovered = true, savedFloatingBrowserRecovered = true, navigationPreservesEdits = true, minimumLayout = true, hardwareAccessed = false };
    }
    private void CaptureWorkbench(string path)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(path); encoder.Save(output);
    }
    private void Navigate(int page)
    {
        if (flashWorkspace?.HardwareInUse == true && page != FlashPage) return;
        if (page == FlashPage && FlashTab is not null) ShowFlashWorkspace();
        Pages.SelectedIndex = page;
        EditorPane?.SetWorkspaceActive(page == 1);
        foreach (var button in Navigation.Children.OfType<RadioButton>()) button.IsChecked = (string)button.Tag == page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RefreshApplicationMenus();
    }
    private void Navigate_Checked(object sender, RoutedEventArgs e)
    {
        if (Pages is not null && sender is RadioButton { Tag: string tag }) Pages.SelectedIndex = int.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
    }
    private void Pages_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, Pages) && Navigation is not null) Navigate(Pages.SelectedIndex);
    }
}
