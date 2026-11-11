using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

/// <summary>Independent WPF curve/surface viewer. Graph gestures select cells; values are edited in the grid.</summary>
public sealed partial class TuningGraph : Grid
{
    private readonly Viewport3D viewport = new() { Margin = new(4) };
    private readonly CurvePlot curve = new();
    private readonly SurfaceAxes axes = new() { IsHitTestVisible = false, Margin = new(4) };
    private readonly OrthographicCamera camera = new() { UpDirection = new(0, 1, 0), NearPlaneDistance = .1, FarPlaneDistance = 200 };
    private TuningTable? table;
    private double[] values = [];
    private int selected = -1;
    private double yaw = 40, elevation = 32, distance = 5;
    private Point? dragStart;
    private Point lastDrag;
    private GeometryModel3D? surface;
    private bool compactPreview;
    private bool Surface => table is { Rows: > 1, Columns: > 1 };
    internal Rect SurfaceFrame { get; private set; }
    internal Size SurfaceViewport => viewport.RenderSize;
    internal int SelectedCell => selected;
    internal void SetCompactPreview(bool compact)
    {
        compactPreview = compact;
        axes.SetData(table, values, compact);
        Camera();
    }
    public event Action<int>? CellPicked;
    public TuningGraph()
    {
        Background = Brushes.Transparent; ClipToBounds = true;
        Children.Add(viewport); Children.Add(curve); Children.Add(axes); viewport.Camera = camera;
        MouseLeftButtonDown += Down; MouseMove += Move; MouseLeftButtonUp += Up;
        MouseWheel += (_, e) => { if (Surface) { distance = Math.Clamp(distance * (e.Delta > 0 ? .9 : 1.1), 2.6, 12); Camera(); } else curve.Zoom(e.GetPosition(curve).X, e.Delta > 0 ? .8 : 1.25); e.Handled = true; };
        LostMouseCapture += (_, _) => dragStart = null;
        SizeChanged += (_, _) => Camera();
        viewport.SizeChanged += (_, _) => Camera();
    }
    public void SetData(TuningTable? definition, double[] data, int selection)
    {
        bool different = !ReferenceEquals(table, definition); table = definition; values = data; selected = selection;
        if (different) { yaw = 40; elevation = 32; distance = 5; curve.ResetView(); ToolTip = null; }
        Render();
    }
    public void Select(int index) { selected = index; curve.EnsureVisible(index); Render(); }
    internal double CurveVisibleFraction => curve.VisibleFraction;
    internal void ZoomCurve(double factor) => curve.Zoom(curve.ActualWidth / 2, factor);
    internal void PickCurvePoint(int index) { int nearest = curve.Nearest(curve.PixelAt(index)); if (nearest >= 0) CellPicked?.Invoke(nearest); }
    public void ResetView() { yaw = 40; elevation = 32; distance = 5; curve.ResetView(); Render(); }
    internal MapPanePreferences CaptureView(MapPanePreferences state) => state with { Yaw = yaw, Elevation = elevation, Distance = distance, CurveStart = curve.Start, CurveEnd = curve.End };
    internal void RestoreView(MapPanePreferences state)
    {
        yaw = state.Yaw; elevation = state.Elevation; distance = state.Distance;
        curve.RestoreRange(state.CurveStart, state.CurveEnd); Render();
    }
    private void Camera()
    {
        if (surface?.Geometry is not MeshGeometry3D mesh || mesh.Positions.Count == 0) return;
        double az = yaw * Math.PI / 180, el = elevation * Math.PI / 180;
        var direction = new Vector3D(Math.Cos(el) * Math.Sin(az), Math.Sin(el), Math.Cos(el) * Math.Cos(az));
        var right = Vector3D.CrossProduct(-direction, new(0, 1, 0)); right.Normalize(); var up = Vector3D.CrossProduct(right, -direction); up.Normalize();
        var bounds = mesh.Bounds; var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        var frame = mesh.Positions.Concat(from x in new[] { -1d, 1d } from y in new[] { 0d, 1d } from z in new[] { -1d, 1d } select new Point3D(x, y, z));
        var projected = frame.Select(p => new Point(Vector3D.DotProduct(p - center, right), Vector3D.DotProduct(p - center, up))).ToArray();
        double spanX = Math.Max(.01, projected.Max(p => p.X) - projected.Min(p => p.X)), spanY = Math.Max(.01, projected.Max(p => p.Y) - projected.Min(p => p.Y));
        center += right * ((projected.Max(p => p.X) + projected.Min(p => p.X)) / 2) + up * ((projected.Max(p => p.Y) + projected.Min(p => p.Y)) / 2);
        double width = Math.Max(1, viewport.ActualWidth), height = Math.Max(1, viewport.ActualHeight);
        double padding = Math.Clamp(Math.Min(width, height) * .1, 8, 40);
        double usableWidth = Math.Max(1, width - padding * 2), usableHeight = Math.Max(1, height - padding * 2);
        camera.Width = Math.Max(spanX / usableWidth, spanY / usableHeight) * width * 1.04 * distance / 5;
        camera.Position = center + direction * 10; camera.LookDirection = -direction * 10;
        axes.SetCamera(camera);
        double pixelScale = viewport.ActualWidth / camera.Width;
        SurfaceFrame = new((viewport.ActualWidth - spanX * pixelScale) / 2, (viewport.ActualHeight - spanY * pixelScale) / 2, spanX * pixelScale, spanY * pixelScale);
    }
    private void Render()
    {
        viewport.Children.Clear(); surface = null; SurfaceFrame = Rect.Empty;
        viewport.Visibility = axes.Visibility = Surface ? Visibility.Visible : Visibility.Collapsed;
        curve.Visibility = Surface ? Visibility.Collapsed : Visibility.Visible;
        curve.Table = table; curve.Values = values; curve.Selected = selected; curve.InvalidateVisual();
        axes.SetData(table, values, compactPreview);
        if (!Surface || table is null) return;
        var finite = values.Where(double.IsFinite).ToArray(); if (finite.Length == 0) return;
        double min = finite.Min(), max = finite.Max(), scale = Math.Max(1, Math.Max(Math.Abs(min), Math.Abs(max))), low = min / scale, span = max / scale - low;
        static double Coordinate(double value, double[] axis) { double min = axis.Min(), max = axis.Max(); return max == min ? 0 : (value - min) / (max - min) * 2 - 1; }
        var mesh = new MeshGeometry3D();
        for (int row = 0; row < table.Rows; row++) for (int col = 0; col < table.Columns; col++)
        {
            double value = values[row * table.Columns + col]; double height = !double.IsFinite(value) || span < 1e-15 ? .5 : (value / scale - low) / span;
            mesh.Positions.Add(new(Coordinate(table.X.Values[col], table.X.Values), height, Coordinate(table.Y.Values[row], table.Y.Values)));
            mesh.TextureCoordinates.Add(new(.5, 1 - height));
        }
        for (int row = 0; row < table.Rows - 1; row++) for (int col = 0; col < table.Columns - 1; col++)
        {
            int a = row * table.Columns + col, b = a + table.Columns, c = a + 1, d = b + 1;
            if (new[] { a, b, c, d }.Any(i => !double.IsFinite(values[i]))) continue;
            foreach (int i in new[] { a, b, c, c, b, d }) mesh.TriangleIndices.Add(i);
        }
        var brush = new LinearGradientBrush { StartPoint = new(0, 1), EndPoint = new(0, 0), GradientStops = [new(Color.FromRgb(96, 112, 124), 0), new(Color.FromRgb(171, 149, 104), .5), new(Color.FromRgb(242, 182, 93), 1)] };
        var material = new DiffuseMaterial(brush); surface = new(mesh, material) { BackMaterial = material };
        var group = new Model3DGroup(); group.Children.Add(new AmbientLight(Color.FromRgb(150, 150, 150))); group.Children.Add(new DirectionalLight(Colors.White, new(-1, -3, -2))); group.Children.Add(surface);
        var wire = new MeshGeometry3D();
        void Line(Point3D a, Point3D b)
        {
            a.Y += .004; b.Y += .004; var normal = Vector3D.CrossProduct(b - a, new(0, 1, 0)); if (normal.Length < .00001) return;
            normal.Normalize(); normal *= .0018; int start = wire.Positions.Count;
            foreach (var p in new[] { a + normal, a - normal, b + normal, b - normal }) wire.Positions.Add(p);
            foreach (int i in new[] { 0, 1, 2, 2, 1, 3 }) wire.TriangleIndices.Add(start + i);
        }
        for (int row = 0; row < table.Rows; row++) for (int col = 0; col < table.Columns; col++)
        {
            int at = row * table.Columns + col;
            if (col + 1 < table.Columns && double.IsFinite(values[at]) && double.IsFinite(values[at + 1])) Line(mesh.Positions[at], mesh.Positions[at + 1]);
            if (row + 1 < table.Rows && double.IsFinite(values[at]) && double.IsFinite(values[at + table.Columns])) Line(mesh.Positions[at], mesh.Positions[at + table.Columns]);
        }
        var wireMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(55, 51, 43))); group.Children.Add(new GeometryModel3D(wire, wireMaterial) { BackMaterial = wireMaterial });
        if (selected >= 0 && selected < values.Length && double.IsFinite(values[selected]))
        {
            var p = mesh.Positions[selected]; const double radius = .027;
            var marker = new MeshGeometry3D { Positions = [new(p.X, p.Y + radius, p.Z), new(p.X + radius, p.Y, p.Z), new(p.X, p.Y, p.Z + radius), new(p.X - radius, p.Y, p.Z), new(p.X, p.Y, p.Z - radius)], TriangleIndices = [0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1] };
            var highlight = new EmissiveMaterial(Brushes.OrangeRed); group.Children.Add(new GeometryModel3D(marker, highlight) { BackMaterial = highlight });
        }
        viewport.Children.Add(new ModelVisual3D { Content = group }); Camera();
    }
    private void Down(object sender, MouseButtonEventArgs e) { dragStart = lastDrag = e.GetPosition(this); CaptureMouse(); }
    private void Move(object sender, MouseEventArgs e)
    {
        if (dragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            if (!Surface && table is not null && values.Length > 1) { int i = curve.Nearest(e.GetPosition(curve).X); var axis = table.Columns == 1 ? table.Y : table.X; if (i >= 0) ToolTip = $"{axis.Name}: {axis.Values[i]:G8}\n{values[i]:G10} {table.Units}"; }
            return;
        }
        if (!Surface)
        { var p = e.GetPosition(this); if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) curve.Pan(p.X - lastDrag.X); lastDrag = p; return; }
        var point = e.GetPosition(this); RotateSurface(point - lastDrag); lastDrag = point;
    }
    private void RotateSurface(Vector movement)
    {
        // Orbit opposite the horizontal drag so the visible surface follows it.
        yaw -= movement.X * .5;
        elevation = Math.Clamp(elevation + movement.Y * .4, 10, 80);
        Camera();
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (dragStart is not Point start || table is null) { ReleaseMouseCapture(); return; }
        var point = e.GetPosition(this); bool click = (point - start).Length < 4; ReleaseMouseCapture();
        if (!click) return;
        if (!Surface)
        {
            int index = curve.Nearest(point.X); if (index >= 0) CellPicked?.Invoke(index);
        }
        else
        {
            int picked = -1;
            VisualTreeHelper.HitTest(viewport, null, result =>
            {
                if (result is RayMeshGeometry3DHitTestResult hit && ReferenceEquals(hit.ModelHit, surface))
                {
                    picked = hit.VertexWeight1 >= hit.VertexWeight2 && hit.VertexWeight1 >= hit.VertexWeight3 ? hit.VertexIndex1 : hit.VertexWeight2 >= hit.VertexWeight3 ? hit.VertexIndex2 : hit.VertexIndex3;
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(e.GetPosition(viewport)));
            if (picked >= 0) CellPicked?.Invoke(picked);
        }
    }
}

internal sealed class CurvePlot : FrameworkElement
{
    public TuningTable? Table { get; set; }
    public double[] Values { get; set; } = [];
    public int Selected { get; set; } = -1;
    private double start, end = 1;
    internal double VisibleFraction => end - start;
    internal double Start => start;
    internal double End => end;
    internal void RestoreRange(double left, double right) => Range(left, Math.Max(.02, right - left));
    public void ResetView() { start = 0; end = 1; InvalidateVisual(); }
    private void Range(double left, double width)
    { width = Math.Clamp(width, .02, 1); start = Math.Clamp(left, 0, 1 - width); end = start + width; InvalidateVisual(); }
    public void Zoom(double x, double factor)
    {
        double position = Math.Clamp((x - 65) / Math.Max(1, ActualWidth - 100), 0, 1), width = end - start;
        double next = Math.Clamp(width * factor, .02, 1); Range(start + position * width - position * next, next);
    }
    public void Pan(double pixels) => Range(start - pixels / Math.Max(1, ActualWidth - 100) * (end - start), end - start);
    private TuningAxis? Axis => Table is { Columns: 1, Rows: > 1 } ? Table.Y : Table?.X;
    private double Fraction(int i)
    {
        if (Table is null) return .5;
        double min = Axis!.Values.Min(), max = Axis!.Values.Max(); return max == min ? .5 : (Axis!.Values[i] - min) / (max - min);
    }
    public void EnsureVisible(int index)
    {
        if (Table is null || Table.Columns > 1 && Table.Rows > 1 || index < 0 || index >= Axis!.Values.Length) return;
        double point = Fraction(index), width = end - start;
        if (point < start || point > end) Range(point - width / 2, width);
    }
    public int Nearest(double x)
    {
        if (Table is null || Values.Length == 0) return -1;
        var visible = Enumerable.Range(0, Values.Length).Where(i => Fraction(i) >= start && Fraction(i) <= end).ToArray();
        return visible.Length == 0 ? -1 : visible.MinBy(i => Math.Abs(X(i) - x));
    }
    private double X(int i)
    {
        return 65 + (Fraction(i) - start) / (end - start) * Math.Max(1, ActualWidth - 100);
    }
    internal double PixelAt(int index) => X(index);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (Table is null || ActualWidth < 100 || ActualHeight < 60) return;
        if (Values.Length == 1)
        {
            var label = new FormattedText(Table.Display(Values[0]) + " " + Table.Units, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 20, UiTheme.Text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new(Math.Max(8, (ActualWidth - label.Width) / 2), Math.Max(8, (ActualHeight - label.Height) / 2))); return;
        }
        var visible = Enumerable.Range(0, Values.Length).Where(i => Fraction(i) >= start && Fraction(i) <= end).ToArray();
        var finite = visible.Select(i => Values[i]).Where(double.IsFinite).ToArray(); if (finite.Length == 0) return;
        double min = finite.Min(), max = finite.Max(), scale = Math.Max(1, Math.Max(Math.Abs(min), Math.Abs(max))), low = min / scale, high = max / scale;
        if (high - low < 1e-15) { low -= .5; high += .5; }
        double Y(double value) => 15 + (1 - (value / scale - low) / (high - low)) * Math.Max(1, ActualHeight - 53);
        void Text(string value, double x, double y) => dc.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, UiTheme.Text, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        var grid = new Pen(UiTheme.Line, 1);
        for (int i = 0; i < 4; i++) { double y = 15 + i * Math.Max(1, ActualHeight - 53) / 3; dc.DrawLine(grid, new(60, y), new(ActualWidth - 25, y)); Text(((high - i * (high - low) / 3) * scale).ToString("G5", CultureInfo.InvariantCulture), 0, y - 7); }
        var pen = new Pen(UiTheme.Amber, 2);
        dc.PushClip(new RectangleGeometry(new Rect(60, 8, Math.Max(1, ActualWidth - 85), Math.Max(1, ActualHeight - 43))));
        for (int i = 0; i < Values.Length; i++)
        {
            if (!double.IsFinite(Values[i])) continue; var point = new Point(X(i), Y(Values[i]));
            if (i > 0 && double.IsFinite(Values[i - 1])) dc.DrawLine(pen, new(X(i - 1), Y(Values[i - 1])), point);
            dc.DrawEllipse(i == Selected ? Brushes.OrangeRed : pen.Brush, null, point, i == Selected ? 5 : 2.5, i == Selected ? 5 : 2.5);
        }
        dc.Pop();
        double first = Axis!.Values.Min(), span = Axis!.Values.Max() - first;
        int ticks = ActualWidth < 260 ? 1 : ActualWidth < 360 ? 2 : 4;
        for (int tick = 0; tick <= ticks; tick++)
        {
            string label = (first + (start + (end - start) * tick / ticks) * span).ToString("G4", CultureInfo.InvariantCulture);
            Text(label, Math.Clamp(65 + tick * Math.Max(1, ActualWidth - 100) / ticks - label.Length * 2.8, 0, Math.Max(0, ActualWidth - label.Length * 6)), ActualHeight - 32);
        }
        void Caption(string label, Point position, double width)
        {
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, UiTheme.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                { MaxTextWidth = Math.Max(1, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(text, position);
        }
        Caption(Axis!.Name, new(60, ActualHeight - 17), ActualWidth - 65);
        Caption(Table.Units, new(2, ActualHeight - 17), 55);
    }
}
