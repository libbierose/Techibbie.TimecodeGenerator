using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Techibbie.TimecodeGenerator.App.Views;

/// <summary>
/// Small, flat, hand-drawn vector glyphs for the toolbar — geometric rather than a literal
/// Font Awesome transliteration, so the app doesn't need an icon-font runtime dependency
/// for the handful of icons it uses.
/// </summary>
public enum IconKind
{
    Play,
    Pause,
    Stop,
    SkipBack,
    SkipForward,
    Record,
    Save,
    Settings,
    Info,
    Clock,
}

public static class Icons
{
    public static Control Create(IconKind kind, IBrush brush, double size = 20)
    {
        var canvas = new Canvas { Width = 20, Height = 20 };

        switch (kind)
        {
            case IconKind.Play:
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 5,3 L 16,10 L 5,17 Z"),
                    Fill = brush,
                });
                break;

            case IconKind.Pause:
                canvas.Children.Add(Rect(5, 3, 3, 14, brush));
                canvas.Children.Add(Rect(12, 3, 3, 14, brush));
                break;

            case IconKind.Stop:
                canvas.Children.Add(new Rectangle
                {
                    Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Fill = brush,
                    [Canvas.LeftProperty] = 4.0, [Canvas.TopProperty] = 4.0,
                });
                break;

            case IconKind.SkipBack:
                canvas.Children.Add(new Path { Data = Geometry.Parse("M 10,3 L 10,17 L 2,10 Z"), Fill = brush });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M 18,3 L 18,17 L 10,10 Z"), Fill = brush });
                break;

            case IconKind.SkipForward:
                canvas.Children.Add(new Path { Data = Geometry.Parse("M 2,3 L 2,17 L 10,10 Z"), Fill = brush });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M 10,3 L 10,17 L 18,10 Z"), Fill = brush });
                break;

            case IconKind.Record:
                canvas.Children.Add(new Ellipse
                {
                    Width = 12, Height = 12, Fill = brush,
                    [Canvas.LeftProperty] = 4.0, [Canvas.TopProperty] = 4.0,
                });
                break;

            case IconKind.Save:
                canvas.Children.Add(Line(10, 3, 10, 12, brush));
                canvas.Children.Add(Line(6, 9, 10, 13, brush));
                canvas.Children.Add(Line(14, 9, 10, 13, brush));
                canvas.Children.Add(Line(4, 16, 4, 17, brush));
                canvas.Children.Add(Line(4, 17, 16, 17, brush));
                canvas.Children.Add(Line(16, 17, 16, 16, brush));
                break;

            case IconKind.Settings:
                canvas.Children.Add(Line(3, 5, 17, 5, brush));
                canvas.Children.Add(Dot(7, 5, brush));
                canvas.Children.Add(Line(3, 10, 17, 10, brush));
                canvas.Children.Add(Dot(13, 10, brush));
                canvas.Children.Add(Line(3, 15, 17, 15, brush));
                canvas.Children.Add(Dot(9, 15, brush));
                break;

            case IconKind.Info:
                canvas.Children.Add(new Ellipse
                {
                    Width = 15, Height = 15, Stroke = brush, StrokeThickness = 1.4,
                    [Canvas.LeftProperty] = 2.5, [Canvas.TopProperty] = 2.5,
                });
                canvas.Children.Add(Dot(10, 6.2, brush, 1.1));
                canvas.Children.Add(new Rectangle
                {
                    Width = 1.6, Height = 5.5, Fill = brush, RadiusX = 0.8, RadiusY = 0.8,
                    [Canvas.LeftProperty] = 9.2, [Canvas.TopProperty] = 8.8,
                });
                break;

            case IconKind.Clock:
                canvas.Children.Add(new Ellipse
                {
                    Width = 15, Height = 15, Stroke = brush, StrokeThickness = 1.4,
                    [Canvas.LeftProperty] = 2.5, [Canvas.TopProperty] = 2.5,
                });
                canvas.Children.Add(Line(10, 10, 10, 5.5, brush));
                canvas.Children.Add(Line(10, 10, 13, 12, brush));
                break;
        }

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static Rectangle Rect(double x, double y, double w, double h, IBrush brush) => new()
    {
        Width = w, Height = h, Fill = brush,
        [Canvas.LeftProperty] = x, [Canvas.TopProperty] = y,
    };

    private static Line Line(double x1, double y1, double x2, double y2, IBrush brush) => new()
    {
        StartPoint = new Point(x1, y1),
        EndPoint = new Point(x2, y2),
        Stroke = brush,
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
    };

    private static Ellipse Dot(double cx, double cy, IBrush brush, double radius = 2) => new()
    {
        Width = radius * 2, Height = radius * 2, Fill = brush,
        [Canvas.LeftProperty] = cx - radius, [Canvas.TopProperty] = cy - radius,
    };
}
