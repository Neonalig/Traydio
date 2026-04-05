using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Traydio.Services.Implementations;

public sealed class LoopbackCommandRelayClient : ICommandRelayClient
{
    private readonly IStationRepository _stationRepository;

    public LoopbackCommandRelayClient(IStationRepository stationRepository)
    {
        _stationRepository = stationRepository;
    }

    public string Name => "loopback";

    public bool TrySend(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText) || !_stationRepository.Communication.EnableLoopbackRelay)
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            client.Connect(_stationRepository.Communication.LoopbackHost, _stationRepository.Communication.LoopbackPort);

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


