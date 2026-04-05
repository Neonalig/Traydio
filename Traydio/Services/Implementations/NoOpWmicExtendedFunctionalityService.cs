using System.Threading.Tasks;

namespace Traydio.Services.Implementations;

public sealed class NoOpWmicExtendedFunctionalityService : IWmicExtendedFunctionalityService
{
    public bool IsSupported => false;

    public bool IsInstalled()
    {
        return false;
    }

    public Task<WmicInstallResult> InstallAsync()
    {
        return Task.FromResult(new WmicInstallResult(
            Success: false,
            RequiresRestart: false,
            Message: "WMIC installation is only available on Windows."));
    }
}

