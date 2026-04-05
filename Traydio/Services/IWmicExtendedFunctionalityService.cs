using System.Threading.Tasks;

namespace Traydio.Services;

public interface IWmicExtendedFunctionalityService
{
    bool IsSupported { get; }

    bool IsInstalled();

    Task<WmicInstallResult> InstallAsync();
}

public readonly record struct WmicInstallResult(bool Success, bool RequiresRestart, string Message);

