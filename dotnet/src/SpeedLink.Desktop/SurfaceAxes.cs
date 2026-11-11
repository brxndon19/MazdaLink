using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

/// <summary>Axis rulers attached to the surface's world coordinates, with screen-space text.</summary>
internal sealed class SurfaceAxes : FrameworkElement
{
    private static Brush XColor => UiTheme.Brush("XAxisBrush");
    private static Brush YColor => UiTheme.Brush("YAxisBrush");
    private OrthographicCamera? camera;
    private TuningTable? table;
    private double[] finite = [];
    private bool compact;
    internal string Labels => table is null ? "" : $"{table.X.Name} {table.Y.Name} {table.Units}";
    internal void SetData(TuningTable? definition, double[] values, bool small)
    {
        table = definition; finite = values.Where(double.IsFinite).ToArray(); compact = small; InvalidateVisual();
    }
    internal void SetCamera(OrthographicCamera value) { camera = value; InvalidateVisual(); }
    internal Point Project(Point3D point)
    {
        if (camera is null) return new();
        Vector3D right = Vector3D.CrossProduct(camera.LookDirection, camera.UpDirection); right.Normalize();
        Vector3D up = Vector3D.CrossProduct(right, camera.LookDirection); up.Normalize();
        Vector3D relative = point - (camera.Position + camera.LookDirection);
        double scale = ActualWidth / camera.Width;
        return new(ActualWidth / 2 + Vector3D.DotProduct(relative, right) * scale,
            ActualHeight / 2 - Vector3D.DotProduct(relative, up) * scale);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (camera is null || table is null || table.Rows <= 1 || finite.Length == 0 || ActualWidth < 80 || ActualHeight < 65) return;
        double frontX = camera.Position.X >= 0 ? 1 : -1, frontZ = camera.Position.Z >= 0 ? 1 : -1;
        var left = (from x in new[] { -1d, 1d } from z in new[] { -1d, 1d } select new Point3D(x, 0, z)).MinBy(p => Project(p).X);
        var labels = new List<Rect>();
        var titles = new List<(string Text, Point Position, Vector Outward, Brush Color)>();
        var numbers = new List<(string Text, Point Position, Vector Outward, Brush Color)>();
        Point center = Project(new(0, .5, 0));
        var rulers = new[]
        {
            (Name: table.X.Name, Start: new Point3D(-1, 0, frontZ), End: new Point3D(1, 0, frontZ), Values: table.X.Values, Color: XColor),
            (Name: table.Y.Name, Start: new Point3D(frontX, 0, -1), End: new Point3D(frontX, 0, 1), Values: table.Y.Values, Color: YColor),
            (Name: string.IsNullOrWhiteSpace(table.Units) ? "Value" : compact ? table.Units : $"Value ({table.Units})", Start: left, End: left + new Vector3D(0, 1, 0), Values: new[] { finite.Min(), finite.Max() }, Color: (Brush)UiTheme.Amber)
        };
        foreach (var ruler in rulers)
        {
            Point a = Project(ruler.Start), b = Project(ruler.End); Vector direction = b - a;
            if (direction.Length < 1) continue;
            direction.Normalize(); var normal = new Vector(-direction.Y, direction.X);
            Point middle = a + (b - a) * .5;
            if (Vector.Multiply(normal, middle - center) < 0) normal = -normal;
            var pen = new Pen(ruler.Color, compact ? 1.2 : 1.5);
            dc.DrawLine(pen, a, b);
            dc.DrawLine(pen, b, b - direction * 5 + normal * 2.5);
            dc.DrawLine(pen, b, b - direction * 5 - normal * 2.5);
            titles.Add((ruler.Name, middle + normal * (compact ? 25 : 32), normal, ruler.Color));
            var ticks = ruler.Values.Where(double.IsFinite).Distinct().Order().ToArray();
            if (ticks.Length == 0) continue;
            int count = compact ? 2 : 4;
            foreach (double tick in Enumerable.Range(0, Math.Min(count, ticks.Length)).Select(i => ticks[ticks.Length == 1 ? 0 : (int)Math.Round(i * (ticks.Length - 1d) / (Math.Min(count, ticks.Length) - 1))]).Distinct())
            {
                double scale = Math.Max(1, Math.Max(Math.Abs(ticks[0]), Math.Abs(ticks[^1])));
                double span = ticks[^1] / scale - ticks[0] / scale;
                double fraction = span < 1e-15 ? .5 : (tick / scale - ticks[0] / scale) / span;
                Point point = Project(ruler.Start + (ruler.End - ruler.Start) * fraction);
                dc.DrawLine(pen, point, point + normal * 4);
                numbers.Add((tick.ToString("G4", CultureInfo.InvariantCulture), point + normal * 11, normal, ruler.Color));
            }
        }
        foreach (var label in titles) DrawLabel(label.Text, label.Position, label.Outward, label.Color, true);
        foreach (var label in numbers) DrawLabel(label.Text, label.Position, label.Outward, label.Color, false);
        void DrawLabel(string value, Point position, Vector outward, Brush color, bool title)
        {
            var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(title ? "Segoe UI Semibold" : "Segoe UI"), compact ? 9 : 11, color, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(30, compact && title ? ActualWidth * .4 : Math.Min(240, ActualWidth - 8)), MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis };
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var box = new Rect(Math.Clamp(position.X - text.Width / 2, 3, Math.Max(3, ActualWidth - text.Width - 3)),
                    Math.Clamp(position.Y - text.Height / 2, 3, Math.Max(3, ActualHeight - text.Height - 3)), text.Width, text.Height);
                var padded = box; padded.Inflate(2, 1);
                if (!labels.Any(other => other.IntersectsWith(padded)))
                {
                    labels.Add(padded);
                    dc.DrawRoundedRectangle(UiTheme.Panel, null, padded, 2, 2);
                    dc.DrawText(text, box.TopLeft); return;
                }
                position += outward * 6;
            }
        }
    }
}
