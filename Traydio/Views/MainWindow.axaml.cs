using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Traydio.Common;
using Traydio.Services;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(MainWindowViewModel))]
public partial class MainWindow : Window
{
    private int _isCloseCheckInProgress;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnMainWindowClosing;
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (Interlocked.Exchange(ref _isCloseCheckInProgress, 1) == 1)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        ConfirmCloseAsync().ForgetWithErrorHandling("Main window close confirmation", showDialog: true);
    }

    private async Task ConfirmCloseAsync()
    {
        try
        {
            if (!await CanLeaveCurrentPageAsync().ConfigureAwait(true))
            {
                return;
            }

            _allowClose = true;
            Close();
        }
        finally
        {
            Interlocked.Exchange(ref _isCloseCheckInProgress, 0);
        }
    }

    private void OnStationsTabClick(object? sender, RoutedEventArgs e)
    {
        ExecuteTabNavigationAsync(static vm => vm.OpenStationsCommand.Execute(null))
            .ForgetWithErrorHandling("Navigate to stations tab", showDialog: true);
    }

    private void OnSearchTabClick(object? sender, RoutedEventArgs e)
    {
        ExecuteTabNavigationAsync(static vm => vm.OpenSearchCommand.Execute(null))
            .ForgetWithErrorHandling("Navigate to search tab", showDialog: true);
    }

    private void OnPluginsTabClick(object? sender, RoutedEventArgs e)
    {
        ExecuteTabNavigationAsync(static vm => vm.OpenPluginsCommand.Execute(null))
            .ForgetWithErrorHandling("Navigate to plugins tab", showDialog: true);
    }

    private void OnSettingsTabClick(object? sender, RoutedEventArgs e)
    {
        ExecuteTabNavigationAsync(static vm => vm.OpenSettingsCommand.Execute(null))
            .ForgetWithErrorHandling("Navigate to settings tab", showDialog: true);
    }

    private async Task ExecuteTabNavigationAsync(Action<MainWindowViewModel> navigateAction)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!await CanLeaveCurrentPageAsync().ConfigureAwait(true))
        {
            return;
        }

        navigateAction(viewModel);
    }

    private async Task<bool> CanLeaveCurrentPageAsync()
    {
        var handlers = this.GetVisualDescendants()
            .OfType<IMainWindowClosingHandler>()
            .ToArray();

        foreach (var handler in handlers)
        {
            if (!await handler.CanCloseMainWindowAsync().ConfigureAwait(true))
            {
                return false;
            }
        }

        return true;
    }
}
