using Avalonia.Controls.ApplicationLifetimes;

namespace Traydio.Commands;

/// <summary>
/// Dispatches parsed application commands to domain services.
/// </summary>
public interface IAppCommandDispatcher
{
    /// <summary>
    /// Initializes dispatcher integration with desktop application lifetime.
    /// </summary>
    /// <param name="lifetime">Desktop lifetime used for shutdown commands.</param>
    void Initialize(IClassicDesktopStyleApplicationLifetime lifetime);

    /// <summary>
    /// Dispatches a command for execution.
    /// </summary>
    /// <param name="command">Command payload to execute.</param>
    void Dispatch(AppCommand command);
}

