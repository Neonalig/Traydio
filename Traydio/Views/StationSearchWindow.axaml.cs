using Avalonia.Controls;
using Traydio.Common;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(StationSearchWindowViewModel))]
public partial class StationSearchWindow : Window
{
    public StationSearchWindow(StationSearchWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

