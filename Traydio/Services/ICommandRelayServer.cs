namespace Traydio.Services;

/// <summary>
/// Receives relayed commands for the primary instance.
/// </summary>
public interface ICommandRelayServer
{
    /// <summary>
    /// Starts listening for incoming commands.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops listening for incoming commands.
    /// </summary>
    void Stop();
}

