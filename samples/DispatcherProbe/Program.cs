using System.Windows.Threading;

if (args is ["--layout"])
{
    var grid = new System.Windows.Controls.Grid();
    var rectangle = new System.Windows.Shapes.Rectangle { Width = 40, Height = 20 };
    grid.Children.Add(rectangle);
    var size = new System.Windows.Size(100, 80);
    grid.Measure(size);
    grid.Arrange(new System.Windows.Rect(size));
    if (rectangle.RenderSize != new System.Windows.Size(40, 20))
        throw new InvalidOperationException($"Unexpected rectangle size: {rectangle.RenderSize}");
    Console.WriteLine("PASS: WPF Grid measured and arranged its Rectangle.");
    return;
}

var dispatcher = Dispatcher.CurrentDispatcher;
bool operationRan = false;
bool timerRan = false;
var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
timer.Tick += (_, _) =>
{
    timer.Stop();
    timerRan = true;
    dispatcher.InvokeShutdown();
};

var producer = new Thread(() => dispatcher.BeginInvoke(() =>
{
    operationRan = dispatcher.CheckAccess();
    timer.Start();
})) { IsBackground = true };
producer.Start();
Dispatcher.Run();
producer.Join();

if (!operationRan || !timerRan || !dispatcher.HasShutdownFinished)
    throw new InvalidOperationException("Dispatcher did not complete the operation, timer and shutdown.");

Console.WriteLine("PASS: dispatcher processed cross-thread work, timer and shutdown.");

