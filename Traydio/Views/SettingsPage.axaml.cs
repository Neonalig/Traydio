using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Traydio.Common;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(SettingsPageViewModel))]
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public SettingsPage(SettingsPageViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}

