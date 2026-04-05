using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Traydio.Common;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(PluginManagementWindowViewModel))]
public partial class PluginManagementPage : UserControl
{
    public PluginManagementPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public PluginManagementPage(PluginManagementWindowViewModel viewModel)
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

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

    private void OnPluginSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: PluginManagementWindowViewModel.InstalledPluginItem pluginItem })
        {
            return;
        }

        viewModel.OpenPluginSettingsCommand.Execute(pluginItem);
    }
}

