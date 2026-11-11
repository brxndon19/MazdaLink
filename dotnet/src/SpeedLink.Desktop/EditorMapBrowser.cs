using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

internal sealed record MapBrowserLabel(Geometry Icon, string Text);

public partial class EditorWorkspace
{
    private static readonly Geometry FolderIcon = Icon("M1,4 H6 L8,6 H15 V13 H1 Z");
    private static readonly Geometry SettingIcon = Icon("M2,5 H14 M2,11 H14 M6,2 V8 M10,8 V14");
    private static readonly Geometry CurveIcon = Icon("M2,2 V14 H15 M3,11 L7,7 L10,9 L14,3");
    private static readonly Geometry SurfaceIcon = Icon("M1,4 H15 V13 H1 Z M1,7 H15 M1,10 H15 M5,4 V13 M10,4 V13");
    private HashSet<string> expandedMapFolders = new(StringComparer.Ordinal);
    private bool mapBrowserFiltered;

    private static Geometry Icon(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private static string[] CategoryFolders(string category)
    {
        var parts = category.Split(" / ", 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? ["Other"] : parts;
    }

    private static bool IsMapFolder(string folder, string category) =>
        category == folder || category.StartsWith(folder + " / ", StringComparison.Ordinal);

    private static IEnumerable<TreeViewItem> MapTreeEntries(ItemsControl parent)
    {
        foreach (var entry in parent.Items.OfType<TreeViewItem>())
        {
            yield return entry;
            foreach (var child in MapTreeEntries(entry)) yield return child;
        }
    }

    internal TreeViewItem RevealMapEntry(TuningTable map)
    {
        foreach (var folder in MapTreeEntries(MapTree).Where(i => i.Tag is string path && IsMapFolder(path, map.Category)))
            folder.IsExpanded = true;
        return MapTreeEntries(MapTree).Single(i => ReferenceEquals(i.Tag, map));
    }

    private void BuildMapFolders(ItemsControl parent, (TuningTable Map, string[] Folders)[] entries, int depth, string path, DataTemplate header)
    {
        foreach (var group in entries.Where(entry => entry.Folders.Length > depth).GroupBy(entry => entry.Folders[depth]))
        {
            string key = depth == 0 ? group.Key : path + " / " + group.Key;
            string label = $"{group.Key}  ({group.Count()})";
            var folder = new TreeViewItem
            {
                Header = new MapBrowserLabel(FolderIcon, label), HeaderTemplate = header, Tag = key, ToolTip = key,
                IsExpanded = mapBrowserFiltered || expandedMapFolders.Contains(key), Padding = new(2, 4, 0, 4)
            };
            AutomationProperties.SetName(folder, label);
            parent.Items.Add(folder);
            BuildMapFolders(folder, group.ToArray(), depth + 1, key, header);
        }
        foreach (var (map, _) in entries.Where(entry => entry.Folders.Length == depth))
        {
            var (icon, kind) = map.IsChoice || map.Count == 1 ? (SettingIcon, "Setting") :
                map.Rows > 1 && map.Columns > 1 ? (SurfaceIcon, "Surface") : (CurveIcon, "Curve");
            bool changed = document!.TableChanged(map, Baseline);
            var entry = new TreeViewItem
            {
                Header = new MapBrowserLabel(icon, (changed ? "● " : "") + map.Name), HeaderTemplate = header, Tag = map,
                ToolTip = $"{kind} · {map.Name}", Padding = new(2, 3, 0, 3)
            };
            AutomationProperties.SetName(entry, changed ? $"{map.Name}, {kind}, changed" : $"{map.Name}, {kind}");
            entry.PreviewMouseLeftButtonDown += (_, _) => { if (entry.IsSelected) Attempt(() => BrowseMap(map)); };
            parent.Items.Add(entry);
        }
    }

}
