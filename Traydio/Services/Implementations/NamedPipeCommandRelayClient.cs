using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Traydio.Services.Implementations;

public sealed class NamedPipeCommandRelayClient(IStationRepository stationRepository) : ICommandRelayClient
{
    private const string _PIPE_NAME = "Traydio.CommandRelay.v1";

    public string Name => "named-pipe";

    public bool TrySend(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText) || !stationRepository.Communication.EnableNamedPipeRelay)
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                _PIPE_NAME,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            client.Connect(500);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: false);
            writer.WriteLine(commandText);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}


