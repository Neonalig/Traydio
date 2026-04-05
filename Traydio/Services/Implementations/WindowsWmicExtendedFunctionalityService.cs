using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Traydio.Services.Implementations;

[SupportedOSPlatform("windows")]
public sealed class WindowsWmicExtendedFunctionalityService : IWmicExtendedFunctionalityService
{
    private const string _WMIC_CAPABILITY_NAME = "WMIC~~~~";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return File.Exists(GetSystem32WmicPath()) || File.Exists(GetSysWow64WmicPath());
    }

    public async Task<WmicInstallResult> InstallAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WmicInstallResult(false, false, "WMIC installation is only available on Windows.");
        }

        if (IsInstalled())
        {
            return new WmicInstallResult(true, false, "WMIC is already installed.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = $"/Online /Add-Capability /CapabilityName:{_WMIC_CAPABILITY_NAME} /NoRestart",
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new WmicInstallResult(false, false, "Unable to start DISM for WMIC installation.");
            }

            await process.WaitForExitAsync().ConfigureAwait(false);

            return process.ExitCode switch
            {
                0 => new WmicInstallResult(true, false, "WMIC capability installed successfully."),
                3010 => new WmicInstallResult(true, true, "WMIC capability installed. A restart is required."),
                _ => new WmicInstallResult(false, false, "WMIC installation failed. DISM exit code: " + process.ExitCode),
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new WmicInstallResult(false, false, "WMIC installation canceled.");
        }
        catch (Exception ex)
        {
            return new WmicInstallResult(false, false, "WMIC installation failed: " + ex.Message);
        }
    }

    private static string GetSystem32WmicPath()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(windowsDirectory, "System32", "wbem", "wmic.exe");
    }

    private static string GetSysWow64WmicPath()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(windowsDirectory, "SysWOW64", "wbem", "wmic.exe");
    }
}

