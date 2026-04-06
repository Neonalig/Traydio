using System.Collections.Generic;
using Traydio.Commands;

namespace Traydio.Services.Implementations;

public sealed class CommandRelayCoordinator(
    IEnumerable<ICommandRelayClient> relayClients,
    IEnumerable<ICommandRelayServer> relayServers,
    ICommandTextRouter commandTextRouter
) : ICommandRelayCoordinator
{
    public bool TryRelayToPrimary(string commandText)
    {
        TraydioTrace.Debug("RelayCoordinator", "Attempting relay to primary: " + commandText);
        foreach (var relayClient in relayClients)
        {
            if (relayClient.TrySend(commandText))
            {
                TraydioTrace.Info("RelayCoordinator", "Relayed command via " + relayClient.Name + ".");
                return true;
            }
        }

        TraydioTrace.Warn("RelayCoordinator", "Failed to relay command to primary.");
        return false;
    }

    public bool DispatchLocal(string commandText)
    {
        var dispatched = commandTextRouter.TryDispatch(commandText);
        TraydioTrace.Debug("RelayCoordinator", "Local dispatch result=" + dispatched + " command=" + commandText);
        return dispatched;
    }

    public void StartPrimaryRelay()
    {
        foreach (var relayServer in relayServers)
        {
            TraydioTrace.Debug("RelayCoordinator", "Starting relay server: " + relayServer.GetType().Name);
            relayServer.Start();
        }
    }

    public void StopPrimaryRelay()
    {
        foreach (var relayServer in relayServers)
        {
            TraydioTrace.Debug("RelayCoordinator", "Stopping relay server: " + relayServer.GetType().Name);
            relayServer.Stop();
        }
    }
}

