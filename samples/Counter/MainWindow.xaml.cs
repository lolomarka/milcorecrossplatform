using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CrossWpf.Sample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new CounterModel();
    }
}

public sealed class CounterModel : INotifyPropertyChanged, ICommand
{
    public int Count { get; private set; }
    public ICommand Increment => this;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        Count++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
    }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application();
        var window = new MainWindow();
        if (Array.IndexOf(args, "--smoke") >= 0)
        {
            window.Show();
            window.Dispatcher.Invoke(() =>
            {
                window.IncrementButton.Command.Execute(null);
            }, DispatcherPriority.Normal);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (window.CounterText.Text != "1")
                throw new InvalidOperationException("Binding did not update after command execution.");
            window.Close();
            Console.WriteLine("PASS: compiled XAML, window creation, command, binding, dispatcher, close.");
            return 0;
        }
        return app.Run(window);
    }
}
