using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Traydio.ViewModels;

namespace Traydio.Views;

public partial class CommandTesterPage : UserControl
{
    public CommandTesterPage()
        : this(new CommandTesterPageViewModel(new NullCommandTextRouter()))
    {
    }

    public CommandTesterPage(CommandTesterPageViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }

    private sealed class NullCommandTextRouter : Commands.ICommandTextRouter
    {
        public bool TryDispatch(string commandText) => false;
    }
}

