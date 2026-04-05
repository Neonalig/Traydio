using System;
using System.Linq;

namespace Traydio.Services.Implementations;

public sealed class ProtocolUrlStartupCommandBridge(IStationRepository stationRepository) : IStartupCommandBridge
{
    public bool TryGetCommand(string[] args, out string? commandText)
    {
        commandText = null;

        if (args.Length == 0 || !stationRepository.Communication.EnableProtocolUrlRelay)
        {
            return false;
        }

        var candidate = GetUrlArgument(args);
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, stationRepository.Communication.ProtocolScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = BuildSegments(uri);
        if (segments.Length == 0)
        {
            return false;
        }

        switch (segments[0].ToLowerInvariant())
        {
            case "play":
                commandText = "play";
                return true;
            case "pause":
                commandText = "pause";
                return true;
            case "toggle":
                commandText = "toggle";
                return true;
            case "open":
                commandText = "open";
                return true;
            case "station" when segments.Length >= 2:
                commandText = "station " + segments[1];
                return true;
            case "volume" when segments.Length >= 2 && int.TryParse(segments[1], out var volume):
                commandText = "volume " + Math.Clamp(volume, 0, 100);
                return true;
            default:
                return false;
        }
    }

    private static string GetUrlArgument(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "--url", StringComparison.OrdinalIgnoreCase))
        {
            return args[1];
        }

        return args[0];
    }

    private static string[] BuildSegments(Uri uri)
    {
        var host = uri.Host;
        var path = uri.AbsolutePath.Trim('/');

        if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return [host];
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        return new[] { host }
            .Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
    }
}



