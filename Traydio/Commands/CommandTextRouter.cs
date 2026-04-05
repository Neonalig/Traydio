using System;

namespace Traydio.Commands;

public sealed class CommandTextRouter(IAppCommandDispatcher dispatcher) : ICommandTextRouter
{
    public bool TryDispatch(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        var parts = commandText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "play":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Play });
                return true;
            case "pause":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Pause });
                return true;
            case "toggle":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.TogglePause });
                return true;
            case "open":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationManager });
                return true;
            case "search":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationSearch });
                return true;
            case "plugins":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenPluginManager });
                return true;
            case "settings":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenSettings });
                return true;
            case "volup":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeUp, Value = 5 });
                return true;
            case "voldown":
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeDown, Value = 5 });
                return true;
            case "volume" when parts.Length >= 2 && int.TryParse(parts[1], out var volume):
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.SetVolume, Value = volume });
                return true;
            case "station" when parts.Length >= 2:
                dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.PlayStation, StationId = parts[1] });
                return true;
            default:
                return false;
        }
    }
}

