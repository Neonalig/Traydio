namespace Traydio.Services;

public interface IStartupCommandBridge
{
    bool TryGetCommand(string[] args, out string? commandText);
}

