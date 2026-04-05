namespace Traydio.Services;

public interface IInstanceGate
{
    bool TryAcquire();
}

