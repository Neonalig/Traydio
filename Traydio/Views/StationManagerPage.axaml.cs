using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Traydio.Common;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(StationManagerPageViewModel))]
public partial class StationManagerPage : UserControl
{
    public StationManagerPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public StationManagerPage(StationManagerPageViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}

