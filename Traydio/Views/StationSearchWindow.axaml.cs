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
        if (topLevel is not Window owner)
        {
            return;
        }

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Grid
            {
                Margin = new Avalonia.Thickness(12),
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children =
                        {
                            new Button
                            {
                                Content = "OK",
                                MinWidth = 88,
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                [Grid.RowProperty] = 1,
                            },
                        },
                        [Grid.RowProperty] = 1,
                    },
                },
            },
        };

        if (dialog.Content is Grid grid && grid.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<Button>().FirstOrDefault() is { } okButton)
        {
            okButton.Click += (_, _) => dialog.Close();
        }

        await dialog.ShowDialog(owner);
    }

}

