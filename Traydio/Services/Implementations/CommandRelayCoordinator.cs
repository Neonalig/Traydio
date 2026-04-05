using System.Collections.Generic;
using Traydio.Commands;

namespace Traydio.Services.Implementations;

public sealed class CommandRelayCoordinator(
    IEnumerable<ICommandRelayClient> relayClients,
    ICommandRelayServer relayServer,
    ICommandTextRouter commandTextRouter
) : ICommandRelayCoordinator
{
    public bool TryRelayToPrimary(string commandText)
    {
        foreach (var relayClient in relayClients)
        {
            if (relayClient.TrySend(commandText))
            {
                return true;
            }
        }

        return false;
    }

    public bool DispatchLocal(string commandText)
    {
        return commandTextRouter.TryDispatch(commandText);
    }

    public void StartPrimaryRelay()
    {
        relayServer.Start();
    }

    public void StopPrimaryRelay()
    {
        relayServer.Stop();
    }
}

