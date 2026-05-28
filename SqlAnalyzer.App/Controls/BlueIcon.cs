using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SqlAnalyzer.App.Controls;

public sealed class BlueIcon : Control
{
    private static readonly ConcurrentDictionary<string, StreamGeometry?> SvgLogoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Bitmap?> BitmapLogoCache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<BlueIcon, string>(nameof(Kind), "Info");

    public static readonly StyledProperty<IBrush> AccentBrushProperty =
        AvaloniaProperty.Register<BlueIcon, IBrush>(nameof(AccentBrush), new SolidColorBrush(Color.Parse("#2F80D8")));

    public static readonly StyledProperty<double> StrokeScaleProperty =
        AvaloniaProperty.Register<BlueIcon, double>(nameof(StrokeScale), 1.0);

    static BlueIcon()
    {
        AffectsRender<BlueIcon>(KindProperty, AccentBrushProperty, StrokeScaleProperty);
    }

    public BlueIcon()
    {
        Width = 18;
        Height = 18;
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public double StrokeScale
    {
        get => GetValue(StrokeScaleProperty);
        set => SetValue(StrokeScaleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double size = Math.Min(bounds.Width, bounds.Height);
        double scale = size / 24.0;
        double offsetX = (bounds.Width - size) / 2.0;
        double offsetY = (bounds.Height - size) / 2.0;
        Pen pen = new(AccentBrush, Math.Max(1.25, 1.7 * scale * StrokeScale));
        IBrush softBrush = BuildSoftBrush(AccentBrush);

        Point P(double x, double y) => new(offsetX + x * scale, offsetY + y * scale);
        Rect R(double x, double y, double width, double height) => new(offsetX + x * scale, offsetY + y * scale, width * scale, height * scale);
        void Line(double x1, double y1, double x2, double y2) => context.DrawLine(pen, P(x1, y1), P(x2, y2));
        void Rect(double x, double y, double width, double height, double radius = 2.0, bool fill = false) =>
            context.DrawRectangle(fill ? softBrush : null, pen, R(x, y, width, height), radius * scale, radius * scale);
        void Ellipse(double x, double y, double width, double height, bool fill = false) =>
            context.DrawEllipse(fill ? softBrush : null, pen, R(x, y, width, height));

        switch ((Kind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "oraclelogo":
                if (TryDrawSvgLogo(context, "oracle.svg", R(2.5, 2.5, 19, 19), AccentBrush))
                {
                    break;
                }
                goto case "connection";
            case "postgresqllogo":
                if (TryDrawSvgLogo(context, "postgresql.svg", R(2.5, 2.5, 19, 19), AccentBrush))
                {
                    break;
                }
                goto case "connection";
            case "mysqllogo":
                if (TryDrawSvgLogo(context, "mysql.svg", R(2.2, 2.2, 19.6, 19.6), AccentBrush))
                {
                    break;
                }
                goto case "connection";
            case "sqlserverlogo":
                if (TryDrawSvgLogo(context, "microsoftsqlserver.svg", R(2.2, 2.2, 19.6, 19.6), AccentBrush))
                {
                    break;
                }
                goto case "connection";
            case "mongodblogo":
                if (TryDrawSvgLogo(context, "mongodb.svg", R(3.2, 2.0, 17.6, 20), AccentBrush))
                {
                    break;
                }
                goto case "connection";
            case "kingbaselogo":
                if (TryDrawBitmapLogo(context, "kingbase-logo-icon.png", R(3, 3, 18, 18)))
                {
                    break;
                }
                goto case "connection";
            case "damenglogo":
                if (TryDrawBitmapLogo(context, "dameng-logo-icon.png", R(2.5, 2.5, 19, 19)))
                {
                    break;
                }
                goto case "connection";
            case "database":
            case "connection":
                Ellipse(6, 4, 12, 4, true);
                Line(6, 6, 6, 17);
                Line(18, 6, 18, 17);
                Ellipse(6, 15, 12, 5);
                Line(7, 10, 17, 10);
                break;
            case "query":
            case "sql":
                Rect(6, 4, 12, 16, 2.5, true);
                Line(9, 8, 15, 8);
                Line(9, 12, 16, 12);
                Line(9, 16, 13, 16);
                break;
            case "comment":
                Rect(5, 5, 14, 11, 3, true);
                Line(9, 16, 7, 20);
                Line(9, 16, 13, 16);
                Line(8, 9, 16, 9);
                Line(8, 12, 14, 12);
                break;
            case "diagram":
                Rect(4, 5, 6, 5, 1.5, true);
                Rect(14, 5, 6, 5, 1.5, true);
                Rect(9, 15, 6, 5, 1.5, true);
                Line(10, 7.5, 14, 7.5);
                Line(7, 10, 11, 15);
                Line(17, 10, 13, 15);
                break;
            case "run":
                Ellipse(4, 4, 16, 16, true);
                Line(10, 8, 16, 12);
                Line(16, 12, 10, 16);
                Line(10, 16, 10, 8);
                break;
            case "plan":
                Rect(5, 4, 14, 16, 2, true);
                Line(8, 8, 10, 8);
                Line(12, 8, 16, 8);
                Line(8, 12, 10, 12);
                Line(12, 12, 16, 12);
                Line(8, 16, 10, 16);
                Line(12, 16, 16, 16);
                break;
            case "format":
                Line(6, 7, 17, 7);
                Line(6, 11, 14, 11);
                Line(6, 15, 12, 15);
                Line(16, 13, 19, 16);
                Line(19, 13, 16, 16);
                Line(17.5, 11.5, 17.5, 17.5);
                break;
            case "search":
                Ellipse(5, 5, 10, 10);
                Line(13, 13, 19, 19);
                break;
            case "replace":
                Line(6, 8, 16, 8);
                Line(16, 8, 13, 5);
                Line(16, 8, 13, 11);
                Line(18, 16, 8, 16);
                Line(8, 16, 11, 13);
                Line(8, 16, 11, 19);
                break;
            case "history":
                Ellipse(4, 4, 16, 16, true);
                Line(12, 12, 12, 7);
                Line(12, 12, 16, 14);
                Line(5, 8, 7, 6);
                Line(5, 8, 8, 8);
                break;
            case "folder":
                Rect(4, 7, 16, 11, 2, true);
                Line(5, 7, 9, 4);
                Line(9, 4, 13, 7);
                break;
            case "schema":
                Ellipse(6, 4, 12, 4, true);
                Line(6, 6, 6, 18);
                Line(18, 6, 18, 18);
                Ellipse(6, 16, 12, 4);
                Line(9, 11, 15, 11);
                break;
            case "table":
                Rect(4, 5, 16, 14, 2, true);
                Line(4, 10, 20, 10);
                Line(4, 14.5, 20, 14.5);
                Line(10, 5, 10, 19);
                Line(15, 5, 15, 19);
                break;
            case "view":
                Ellipse(4, 8, 16, 8, true);
                Ellipse(10, 10, 4, 4);
                break;
            case "materializedview":
                Rect(5, 6, 14, 12, 2, true);
                Line(8, 10, 16, 10);
                Line(8, 14, 13, 14);
                Line(15, 14, 18, 17);
                break;
            case "sequence":
                Line(7, 6, 17, 6);
                Line(7, 12, 17, 12);
                Line(7, 18, 17, 18);
                Line(15, 4, 17, 6);
                Line(17, 6, 15, 8);
                Line(15, 10, 17, 12);
                Line(17, 12, 15, 14);
                Line(15, 16, 17, 18);
                Line(17, 18, 15, 20);
                break;
            case "trigger":
                Line(12, 4, 7, 13);
                Line(12, 4, 17, 13);
                Line(7, 13, 12, 11);
                Line(17, 13, 12, 11);
                Line(12, 11, 10, 20);
                break;
            case "synonym":
                Ellipse(5, 6, 8, 8);
                Ellipse(11, 10, 8, 8);
                Line(12, 10, 14, 10);
                break;
            case "package":
                Rect(5, 6, 14, 12, 2, true);
                Line(5, 9, 12, 12);
                Line(19, 9, 12, 12);
                Line(12, 12, 12, 18);
                break;
            case "function":
                Line(7, 18, 10, 6);
                Line(10, 6, 15, 6);
                Line(8, 12, 14, 12);
                Line(15, 10, 18, 14);
                Line(18, 10, 15, 14);
                break;
            case "procedure":
                Rect(5, 5, 14, 14, 2, true);
                Line(9, 9, 15, 12);
                Line(15, 12, 9, 15);
                break;
            case "column":
                Rect(6, 5, 12, 14, 2, true);
                Line(9, 5, 9, 19);
                Line(15, 5, 15, 19);
                break;
            case "import":
                Rect(5, 6, 14, 13, 2, true);
                Line(12, 3, 12, 13);
                Line(8, 9, 12, 13);
                Line(16, 9, 12, 13);
                break;
            case "export":
                Rect(5, 7, 14, 12, 2, true);
                Line(12, 15, 12, 4);
                Line(8, 8, 12, 4);
                Line(16, 8, 12, 4);
                break;
            case "save":
                Rect(5, 4, 14, 16, 2, true);
                Line(8, 4, 8, 9);
                Line(16, 4, 16, 9);
                Line(8, 16, 16, 16);
                break;
            case "edit":
                Rect(5, 5, 12, 14, 2, true);
                Line(9, 15, 16, 8);
                Line(15, 7, 17, 9);
                Line(8, 16, 11, 15);
                break;
            case "add":
                Ellipse(5, 5, 14, 14, true);
                Line(12, 8, 12, 16);
                Line(8, 12, 16, 12);
                break;
            case "remove":
                Ellipse(5, 5, 14, 14, true);
                Line(8, 12, 16, 12);
                break;
            case "key":
                Ellipse(5, 7, 7, 7);
                Line(12, 10.5, 19, 10.5);
                Line(16, 10.5, 16, 14);
                Line(19, 10.5, 19, 13);
                break;
            case "copy":
                Rect(8, 5, 10, 12, 2);
                Rect(5, 8, 10, 12, 2, true);
                break;
            case "close":
                Line(8, 8, 16, 16);
                Line(16, 8, 8, 16);
                break;
            case "check":
                Ellipse(5, 5, 14, 14, true);
                Line(8, 12, 11, 15);
                Line(11, 15, 17, 9);
                break;
            case "error":
                Ellipse(5, 5, 14, 14, true);
                Line(9, 9, 15, 15);
                Line(15, 9, 9, 15);
                break;
            case "empty":
                Rect(6, 6, 12, 12, 3, true);
                Line(9, 12, 15, 12);
                break;
            default:
                Ellipse(5, 5, 14, 14, true);
                Line(12, 10, 12, 16);
                Ellipse(11.2, 7, 1.6, 1.6, true);
                break;
        }
    }

    private static bool TryDrawSvgLogo(DrawingContext context, string fileName, Rect target, IBrush brush)
    {
        StreamGeometry? geometry = SvgLogoCache.GetOrAdd(fileName, LoadSvgLogoGeometry);
        if (geometry == null)
        {
            return false;
        }

        double scaleX = target.Width / 24.0;
        double scaleY = target.Height / 24.0;
        using (context.PushTransform(Matrix.CreateScale(scaleX, scaleY) * Matrix.CreateTranslation(target.X, target.Y)))
        {
            context.DrawGeometry(brush, null, geometry);
        }

        return true;
    }

    private static StreamGeometry? LoadSvgLogoGeometry(string fileName)
    {
        try
        {
            Uri uri = new($"avares://QueryPaw/Assets/DatabaseLogos/{fileName}");
            using Stream stream = AssetLoader.Open(uri);
            using StreamReader reader = new(stream);
            string svg = reader.ReadToEnd();
            Match match = Regex.Match(svg, "<path[^>]*\\sd=\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return null;
            }

            return StreamGeometry.Parse(match.Groups[1].Value);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryDrawBitmapLogo(DrawingContext context, string fileName, Rect target)
    {
        Bitmap? bitmap = BitmapLogoCache.GetOrAdd(fileName, LoadBitmapLogo);
        if (bitmap == null)
        {
            return false;
        }

        context.DrawImage(bitmap, target);
        return true;
    }

    private static Bitmap? LoadBitmapLogo(string fileName)
    {
        try
        {
            Uri uri = new($"avares://QueryPaw/Assets/DatabaseLogos/{fileName}");
            Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static IBrush BuildSoftBrush(IBrush brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            return new SolidColorBrush(solid.Color, 0.14);
        }

        return new SolidColorBrush(Color.Parse("#DBEAFE"), 0.78);
    }
}
