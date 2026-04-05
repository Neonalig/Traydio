using System.Threading.Tasks;
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

    private void OnUpgradeCandidateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: PluginManagementWindowViewModel.PluginCandidateItem candidate })
        {
            return;
        }

        viewModel.UpgradeCandidateCommand.Execute(candidate);
    }

    private async void OnInstallFromPathClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        var warning = viewModel.GetDowngradeWarningForPath(viewModel.PluginDllPath);
        if (!string.IsNullOrWhiteSpace(warning))
        {
            await ShowInfoDialogAsync("Plugin downgrade warning", warning);
        }

        viewModel.InstallPluginFromFilePath(viewModel.PluginDllPath);
    }

    private async Task ShowInfoDialogAsync(string title, string message)
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

