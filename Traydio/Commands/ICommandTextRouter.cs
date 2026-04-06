namespace Traydio.Commands;

/// <summary>
/// Routes plain-text commands to strongly-typed application commands.
/// </summary>
public interface ICommandTextRouter
{
    /// <summary>
    /// Attempts to parse and dispatch a command.
    /// </summary>
    /// <param name="commandText">Command text to parse.</param>
    /// <returns><see langword="true"/> when a known command is dispatched; otherwise <see langword="false"/>.</returns>
    bool TryDispatch(string commandText);
}

