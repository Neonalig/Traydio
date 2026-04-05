using System;

namespace Traydio.Services.Implementations;

public sealed class CommandLineStartupCommandBridge : IStartupCommandBridge
{
    public bool TryGetCommand(string[] args, out string? commandText)
    {
        commandText = null;
        if (args.Length == 0)
        {
            return false;
        }

        if (args.Length >= 2 && string.Equals(args[0], "--cmd", StringComparison.OrdinalIgnoreCase))
        {
            commandText = string.Join(" ", args[1..]);
            return !string.IsNullOrWhiteSpace(commandText);
        }

        return false;
    }
}

