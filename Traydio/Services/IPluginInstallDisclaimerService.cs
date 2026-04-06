using System.Threading;
using System.Threading.Tasks;
using Traydio.Common;

namespace Traydio.Services;

public interface IPluginInstallDisclaimerService
{
    Task<bool> EnsureAcceptedAsync(string pluginId, PluginInstallDisclaimer disclaimer, CancellationToken cancellationToken);

    Task<bool> ShowAsync(PluginInstallDisclaimer disclaimer, bool requireAcceptance, CancellationToken cancellationToken);
}

