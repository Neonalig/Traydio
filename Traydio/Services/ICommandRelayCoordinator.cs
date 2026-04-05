namespace Traydio.Services;

public interface ICommandRelayCoordinator
{
    bool TryRelayToPrimary(string commandText);

    bool DispatchLocal(string commandText);

    void StartPrimaryRelay();

    void StopPrimaryRelay();
}

