using System;
using System.Linq;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Traydio.Common;
using Traydio.Services;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(StationSearchWindowViewModel))]
public partial class StationSearchPage : UserControl
{
    private StationSearchWindowViewModel? _observedViewModel;
    private string _lastStatusShown = string.Empty;

    public StationSearchPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public StationSearchPage(StationSearchWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        AttachToViewModel(viewModel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is StationSearchWindowViewModel viewModel)
        {
            AttachToViewModel(viewModel);
        }
        else
        {
            DetachFromViewModel();
        }
    }

    private void AttachToViewModel(StationSearchWindowViewModel viewModel)
    {
        if (ReferenceEquals(_observedViewModel, viewModel))
        {
            return;
        }

        DetachFromViewModel();
        _observedViewModel = viewModel;
        _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachFromViewModel()
    {
        if (_observedViewModel is null)
        {
            return;
        }

        _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnViewModelPropertyChangedAsync(sender, e)
            .ForgetWithErrorHandling("Station search status dialog", showDialog: true);
    }

    private async System.Threading.Tasks.Task OnViewModelPropertyChangedAsync(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(StationSearchWindowViewModel.Status), StringComparison.Ordinal))
        {
            return;
        }

        if (sender is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        var status = viewModel.Status.Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        if (string.Equals(status, "Searching...", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(status, _lastStatusShown, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatusShown = status;
        await ShowInfoDialogAsync("Station search", status);
    }

    private async System.Threading.Tasks.Task ShowInfoDialogAsync(string title, string message)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        await MessageBox.ShowDialog(topLevel as Window, title, message);
    }

}

