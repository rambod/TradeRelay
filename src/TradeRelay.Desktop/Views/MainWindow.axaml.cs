using Avalonia.Controls;
using TradeRelay.Desktop.ViewModels;

namespace TradeRelay.Desktop.Views;

/// <summary>
/// Hosts the initial TradeRelay desktop shell.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class for the XAML loader and designer.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="viewModel">The view model resolved by dependency injection.</param>
    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
    }
}
