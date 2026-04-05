using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Traydio.Common;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(StationSearchWindowViewModel))]
public partial class StationSearchWindow : Window
{
    public StationSearchWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public StationSearchWindow(StationSearchWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

}

