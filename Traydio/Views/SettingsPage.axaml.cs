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
    private bool _suppressNextClosePrompt;

    public SettingsPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public SettingsPage(SettingsPageViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnApplyThemeClick(object? sender, RoutedEventArgs e)
    {
        OnApplyThemeClickAsync().ForgetWithErrorHandling("Apply theme from settings", showDialog: true);
    }

    private async Task OnApplyThemeClickAsync()
    {
        if (DataContext is not SettingsPageViewModel viewModel)
        {
            return;
        }

        // Theme apply now always persists first, then recreates the main window.
        viewModel.SaveCommand.Execute(null);
        _suppressNextClosePrompt = true;
        viewModel.ApplyThemeWindowCommand.Execute(null);
        await Task.CompletedTask;
    }

    public async Task<bool> CanCloseMainWindowAsync()
    {
        if (_suppressNextClosePrompt)
        {
            _suppressNextClosePrompt = false;
            return true;
        }

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
            "Save settings changes?",
            "You have unsaved settings changes.\n\nYes = Save changes\nNo = Discard changes\nCancel = Stay on this page",
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

