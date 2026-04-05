using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Commands;
using Traydio.Common;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(Traydio.Views.CommandTesterPage))]
public partial class CommandTesterPageViewModel : ViewModelBase
{
    private readonly ICommandTextRouter _commandTextRouter;

    public IReadOnlyList<string> SyntaxHints { get; } =
    [
        "play",
        "pause",
        "toggle",
        "open",
        "search",
        "plugins",
        "settings",
        "volup",
        "voldown",
        "volume <0-100>",
        "station <station-id>",
    ];

    [ObservableProperty]
    private string _commandText = "play";

    [ObservableProperty]
    private string _status = "Enter a command and click Run.";

    public CommandTesterPageViewModel(ICommandTextRouter commandTextRouter)
    {
        _commandTextRouter = commandTextRouter;
    }

    [RelayCommand]
    private void Run()
    {
        if (string.IsNullOrWhiteSpace(CommandText))
        {
            Status = "Enter a command first.";
            return;
        }

        var trimmed = CommandText.Trim();
        var handled = _commandTextRouter.TryDispatch(trimmed);
        Status = handled
            ? $"Dispatched: {trimmed}"
            : $"Unknown command: {trimmed}";
    }

    [RelayCommand]
    private void Clear()
    {
        CommandText = string.Empty;
        Status = "Enter a command and click Run.";
    }
}

