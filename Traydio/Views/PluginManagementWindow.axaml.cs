using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Classic.CommonControls.Dialogs;
using Traydio.Common;
using Traydio.Services;
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

    private void OnBrowsePluginClick(object? sender, RoutedEventArgs e)
    {
        OnBrowsePluginClickAsync().ForgetWithErrorHandling("Plugin browse", showDialog: true);
    }

    private async Task OnBrowsePluginClickAsync()
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

    private void OnPluginSettingsClick(object? sender, RoutedEventArgs e)
    {
        OnPluginSettingsClickAsync(sender).ForgetWithErrorHandling("Plugin settings", showDialog: true);
    }

    private async Task OnPluginSettingsClickAsync(object? sender)
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

    private void OnUpgradeCandidateClick(object? sender, RoutedEventArgs e)
    {
        OnUpgradeCandidateClickAsync(sender).ForgetWithErrorHandling("Plugin upgrade", showDialog: true);
    }

    private async Task OnUpgradeCandidateClickAsync(object? sender)
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

    private void OnInstallCandidateClick(object? sender, RoutedEventArgs e)
    {
        OnInstallCandidateClickAsync(sender).ForgetWithErrorHandling("Plugin install candidate", showDialog: true);
    }

    private async Task OnInstallCandidateClickAsync(object? sender)
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
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin install");
    }

    private void OnInstalledCheckboxClick(object? sender, RoutedEventArgs e)
    {
        OnInstalledCheckboxClickAsync(sender).ForgetWithErrorHandling("Plugin state toggle", showDialog: true);
    }

    private async Task OnInstalledCheckboxClickAsync(object? sender)
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

    private void OnInstalledListDoubleTapped(object? sender, TappedEventArgs e)
    {
        OnInstalledListDoubleTappedAsync(sender).ForgetWithErrorHandling("Installed plugin double tap", showDialog: true);
    }

    private async Task OnInstalledListDoubleTappedAsync(object? sender)
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

    private void OnInstalledListKeyDown(object? sender, KeyEventArgs e)
    {
        OnInstalledListKeyDownAsync(e).ForgetWithErrorHandling("Installed plugin key action", showDialog: true);
    }

    private async Task OnInstalledListKeyDownAsync(KeyEventArgs e)
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

    private void OnUninstallPluginClick(object? sender, RoutedEventArgs e)
    {
        OnUninstallPluginClickAsync(sender).ForgetWithErrorHandling("Plugin uninstall", showDialog: true);
    }

    private async Task OnUninstallPluginClickAsync(object? sender)
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
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var choice = await MessageBox.ShowDialog(
            owner,
            "Confirm uninstall",
            $"Uninstall plugin '{pluginItem.DisplayName}'?\n\nIf the file is currently locked, it will be marked for delete on restart and disabled now.",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        viewModel.RemoveInstalledPlugin(pluginItem);
        await ShowViewModelStatusDialogAsync(viewModel, "Plugin remove");
    }

    private void OnRestartAppClick(object? sender, RoutedEventArgs e)
    {
        OnRestartAppClickAsync().ForgetWithErrorHandling("Restart app", showDialog: true);
    }

    private async Task OnRestartAppClickAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var choice = await MessageBox.ShowDialog(
            owner,
            "Restart app",
            "Restart Traydio now?\n\nUse this after changing native plugin dependency paths so new DLLs are loaded.",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            await ShowInfoDialogAsync("Restart app", "Could not determine application path for restart.");
            return;
        }

        var restartArguments = "--cmd plugins";
        if (Debugger.IsAttached)
        {
            // Best effort: the relaunched app can request debugger attach on startup.
            restartArguments += " --debugger-launch";
        }

        if (OperatingSystem.IsWindows())
        {
            var escapedPath = processPath.Replace("\"", "\"\"");
            var delayedCommand = $"/c timeout /t 1 /nobreak >nul && start \"\" \"{escapedPath}\" {restartArguments}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = delayedCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = restartArguments,
                UseShellExecute = true,
            });
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    private void OnCandidateListDoubleTapped(object? sender, TappedEventArgs e)
    {
        OnCandidateListDoubleTappedAsync(sender).ForgetWithErrorHandling("Candidate double tap install", showDialog: true);
    }

    private async Task OnCandidateListDoubleTappedAsync(object? sender)
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

    private void OnInstalledListDrop(object? sender, DragEventArgs e)
    {
        InstallDroppedPluginFilesAsync(e).ForgetWithErrorHandling("Installed list drop install", showDialog: true);
    }

    private void OnCandidateListDrop(object? sender, DragEventArgs e)
    {
        InstallDroppedPluginFilesAsync(e).ForgetWithErrorHandling("Candidate list drop install", showDialog: true);
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

    private void OnChangePluginDirectoryClick(object? sender, RoutedEventArgs e)
    {
        OnChangePluginDirectoryClickAsync().ForgetWithErrorHandling("Change plugin directory", showDialog: true);
    }

    private async Task OnChangePluginDirectoryClickAsync()
    {
        if (DataContext is not PluginManagementWindowViewModel viewModel)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || topLevel is not Window owner)
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
            var choice = await MessageBox.ShowDialog(
                owner,
                "Move existing plugins?",
                "Migrate existing plugin DLLs to the new folder?\n\nYes = copy existing plugins\nNo = keep existing plugins in current folder\nCancel = abort",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question
            );
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            migrate = choice == MessageBoxResult.Yes;
        }

        if (viewModel.ChangePluginDirectory(nextPath, migrate, out var error))
        {
            await ShowInfoDialogAsync("Plugin folder", "Plugin install folder updated.");
            return;
        }

        await ShowInfoDialogAsync("Plugin folder", "Could not change plugin folder: " + (error ?? "Unknown error."));
    }

    private void OnInstallFromPathClick(object? sender, RoutedEventArgs e)
    {
        OnInstallFromPathClickAsync().ForgetWithErrorHandling("Install plugin from path", showDialog: true);
    }

    private async Task OnInstallFromPathClickAsync()
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
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        await MessageBox.ShowDialog(owner, title, message, MessageBoxButtons.Ok, MessageBoxIcon.Information);
    }
}

