namespace Traydio.Services;

public interface ICommandRelayClient
{
    string Name { get; }

    bool TrySend(string commandText);
}

