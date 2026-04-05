using System;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Traydio.Services.Implementations;

[SupportedOSPlatform("windows")]
public sealed class WindowsProtocolRegistrationService : IProtocolRegistrationService
{
    public bool IsRegistered(string scheme)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{scheme}");
        return key is not null;
    }

    public bool Register(string scheme, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "Protocol registration is only supported on Windows.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            error = "Protocol scheme is required.";
            return false;
        }

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                error = "Unable to resolve executable path.";
                return false;
            }

            using var root = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{scheme}");
            root.SetValue(string.Empty, $"URL:{scheme} Protocol");
            root.SetValue("URL Protocol", string.Empty);

            using var shell = root.CreateSubKey("shell");
            using var open = shell.CreateSubKey("open");
            using var command = open.CreateSubKey("command");
            command.SetValue(string.Empty, $"\"{exePath}\" --url \"%1\"");

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool Unregister(string scheme, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "Protocol registration is only supported on Windows.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            error = "Protocol scheme is required.";
            return false;
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\{scheme}", throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}


