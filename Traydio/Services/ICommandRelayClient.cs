namespace Traydio.Services;

/// <summary>
/// Sends command payloads to a running primary instance.
/// </summary>
public interface ICommandRelayClient
{
    /// <summary>
    /// Gets a stable diagnostic name for the relay client.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Attempts to send a command to the primary instance.
    /// </summary>
    /// <param name="commandText">Command text to send.</param>
    /// <returns><see langword="true"/> when send succeeds.</returns>
    bool TrySend(string commandText);
}

