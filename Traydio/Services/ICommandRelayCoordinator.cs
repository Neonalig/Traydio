namespace Traydio.Services;

/// <summary>
/// Coordinates command relay clients and servers for single-instance command forwarding.
/// </summary>
public interface ICommandRelayCoordinator
{
    /// <summary>
    /// Attempts to relay a command to the already-running primary instance.
    /// </summary>
    /// <param name="commandText">Command text to relay.</param>
    /// <returns><see langword="true"/> when a relay channel accepted the command.</returns>
    bool TryRelayToPrimary(string commandText);

    /// <summary>
    /// Dispatches a command in the current process.
    /// </summary>
    /// <param name="commandText">Command text to dispatch locally.</param>
    /// <returns><see langword="true"/> when dispatch succeeds.</returns>
    bool DispatchLocal(string commandText);

    /// <summary>
    /// Starts all configured primary relay servers.
    /// </summary>
    void StartPrimaryRelay();

    /// <summary>
    /// Stops all configured primary relay servers.
    /// </summary>
    void StopPrimaryRelay();
}

