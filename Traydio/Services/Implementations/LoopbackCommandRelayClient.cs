using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Traydio.Services.Implementations;

public sealed class LoopbackCommandRelayClient(IStationRepository stationRepository) : ICommandRelayClient
{
    public string Name => "loopback";

    public bool TrySend(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText) || !stationRepository.Communication.EnableLoopbackRelay)
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            client.Connect(stationRepository.Communication.LoopbackHost, stationRepository.Communication.LoopbackPort);

            using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false), 1024, leaveOpen: false);
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


