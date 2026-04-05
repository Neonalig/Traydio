using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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
        viewModel.InstallPluginFromFilePath(viewModel.PluginDllPath);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin install");
    }

    private async void OnPluginSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: PluginManagementWindowViewModel.InstalledPluginItem pluginItem })
        {
            return;
        }

        if (viewModel.TryOpenPluginSettings(pluginItem, out var error))
        {
            return;
        }

        await ShowInfoDialogAsync("Plugin settings error", error ?? "Unknown error.");
    }

    private async void OnUpgradeCandidateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: PluginManagementWindowViewModel.PluginCandidateItem candidate })
        {
            return;
        }

        viewModel.InstallCandidate(candidate);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin upgrade");
    }

    private async void OnInstalledCheckboxClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not CheckBox { DataContext: PluginManagementWindowViewModel.InstalledPluginItem pluginItem, IsChecked: { } isChecked })
        {
            return;
        }

        viewModel.SetInstalledPluginEnabled(pluginItem, isChecked);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin state");
    }

    private async void OnInstalledListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not ListBox { SelectedItem: PluginManagementWindowViewModel.InstalledPluginItem pluginItem })
        {
            return;
        }

        viewModel.ToggleInstalledPlugin(pluginItem);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin state");
    }

    private async void OnInstalledListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return;
        }

        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        var pluginItem = viewModel.SelectedInstalledPlugin;
        if (pluginItem is null)
        {
            return;
        }

        await ConfirmAndRemovePluginAsync(viewModel, pluginItem);
        e.Handled = true;
    }

    private async void OnUninstallPluginClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: PluginManagementWindowViewModel.InstalledPluginItem pluginItem })
        {
            return;
        }

        await ConfirmAndRemovePluginAsync(viewModel, pluginItem);
    }

    private async Task ConfirmAndRemovePluginAsync(PluginManagementWindowViewModel viewModel, PluginManagementWindowViewModel.InstalledPluginItem pluginItem)
    {
        var verb = pluginItem.CanUninstall ? "uninstall" : "disable";
        var choice = await ShowYesNoCancelDialogAsync(
            $"Confirm {verb}",
            $"{(pluginItem.CanUninstall ? "Uninstall" : "Disable")} plugin '{pluginItem.DisplayName}'?");

        if (choice != UserChoice.Yes)
        {
            return;
        }

        viewModel.RemoveInstalledPlugin(pluginItem);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin remove");
    }

    private async void OnCandidateListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        if (sender is not ListBox { SelectedItem: PluginManagementWindowViewModel.PluginCandidateItem candidate })
        {
            return;
        }

        viewModel.InstallCandidate(candidate);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin install");
    }

    private void OnPluginListDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.TryGetFiles()
            ?.OfType<IStorageFile>()
            .Any(file => string.Equals(Path.GetExtension(file.Name), ".dll", StringComparison.OrdinalIgnoreCase)) == true;

        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnInstalledListDrop(object? sender, DragEventArgs e)
    {
        await InstallDroppedPluginFilesAsync(e);
    }

    private async void OnCandidateListDrop(object? sender, DragEventArgs e)
    {
        await InstallDroppedPluginFilesAsync(e);
    }

    private async Task InstallDroppedPluginFilesAsync(DragEventArgs e)
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles()
            ?.OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => path!)
            .Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        if (files.Length == 0)
        {
            return;
        }

        viewModel.InstallPluginsFromDroppedPaths(files);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin install");
    }

    private async void OnChangePluginDirectoryClick(object? sender, RoutedEventArgs e)
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

        var picked = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose plugin install folder",
        });

        var selected = picked.FirstOrDefault();
        var selectedPath = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var currentPath = Path.GetFullPath(viewModel.PluginDirectory);
        var nextPath = Path.GetFullPath(selectedPath);
        if (string.Equals(currentPath, nextPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var hasExistingPlugins = Directory.Exists(currentPath)
            && Directory.GetFiles(currentPath, "*.dll", SearchOption.TopDirectoryOnly).Length > 0;

        var migrate = false;
        if (hasExistingPlugins)
        {
            var choice = await ShowYesNoCancelDialogAsync(
                "Move existing plugins?",
                "Migrate existing plugin DLLs to the new folder?\n\nYes = copy existing plugins\nNo = keep existing plugins in current folder\nCancel = abort");

            if (choice == UserChoice.Cancel)
            {
                return;
            }

            migrate = choice == UserChoice.Yes;
        }

        if (viewModel.ChangePluginDirectory(nextPath, migrate, out var error))
        {
            await ShowInfoDialogAsync("Plugin folder", "Plugin install folder updated.");
            return;
        }

        await ShowInfoDialogAsync("Plugin folder", "Could not change plugin folder: " + (error ?? "Unknown error."));
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
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin install");
    }

    private async Task ShowViewModelStatusDialogAsync(PluginManagementWindowViewModel viewModel, string title)
    {
        if (string.IsNullOrWhiteSpace(viewModel.Status))
        {
            return;
        }

        await ShowInfoDialogAsync(title, viewModel.Status);
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

    private async Task<UserChoice> ShowYesNoCancelDialogAsync(string title, string message)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner)
        {
            return UserChoice.Cancel;
        }

        var result = UserChoice.Cancel;
        var dialog = new Window
        {
            Title = title,
            Width = 560,
            Height = 240,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var yesButton = new Button { Content = "Yes", MinWidth = 90 };
        var noButton = new Button { Content = "No", MinWidth = 90 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };

        yesButton.Click += (_, _) => { result = UserChoice.Yes; dialog.Close(); };
        noButton.Click += (_, _) => { result = UserChoice.No; dialog.Close(); };
        cancelButton.Click += (_, _) => { result = UserChoice.Cancel; dialog.Close(); };

        dialog.Content = new Grid
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
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { yesButton, noButton, cancelButton },
                    [Grid.RowProperty] = 1,
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    private enum UserChoice
    {
        Yes,
        No,
        Cancel,
    }
}

