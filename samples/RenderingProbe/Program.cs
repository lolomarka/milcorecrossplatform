using System;
using Avalonia;
using Avalonia.Headless;
using W = System.Windows;
using M = System.Windows.Media;

namespace CrossWpf.RenderingProbe;

public sealed class ProbeApplication : Application { }

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
        Root.UpdateLayout();
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
        AppBuilder.Configure<ProbeApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .SetupWithoutStarting();

        var root = new W.Controls.Grid { Background = M.Brushes.White };
        var rectangle = new W.Shapes.Rectangle
        {
            Width = 80,
            Height = 60,
            Fill = M.Brushes.Red,
            HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Top,
            Margin = new W.Thickness(20, 20, 0, 0)
        };
        root.Children.Add(rectangle);
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

    private static void Render(WpfVisualPresenter presenter, string path, int width, int height)
    {
        presenter.Measure(new Size(width, height));
        presenter.Arrange(new Rect(0, 0, width, height));
        presenter.Root.UpdateLayout();
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(presenter);
        bitmap.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void AssertPixel(string path, int x, int y, int red, int green, int blue)
    {
        using var stream = System.IO.File.OpenRead(path);
        var decoder = new M.Imaging.PngBitmapDecoder(stream, M.Imaging.BitmapCreateOptions.None, M.Imaging.BitmapCacheOption.OnLoad);
        var bitmap = new M.Imaging.FormatConvertedBitmap(decoder.Frames[0], M.PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        bitmap.CopyPixels(new W.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        if (Math.Abs(pixel[2] - red) > 1 || Math.Abs(pixel[1] - green) > 1 || Math.Abs(pixel[0] - blue) > 1 || pixel[3] != 255)
            throw new InvalidOperationException($"Unexpected pixel in {path} at ({x},{y}): {pixel[2]},{pixel[1]},{pixel[0]},{pixel[3]}");
    }
}


