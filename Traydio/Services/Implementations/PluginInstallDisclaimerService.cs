using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Classic.CommonControls.Dialogs;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class PluginInstallDisclaimerService(IPluginSettingsProvider pluginSettingsProvider) : IPluginInstallDisclaimerService
{
    private const string _ACCEPTANCE_KEY_PREFIX = "installDisclaimerAccepted:";

    public async Task<bool> EnsureAcceptedAsync(string pluginId, PluginInstallDisclaimer disclaimer, CancellationToken cancellationToken)
    {
        var settings = pluginSettingsProvider.GetPluginSettings(pluginId);
        var acceptanceKey = _ACCEPTANCE_KEY_PREFIX + disclaimer.Version;
        if (settings.TryGetValue(acceptanceKey, out var accepted) &&
            string.Equals(accepted, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var acceptedNow = await ShowAsync(disclaimer, requireAcceptance: true, cancellationToken).ConfigureAwait(false);
        if (!acceptedNow)
        {
            return false;
        }

        var updatedSettings = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase)
        {
            [acceptanceKey] = "true",
        };
        pluginSettingsProvider.SavePluginSettings(pluginId, updatedSettings);
        return true;
    }

    public async Task<bool> ShowAsync(PluginInstallDisclaimer disclaimer, bool requireAcceptance, CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ShowOnUiThreadAsync(disclaimer, requireAcceptance, cancellationToken).ConfigureAwait(false);
        }

        var queued = await Dispatcher.UIThread
            .InvokeAsync(() => ShowOnUiThreadAsync(disclaimer, requireAcceptance, cancellationToken))
            .ConfigureAwait(false);
        return queued;
    }

    private static async Task<bool> ShowOnUiThreadAsync(
        PluginInstallDisclaimer disclaimer,
        bool requireAcceptance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var owner = GetOwnerWindow();
        if (owner is null)
        {
            return !requireAcceptance;
        }

        var message = disclaimer.Message;
        if (!string.IsNullOrWhiteSpace(disclaimer.LinkUrl))
        {
            message += "\n\nMore info: " + disclaimer.LinkUrl;
        }

        if (!requireAcceptance)
        {
            await MessageBox.ShowDialog(owner, message, disclaimer.Title, MessageBoxButtons.Ok, MessageBoxIcon.Information);
            return true;
        }

        var choice = await MessageBox.ShowDialog(owner, message, disclaimer.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return choice == MessageBoxResult.Yes;
    }

    private static Window? GetOwnerWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.Windows.FirstOrDefault(window => window.IsActive)
               ?? desktop.MainWindow
               ?? desktop.Windows.FirstOrDefault();
    }
}


