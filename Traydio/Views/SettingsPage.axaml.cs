using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Classic.CommonControls.Dialogs;
using Traydio.Common;
using Traydio.Services;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(SettingsPageViewModel))]
public partial class SettingsPage : UserControl, IMainWindowClosingHandler
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

    private void OnRestartAppClick(object? sender, RoutedEventArgs e)
    {
        OnRestartAppClickAsync().ForgetWithErrorHandling("Restart app from settings", showDialog: true);
    }

    private async Task OnRestartAppClickAsync()
    {
        if (DataContext is not SettingsPageViewModel viewModel)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var choice = await MessageBox.ShowDialog(
            owner,
            "Save before restart?",
            "You changed settings. Save before restarting Traydio?\n\nYes = Save then restart\nNo = Restart without saving\nCancel = Stay here",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        if (choice == MessageBoxResult.Yes)
        {
            viewModel.SaveCommand.Execute(null);
        }

        viewModel.RestartAppCommand.Execute(null);
    }

    public async Task<bool> CanCloseMainWindowAsync()
    {
        if (DataContext is not SettingsPageViewModel viewModel)
        {
            return true;
        }

        if (!viewModel.HasUnsavedChanges)
        {
            return true;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return true;
        }

        var choice = await MessageBox.ShowDialog(
            owner,
            "Save settings before closing?",
            "You have unsaved settings changes.\n\nYes = Save and close\nNo = Close without saving\nCancel = Stay open",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (choice == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (choice == MessageBoxResult.Yes)
        {
            viewModel.SaveCommand.Execute(null);
        }

        return true;
    }
}

