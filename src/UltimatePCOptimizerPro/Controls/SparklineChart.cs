using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UltimatePCOptimizerPro.Controls;

/// <summary>Lightweight line/area chart rendered directly in OnRender (no charting dependency, no per-point visuals).</summary>
public sealed class SparklineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double>), typeof(SparklineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SparklineChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SparklineChart),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(SparklineChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SparklineChart),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    /// <summary>NaN = auto scale to the data.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    private static readonly Pen GridPen = CreateGridPen();

    private static Pen CreateGridPen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromArgb(40, 154, 168, 187)), 1) { DashStyle = DashStyles.Dot };
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var values = Values;
        if (values is null || values.Count < 2) return;

        // Downsample to at most one point per pixel (keeps frame-time charts with 100k frames fast) using max per bucket.
        int n = values.Count;
        int buckets = Math.Min(n, Math.Max(2, (int)w));
        var pts = new double[buckets];
        for (int b = 0; b < buckets; b++)
        {
            int start = (int)((long)b * n / buckets), end = Math.Max(start + 1, (int)((long)(b + 1) * n / buckets));
            double m = double.MinValue;
            for (int i = start; i < end && i < n; i++) if (values[i] > m) m = values[i];
            pts[b] = double.IsFinite(m) ? m : 0;
        }
        double max = double.IsNaN(Maximum) ? Math.Max(1e-6, pts.Max() * 1.15) : Maximum;

        if (ShowGrid)
        {
            for (int g = 1; g < 4; g++) dc.DrawLine(GridPen, new Point(0, h * g / 4), new Point(w, h * g / 4));
            var label = new FormattedText($"{max:0.#}{Unit}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10,
                new SolidColorBrush(Color.FromArgb(150, 154, 168, 187)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(Math.Max(0, w - label.Width - 2), 0));
        }

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            for (int i = 0; i < buckets; i++)
            {
                double x = buckets == 1 ? 0 : i * (w - 1) / (buckets - 1);
                double y = h - 1 - Math.Clamp(pts[i] / max, 0, 1) * (h - 2);
                var p = new Point(x, y);
                if (i == 0)
                {
                    lc.BeginFigure(p, false, false);
                    ac.BeginFigure(new Point(x, h), true, true);
                    ac.LineTo(p, false, false);
                }
                else
                {
                    lc.LineTo(p, true, true);
                    ac.LineTo(p, false, false);
                }
            }
            ac.LineTo(new Point(w - 1, h), false, false);
        }
        line.Freeze();
        area.Freeze();

        var color = (Stroke as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        var fill = new LinearGradientBrush(Color.FromArgb(90, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}
