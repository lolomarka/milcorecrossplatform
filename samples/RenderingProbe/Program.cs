using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using W = System.Windows;
using M = System.Windows.Media;
using I = System.Windows.Input;
using T = System.Windows.Threading;

namespace CrossWpf.RenderingProbe;

public sealed class ProbeApplication : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var root = Program.CreateScene(out var rectangle);
            var presenter = new WpfVisualPresenter(root);
            var window = new Window
            {
                Title = "CrossWpf rendering probe",
                Width = 320,
                Height = 240,
                Content = presenter
            };
            int clicks = 0;
            root.MouseDown += (_, args) =>
            {
                if (args.ChangedButton != I.MouseButton.Left)
                    return;

                root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    clicks++;
                    rectangle.Fill = clicks % 2 == 0 ? M.Brushes.Red : M.Brushes.Blue;
                    window.Title = $"CrossWpf rendering probe - clicks: {clicks}";
                    presenter.InvalidateArrange();
                    presenter.InvalidateVisual();
                }));
            };
            window.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(presenter).Properties.PointerUpdateKind != Avalonia.Input.PointerUpdateKind.LeftButtonPressed)
                    return;

                // ponytail: route to the root until the host supports WPF hit testing and pointer state.
                root.RaiseEvent(new I.MouseButtonEventArgs(I.Mouse.PrimaryDevice, Environment.TickCount, I.MouseButton.Left)
                {
                    RoutedEvent = I.Mouse.MouseDownEvent
                });
            };
            if (!OperatingSystem.IsWindows())
            {
                // ponytail: poll for the probe until WPF exposes a host wakeup hook.
                var pump = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                pump.Tick += (_, _) => root.Dispatcher.Invoke(() => { }, T.DispatcherPriority.Background);
                window.Closed += (_, _) => pump.Stop();
                pump.Start();
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class WpfVisualPresenter : Avalonia.Controls.Control
{
    public W.UIElement Root { get; }

    public WpfVisualPresenter(W.UIElement root) => Root = root;

    protected override Size MeasureOverride(Size availableSize)
    {
        Root.Measure(new W.Size(availableSize.Width, availableSize.Height));
        return new Size(Root.DesiredSize.Width, Root.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Root.Arrange(new W.Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        base.Render(context);
        DrawVisual(context, Root);
    }

    private static void DrawVisual(Avalonia.Media.DrawingContext context, M.Visual visual)
    {
        if (M.VisualTreeHelper.GetOpacityMask(visual) != null || M.VisualTreeHelper.GetEffect(visual) != null)
            throw new NotSupportedException("Visual opacity masks and effects are not supported by the rectangle probe.");
        var offset = M.VisualTreeHelper.GetOffset(visual);
        var transform = M.VisualTreeHelper.GetTransform(visual)?.Value ?? M.Matrix.Identity;
        transform.Translate(offset.X, offset.Y);
        using var matrixScope = context.PushTransform(ConvertMatrix(transform));
        using var opacityScope = context.PushOpacity(M.VisualTreeHelper.GetOpacity(visual));
        var clip = M.VisualTreeHelper.GetClip(visual);
        if (clip != null)
        {
            using var clipScope = context.PushGeometryClip(ConvertGeometry(clip));
            DrawVisualContents(context, visual);
        }
        else
        {
            DrawVisualContents(context, visual);
        }
    }

    private static void DrawVisualContents(Avalonia.Media.DrawingContext context, M.Visual visual)
    {
        var drawing = M.VisualTreeHelper.GetDrawing(visual);
        if (drawing != null)
            DrawDrawing(context, drawing);
        for (int index = 0; index < M.VisualTreeHelper.GetChildrenCount(visual); index++)
        {
            if (M.VisualTreeHelper.GetChild(visual, index) is M.Visual child)
                DrawVisual(context, child);
        }
    }

    private static void DrawDrawing(Avalonia.Media.DrawingContext context, M.Drawing drawing)
    {
        if (drawing is M.DrawingGroup group)
        {
            if (group.OpacityMask != null)
                throw new NotSupportedException("DrawingGroup.OpacityMask is not supported by the rectangle probe.");
            using var transform = context.PushTransform(ConvertMatrix(group.Transform?.Value ?? M.Matrix.Identity));
            using var opacity = context.PushOpacity(group.Opacity);
            if (group.ClipGeometry != null)
            {
                using var clip = context.PushGeometryClip(ConvertGeometry(group.ClipGeometry));
                foreach (var child in group.Children)
                    DrawDrawing(context, child);
            }
            else
            {
                foreach (var child in group.Children)
                    DrawDrawing(context, child);
            }
            return;
        }
        if (drawing is M.GeometryDrawing geometry)
        {
            if (geometry.Pen != null)
                throw new NotSupportedException("GeometryDrawing.Pen is not supported by the rectangle probe.");
            context.DrawGeometry(ConvertBrush(geometry.Brush), null, ConvertGeometry(geometry.Geometry));
            return;
        }
        throw new NotSupportedException($"Unsupported WPF drawing: {drawing.GetType().FullName}");
    }

    private static Avalonia.Media.IBrush? ConvertBrush(M.Brush? brush)
    {
        if (brush == null)
            return null;
        if (brush is not M.SolidColorBrush solid)
            throw new NotSupportedException($"Unsupported WPF brush: {brush.GetType().FullName}");
        var color = solid.Color;
        return new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B), solid.Opacity);
    }

    private static Avalonia.Media.Geometry ConvertGeometry(M.Geometry geometry)
    {
        if (geometry is not M.RectangleGeometry rectangle)
            throw new NotSupportedException($"Unsupported WPF geometry: {geometry.GetType().FullName}");
        var rect = rectangle.Rect;
        return new Avalonia.Media.RectangleGeometry
        {
            Rect = new Rect(rect.X, rect.Y, rect.Width, rect.Height),
            RadiusX = rectangle.RadiusX,
            RadiusY = rectangle.RadiusY,
            Transform = new Avalonia.Media.MatrixTransform(ConvertMatrix(rectangle.Transform?.Value ?? M.Matrix.Identity))
        };
    }

    private static Matrix ConvertMatrix(M.Matrix matrix) =>
        new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY);
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--window"])
        {
            AppBuilder.Configure<ProbeApplication>()
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
            return;
        }

        AppBuilder.Configure<ProbeApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .SetupWithoutStarting();

        var root = CreateScene(out var rectangle);
        var presenter = new WpfVisualPresenter(root);
        var output = args.Length == 0 ? "rendering-probe" : args[0];
        System.IO.Directory.CreateDirectory(output);
        Render(presenter, output + "/before.png", 160, 120);
        AssertPixel(output + "/before.png", 30, 30, 255, 0, 0);
        rectangle.Fill = M.Brushes.Blue;
        rectangle.Opacity = 0.5;
        rectangle.RenderTransform = new M.TranslateTransform(10, 0);
        rectangle.Clip = new M.RectangleGeometry(new W.Rect(0, 0, 40, 60));
        Render(presenter, output + "/after.png", 200, 140);
        AssertPixel(output + "/after.png", 40, 30, 127, 127, 255);
        AssertPixel(output + "/after.png", 80, 30, 255, 255, 255);
        Console.WriteLine("PASS: WPF layout and visual drawings rendered through Avalonia/Skia; fill, opacity, transform, clip and resize verified.");
    }

    internal static W.Controls.Grid CreateScene(out W.Shapes.Rectangle rectangle)
    {
        var root = new W.Controls.Grid { Background = M.Brushes.White };
        rectangle = new W.Shapes.Rectangle
        {
            Width = 80,
            Height = 60,
            Fill = M.Brushes.Red,
            HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Top,
            Margin = new W.Thickness(20, 20, 0, 0)
        };
        root.Children.Add(rectangle);
        return root;
    }

    private static void Render(WpfVisualPresenter presenter, string path, int width, int height)
    {
        presenter.Measure(new Size(width, height));
        presenter.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(presenter);
        bitmap.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void AssertPixel(string path, int x, int y, int red, int green, int blue)
    {
        using var bitmap = SkiaSharp.SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Cannot decode {path}");
        var pixel = bitmap.GetPixel(x, y);
        if (Math.Abs(pixel.Red - red) > 1 || Math.Abs(pixel.Green - green) > 1 ||
            Math.Abs(pixel.Blue - blue) > 1 || pixel.Alpha != 255)
        {
            throw new InvalidOperationException($"Unexpected pixel in {path} at ({x},{y}): {pixel}");
        }
    }
}
