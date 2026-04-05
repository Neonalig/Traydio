using System;
using System.Threading;

namespace Traydio.Services.Implementations;

public sealed class MutexInstanceGate : IInstanceGate, IDisposable
{
    private readonly Mutex _mutex = new(false, "Global\\Traydio.SingleInstance");
    private bool _ownsMutex;

    public bool TryAcquire()
    {
        if (_ownsMutex)
        {
            return true;
        }

        try
        {
            _ownsMutex = _mutex.WaitOne(0, false);
            return _ownsMutex;
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
            return true;
        }
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}

