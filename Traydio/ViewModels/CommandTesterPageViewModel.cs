using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Commands;
using Traydio.Common;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(CommandTesterPage))]
public partial class CommandTesterPageViewModel(ICommandTextRouter commandTextRouter) : ViewModelBase
{
    public IReadOnlyList<CommandTipItem> SyntaxHints { get; } =
    [
        new("play", "Dispatches Play for the active station."),
        new("pause", "Dispatches Pause for current playback."),
        new("toggle", "Toggles play/pause state."),
        new("open", "Opens Station Manager page."),
        new("search", "Opens station catalog search page."),
        new("plugins", "Opens Plugin Manager page."),
        new("settings", "Opens Settings page."),
        new("volup", "Increases volume by the default increment."),
        new("voldown", "Decreases volume by the default increment."),
        new("volume <0-100>", "Sets absolute output volume."),
        new("station <station-id>", "Plays a specific station by ID."),
    ];

    [ObservableProperty]
    private string _commandText = "play";

    [ObservableProperty]
    private string _status = "Enter a command and click Run.";

    [RelayCommand]
    private void Run()
    {
        if (string.IsNullOrWhiteSpace(CommandText))
        {
            Status = "Enter a command first.";
            return;
        }

        var trimmed = CommandText.Trim();
        var handled = commandTextRouter.TryDispatch(trimmed);
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

