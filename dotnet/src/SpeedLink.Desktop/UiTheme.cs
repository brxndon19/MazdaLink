using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace SpeedLink.Desktop;

internal enum Appearance { System, Light, Dark }

internal static class UiTheme
{
    private sealed class Swatch : DependencyObject
    {
        private static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register(nameof(Color), typeof(Color), typeof(Swatch));
        private readonly Color dark, light;
        public Color Color => (Color)GetValue(ColorProperty);
        internal SolidColorBrush Brush { get; } = new();

        internal Swatch(Color dark, Color light)
        {
            this.dark = dark;
            this.light = light;
            SetValue(ColorProperty, dark);
            // A bound brush cannot be frozen by WPF when it seals a style.
            BindingOperations.SetBinding(Brush, SolidColorBrush.ColorProperty, new Binding(nameof(Color)) { Source = this });
        }

        internal void Apply(bool useLight) => SetValue(ColorProperty, useLight ? light : dark);
    }

    private static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static Swatch Pair(uint dark, uint light) => new(Rgb(dark), Rgb(light));
    private static readonly Dictionary<string, Swatch> palette = new()
    {
        ["ShellBrush"] = Pair(0x191C1F, 0xE9EDF1),
        ["PanelBrush"] = Pair(0x23272B, 0xFFFFFF),
        ["RaisedBrush"] = Pair(0x2D3237, 0xF5F7F9),
        ["LineBrush"] = Pair(0x434A50, 0xA1ADB8),
        ["TextBrush"] = Pair(0xEDF0F2, 0x1C2731),
        ["MutedBrush"] = Pair(0xB2BBC2, 0x495866),
        ["AxisBandBrush"] = Pair(0x303B45, 0xDAE4ED),
        ["AxisBorderBrush"] = Pair(0x899AA8, 0x687D8F),
        ["AmberBrush"] = Pair(0xF2B65D, 0x875100),
        ["AmberSoftBrush"] = Pair(0x493A25, 0xF7E5C3),
        ["InkBrush"] = Pair(0x211C15, 0xFFFFFF),
        ["CellSelectionBrush"] = Pair(0x5D4624, 0xFFDA8D),
        ["ChangedCellBrush"] = Pair(0xFF9C37, 0x875100),
        ["InvalidCellBrush"] = Pair(0x793937, 0xFFD6D0),
        ["ErrorBrush"] = Pair(0xFFB1A6, 0x9F3026),
        ["AlternatingRowBrush"] = Pair(0x272C30, 0xF1F4F7),
        ["ScrollTrackBrush"] = Pair(0x1C2125, 0xE5EBF0),
        ["ScrollThumbBrush"] = Pair(0x71828E, 0x8496A6),
        ["ScrollHoverBrush"] = Pair(0xA0B2BF, 0x64798A),
        ["ScrollGlyphBrush"] = Pair(0xCBD4DB, 0x394D5F),
        ["XAxisBrush"] = Pair(0x7CC4ED, 0x166391),
        ["YAxisBrush"] = Pair(0x7DD5B3, 0x28674F)
    };
    private static readonly Swatch[] heat = Enumerable.Range(0, 33).Select(index =>
    {
        double t = index / 32d;
        return new Swatch(Color.FromRgb((byte)(39 + 48 * t), (byte)(43 + 23 * t), (byte)(46 - 7 * t)),
            Color.FromRgb((byte)(235 + 17 * t), (byte)(239 - 24 * t), (byte)(242 - 83 * t)));
    }).ToArray();

    internal static SolidColorBrush Brush(string name) => palette[name].Brush;
    internal static SolidColorBrush Text => Brush("TextBrush");
    internal static SolidColorBrush Muted => Brush("MutedBrush");
    internal static SolidColorBrush Panel => Brush("PanelBrush");
    internal static SolidColorBrush Line => Brush("LineBrush");
    internal static SolidColorBrush Amber => Brush("AmberBrush");
    internal static SolidColorBrush Heat(double fraction) => heat[(int)Math.Round(Math.Clamp(fraction, 0, 1) * 32)].Brush;

    internal static void Ensure()
    {
        if (Application.Current is not { } app || app.Resources.Contains("ShellBrush")) return;
        foreach (var (name, color) in palette) app.Resources[name] = color.Brush;
        app.Resources[SystemColors.MenuBrushKey] = Panel;
        app.Resources[SystemColors.MenuTextBrushKey] = Text;
        app.Resources[SystemColors.WindowBrushKey] = Panel;
        app.Resources[SystemColors.WindowTextBrushKey] = Text;
        app.Resources[SystemColors.ControlBrushKey] = Brush("RaisedBrush");
        app.Resources[SystemColors.ControlTextBrushKey] = Text;
        app.Resources[SystemColors.HighlightBrushKey] = Amber;
        app.Resources[SystemColors.HighlightTextBrushKey] = Brush("InkBrush");
        app.Resources.MergedDictionaries.Add(new() { Source = new Uri("/SpeedLink;component/Theme.xaml", UriKind.Relative) });
    }

    internal static void Apply(Appearance choice)
    {
        Ensure();
        bool light = choice == Appearance.Light || choice == Appearance.System && WindowsUsesLightApps();
        foreach (var swatch in palette.Values) swatch.Apply(light);
        foreach (var swatch in heat) swatch.Apply(light);
    }

    private static bool WindowsUsesLightApps()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is not 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return true; }
    }

    internal static Appearance Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 16) return Appearance.System;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim() switch { "Light" => Appearance.Light, "Dark" => Appearance.Dark, _ => Appearance.System };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Appearance.System; }
    }

    internal static void Save(string path, Appearance choice)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, choice.ToString());
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
