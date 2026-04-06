using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Traydio.Common;
using Traydio.ViewModels;
using System.Threading.Tasks;
using Traydio.Services;

namespace Traydio.Views;

[ViewFor(typeof(StationSearchWindowViewModel))]
public partial class StationSearchPage : UserControl
{
    public StationSearchPage()
    {
        AvaloniaXamlLoader.Load(this);

        var resultsList = this.FindControl<ListBox>("ResultsList");
        if (resultsList is not null)
        {
            resultsList.DoubleTapped += OnResultsListDoubleTapped;
        }
    }

    public StationSearchPage(StationSearchWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnResultsListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not ListBox listBox)
        {
            return;
        }

        if (listBox.SelectedItem is not StationSearchWindowViewModel.SearchResultItem station)
        {
            return;
        }

        if (viewModel.PlayOrPauseResultCommand.CanExecute(station))
        {
            viewModel.PlayOrPauseResultCommand.Execute(station);
        }
    }

    private void OnAddResultClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: StationSearchWindowViewModel.SearchResultItem station })
        {
            return;
        }

        if (viewModel.AddStationCommand.CanExecute(station))
        {
            viewModel.AddStationCommand.Execute(station);
        }
    }

    private void OnPlayPauseResultClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        if (TryGetResultItem(sender) is not { } station)
        {
            return;
        }

        if (viewModel.PlayOrPauseResultCommand.CanExecute(station))
        {
            viewModel.PlayOrPauseResultCommand.Execute(station);
        }
    }

    private void OnRemoveResultClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        if (TryGetResultItem(sender) is not { } station)
        {
            return;
        }

        if (viewModel.RemoveResultCommand.CanExecute(station))
        {
            viewModel.RemoveResultCommand.Execute(station);
        }
    }

    private void OnOpenProviderWebsiteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.SelectedProviderWebsiteUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = viewModel.SelectedProviderWebsiteUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            viewModel.Status = "Could not open provider website: " + ex.Message;
        }
    }

    private void OnCopyResultNameClick(object? sender, RoutedEventArgs e)
    {
        CopyResultAsync(sender, static station => station.Name, "station name")
            .ForgetWithErrorHandling("Copy station result name", showDialog: false);
    }

    private void OnCopyResultUrlClick(object? sender, RoutedEventArgs e)
    {
        CopyResultAsync(sender, static station => station.StreamUrl, "stream URL")
            .ForgetWithErrorHandling("Copy station result URL", showDialog: false);
    }

    private void OnCopyResultNameUrlClick(object? sender, RoutedEventArgs e)
    {
        CopyResultAsync(sender, static station => station.Name + " - " + station.StreamUrl, "station entry")
            .ForgetWithErrorHandling("Copy station result name+URL", showDialog: false);
    }

    private async Task CopyResultAsync(object? sender, Func<StationSearchWindowViewModel.SearchResultItem, string> selector, string label)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        var station = TryGetResultItem(sender);
        if (station is null)
        {
            viewModel.Status = "Select a station result first.";
            return;
        }

        var value = selector(station);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(value);
        viewModel.Status = "Copied " + label + ": " + station.Name;
    }

    private static StationSearchWindowViewModel.SearchResultItem? TryGetResultItem(object? sender)
    {
        if (sender is Control { DataContext: StationSearchWindowViewModel.SearchResultItem station })
        {
            return station;
        }

        if (sender is ContextMenu { PlacementTarget.DataContext: StationSearchWindowViewModel.SearchResultItem placementStation })
        {
            return placementStation;
        }

        return null;
    }

    private void OnOpenPageSizeMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } contextMenu } button)
        {
            return;
        }

        contextMenu.PlacementTarget = button;
        contextMenu.Open();
    }

    private void OnPageSizePreset10Click(object? sender, RoutedEventArgs e)
    {
        ApplyPageSizePresetAsync(10).ForgetWithErrorHandling("Set station page size", showDialog: false);
    }

    private void OnPageSizePreset20Click(object? sender, RoutedEventArgs e)
    {
        ApplyPageSizePresetAsync(20).ForgetWithErrorHandling("Set station page size", showDialog: false);
    }

    private void OnPageSizePreset50Click(object? sender, RoutedEventArgs e)
    {
        ApplyPageSizePresetAsync(50).ForgetWithErrorHandling("Set station page size", showDialog: false);
    }

    private void OnPageSizePreset100Click(object? sender, RoutedEventArgs e)
    {
        ApplyPageSizePresetAsync(100).ForgetWithErrorHandling("Set station page size", showDialog: false);
    }

    private async Task ApplyPageSizePresetAsync(int pageSize)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.SetPageSizePresetCommand.ExecuteAsync(pageSize);
    }

}

