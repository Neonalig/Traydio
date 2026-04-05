namespace Traydio.Services.Implementations;

public sealed class NoOpProtocolRegistrationService : IProtocolRegistrationService
{
    public bool IsRegistered(string scheme)
    {
        return false;
    }

    public bool Register(string scheme, out string? error)
    {
        error = "Protocol registration is not supported on this platform.";
        return false;
    }

    public bool Unregister(string scheme, out string? error)
    {
        error = "Protocol registration is not supported on this platform.";
        return false;
    }
}

