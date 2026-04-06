using System;
using Traydio.Services;

namespace Traydio.Commands;

public sealed class CommandTextRouter(IAppCommandDispatcher dispatcher) : ICommandTextRouter
{
    public bool TryDispatch(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            TraydioTrace.Debug("CommandRouter", "Rejected blank command text.");
            return false;
        }

        var parts = commandText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            TraydioTrace.Debug("CommandRouter", "Rejected empty command parts.");
            return false;
        }

        var commandName = parts[0].ToLowerInvariant();

        switch (commandName)
        {
            case "play":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Play });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: play");
                return true;
            case "pause":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Pause });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: pause");
                return true;
            case "toggle":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.TogglePause });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: toggle");
                return true;
            case "open":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationManager });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: open");
                return true;
            case "search":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationSearch });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: search");
                return true;
            case "plugins":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenPluginManager });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: plugins");
                return true;
            case "settings":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenSettings });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: settings");
                return true;
            case "volup":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeUp, Value = 5 });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: volup");
                return true;
            case "voldown":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeDown, Value = 5 });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: voldown");
                return true;
            case "volume" when parts.Length >= 2 && int.TryParse(parts[1], out var volume):
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.SetVolume, Value = volume });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: volume " + volume);
                return true;
            case "station" when parts.Length >= 2:
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.PlayStation, StationId = parts[1] });
                TraydioTrace.Debug("CommandRouter", "Dispatched command: station " + parts[1]);
                return true;
            default:
                TraydioTrace.Warn("CommandRouter", "Unknown command: " + commandText.Trim());
                return false;
        }
    }
}

