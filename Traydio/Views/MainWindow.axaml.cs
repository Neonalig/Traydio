using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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
            var handlers = this.GetVisualDescendants()
                .OfType<IMainWindowClosingHandler>()
                .ToArray();

            foreach (var handler in handlers)
            {
                if (!await handler.CanCloseMainWindowAsync().ConfigureAwait(true))
                {
                    return;
                }
            }

            _allowClose = true;
            Close();
        }
        finally
        {
            Interlocked.Exchange(ref _isCloseCheckInProgress, 0);
        }
    }
}
