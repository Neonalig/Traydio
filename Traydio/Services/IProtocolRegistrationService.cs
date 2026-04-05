namespace Traydio.Services;

public interface IProtocolRegistrationService
{
    bool IsRegistered(string scheme);

    bool Register(string scheme, out string? error);

    bool Unregister(string scheme, out string? error);
}

