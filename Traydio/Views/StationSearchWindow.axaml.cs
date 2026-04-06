using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Traydio.Common;
using Traydio.ViewModels;

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

        if (listBox.SelectedItem is not DiscoveredStation station)
        {
            return;
        }

        if (viewModel.AddStationCommand.CanExecute(station))
        {
            viewModel.AddStationCommand.Execute(station);
        }
    }

    private void OnAddResultClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationSearchWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: DiscoveredStation station })
        {
            return;
        }

        if (viewModel.AddStationCommand.CanExecute(station))
        {
            viewModel.AddStationCommand.Execute(station);
        }
    }

}

