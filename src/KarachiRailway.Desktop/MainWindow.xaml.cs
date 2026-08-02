using System.Windows;

namespace KarachiRailway.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// The DataContext is set in XAML to MainViewModel (MVVM pattern).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;

        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}
