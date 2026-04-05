using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Traydio.Common;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(PluginManagementWindowViewModel))]
public partial class PluginManagementWindow : Window
{
    public PluginManagementWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public PluginManagementWindow(PluginManagementWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private async void OnBrowsePluginClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select plugin DLL",
            FileTypeFilter =
            [
                new FilePickerFileType("Plugin DLL")
                {
                    Patterns = ["*.dll"],
                },
            ],
        });

        var selected = result.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        viewModel.PluginDllPath = selected.TryGetLocalPath() ?? selected.Name;
    }
}

