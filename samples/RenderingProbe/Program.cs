using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using W = System.Windows;
using M = System.Windows.Media;
using T = System.Windows.Threading;
using WC = System.Windows.Controls;

namespace CrossWpf.RenderingProbe;

internal sealed class ProbeButton : WC.Button
{
    internal void Activate() => OnClick();
}

public sealed class ProbeApplication : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var root = new WC.Grid { Background = M.Brushes.White };
            var face = new W.FrameworkElementFactory(typeof(W.Shapes.Rectangle));
            face.SetValue(W.Shapes.Shape.FillProperty, new W.TemplateBindingExtension(WC.Control.BackgroundProperty));
            var button = new ProbeButton
            {
                Width = 80,
                Height = 60,
                Background = M.Brushes.Red,
                HorizontalAlignment = W.HorizontalAlignment.Left,
                VerticalAlignment = W.VerticalAlignment.Top,
                Margin = new W.Thickness(20, 20, 0, 0),
                OverridesDefaultStyle = true,
                Template = new WC.ControlTemplate(typeof(WC.Button)) { VisualTree = face }
            };
            root.Children.Add(button);
            var presenter = new WpfVisualPresenter(root);
            var window = new Window
            {
                Title = "CrossWpf rendering probe",
                Width = 320,
                Height = 240,
                Content = presenter
            };
#if FORK_LINUX
            var dispatcher = root.Dispatcher;
            var timer = new Avalonia.Threading.DispatcherTimer();
            bool closed = false;
            int posted = 0;

            void ProcessWork()
            {
                timer.Stop();
                if (closed)
                    return;

                int delay = dispatcher.ProcessManagedHostWork();
                if (!closed && delay >= 0)
                {
                    timer.Interval = TimeSpan.FromMilliseconds(delay);
                    timer.Start();
                }
            }

            timer.Tick += (_, _) => ProcessWork();
            dispatcher.SetManagedHostWakeup(() =>
            {
                if (System.Threading.Interlocked.Exchange(ref posted, 1) != 0)
                    return;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    System.Threading.Interlocked.Exchange(ref posted, 0);
                    ProcessWork();
                }, Avalonia.Threading.DispatcherPriority.Background);
            });
            window.Closed += (_, _) =>
            {
                closed = true;
                dispatcher.SetManagedHostWakeup(null);
                timer.Stop();
            };
#endif
            int clicks = 0;
            button.Click += (_, _) =>
            {
                clicks++;
                button.Background = clicks % 2 == 0 ? M.Brushes.Red : M.Brushes.Blue;
                window.Title = $"CrossWpf rendering probe - clicks: {clicks}";
                presenter.InvalidateArrange();
                presenter.InvalidateVisual();
            };

            bool IsOverButton(Avalonia.Input.PointerEventArgs args)
            {
                var point = args.GetPosition(presenter);
                var offset = M.VisualTreeHelper.GetOffset(button);
                var bounds = new W.Rect(new W.Point(offset.X, offset.Y), button.RenderSize);
                return button.IsEnabled && button.IsHitTestVisible &&
                    bounds.Contains(new W.Point(point.X, point.Y));
            }

            // ponytail: Avalonia owns capture for this single button until WPF has a platform mouse device.
            Avalonia.Input.IPointer? pressed = null;
            window.PointerPressed += (_, args) =>
            {
                if (pressed != null ||
                    args.GetCurrentPoint(presenter).Properties.PointerUpdateKind != Avalonia.Input.PointerUpdateKind.LeftButtonPressed ||
                    !IsOverButton(args))
                {
                    return;
                }

                args.Pointer.Capture(window);
                if (args.Pointer.Captured == window)
                    pressed = args.Pointer;
            };
            window.PointerCaptureLost += (_, args) =>
            {
                if (pressed == args.Pointer)
                    pressed = null;
            };
            window.PointerReleased += (_, args) =>
            {
                if (pressed != args.Pointer || args.InitialPressMouseButton != Avalonia.Input.MouseButton.Left)
                    return;

                pressed = null;
                args.Pointer.Capture(null);
                if (IsOverButton(args))
                    button.Dispatcher.BeginInvoke(new Action(button.Activate));
            };
            desktop.MainWindow = window;
#if FORK_LINUX
            if (desktop.Args is ["--window-dispatcher-smoke"])
            {
                window.Opened += (_, _) =>
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            dispatcher.VerifyAccess();
                            var testTimer = new T.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                            testTimer.Tick += (_, _) =>
                            {
                                testTimer.Stop();
                                Console.WriteLine("PASS: cross-thread SystemIdle operation and WPF timer on the Avalonia UI thread.");
                                window.Close();
                            };
                            testTimer.Start();
                        }), T.DispatcherPriority.SystemIdle));
                };
            }
#endif
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
        if (args is ["--window"] or ["--window-dispatcher-smoke"])
        {
            AppBuilder.Configure<ProbeApplication>()
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
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
