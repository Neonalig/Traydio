using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Traydio.Commands;

namespace Traydio.Views;

public partial class CommandTesterPage : UserControl
{
    private static readonly string[] _syntaxHints =
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

    private readonly ICommandTextRouter _commandTextRouter;
    private readonly TextBox _commandTextBox;
    private readonly TextBlock _statusText;

    public CommandTesterPage()
        : this(new NullCommandTextRouter())
    {
    }

    public CommandTesterPage(ICommandTextRouter commandTextRouter)
    {
        _commandTextRouter = commandTextRouter;
        _commandTextBox = new TextBox();
        _statusText = new TextBlock();

        AvaloniaXamlLoader.Load(this);

        _commandTextBox = this.FindControl<TextBox>("CommandTextBox")
            ?? throw new InvalidOperationException("Commands page is missing CommandTextBox.");
        _statusText = this.FindControl<TextBlock>("StatusText")
            ?? throw new InvalidOperationException("Commands page is missing StatusText.");
        var syntaxHintsList = this.FindControl<ItemsControl>("SyntaxHintsList")
            ?? throw new InvalidOperationException("Commands page is missing SyntaxHintsList.");

        _commandTextBox.Text = "play";
        _statusText.Text = "Enter a command and click Run.";
        syntaxHintsList.ItemsSource = _syntaxHints;
    }

    private void OnRunClick(object? sender, RoutedEventArgs e)
    {
        var commandText = _commandTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(commandText))
        {
            _statusText.Text = "Enter a command first.";
            return;
        }

        var handled = _commandTextRouter.TryDispatch(commandText);
        _statusText.Text = handled
            ? "Dispatched: " + commandText
            : "Unknown command: " + commandText;
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _commandTextBox.Text = string.Empty;
        _statusText.Text = "Enter a command and click Run.";
    }

    private sealed class NullCommandTextRouter : ICommandTextRouter
    {
        public bool TryDispatch(string commandText) => false;
    }
}

