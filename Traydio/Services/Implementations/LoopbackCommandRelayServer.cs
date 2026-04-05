using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Commands;

namespace Traydio.Services.Implementations;

public sealed class LoopbackCommandRelayServer : ICommandRelayServer, IDisposable
{
    private readonly IStationRepository _stationRepository;
    private readonly ICommandTextRouter _commandTextRouter;

    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public LoopbackCommandRelayServer(IStationRepository stationRepository, ICommandTextRouter commandTextRouter)
    {
        _stationRepository = stationRepository;
        _commandTextRouter = commandTextRouter;
    }

    public void Start()
    {
        if (_listenerTask is not null || !_stationRepository.Communication.EnableLoopbackRelay)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // best effort shutdown for background listener
        }

        _cts?.Dispose();
        _cts = null;
        _listenerTask = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        var address = ResolveAddress(_stationRepository.Communication.LoopbackHost);
        var port = _stationRepository.Communication.LoopbackPort;

        try
        {
            using var listener = new TcpListener(address, port);
            listener.Start();

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _commandTextRouter.TryDispatch(line);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    client?.Dispose();
                }
            }
        }
        catch
        {
            // Start failures are non-fatal; named pipe relay is still available.
        }
    }

    private static IPAddress ResolveAddress(string host)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        return IPAddress.TryParse(host, out var parsed) ? parsed : IPAddress.Loopback;
    }
}

