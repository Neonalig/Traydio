using System;

namespace Traydio.Commands;

public sealed class CommandTextRouter : ICommandTextRouter
{
    private readonly IAppCommandDispatcher _dispatcher;

    public CommandTextRouter(IAppCommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

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
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Play });
                return true;
            case "pause":
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Pause });
                return true;
            case "toggle":
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.TogglePause });
                return true;
            case "open":
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationManager });
                return true;
            case "volup":
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeUp, Value = 5 });
                return true;
            case "voldown":
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeDown, Value = 5 });
                return true;
            case "volume" when parts.Length >= 2 && int.TryParse(parts[1], out var volume):
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.SetVolume, Value = volume });
                return true;
            case "station" when parts.Length >= 2:
                _dispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.PlayStation, StationId = parts[1] });
                return true;
            default:
                return false;
        }
    }
}

