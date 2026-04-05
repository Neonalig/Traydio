using Avalonia.Controls.ApplicationLifetimes;

namespace Traydio.Commands;

public interface IAppCommandDispatcher
{
    void Initialize(IClassicDesktopStyleApplicationLifetime lifetime);

    void Dispatch(AppCommand command);
}

