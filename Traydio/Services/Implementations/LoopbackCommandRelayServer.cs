using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Commands;

namespace Traydio.Services.Implementations;

public sealed class LoopbackCommandRelayServer(IStationRepository stationRepository, ICommandTextRouter commandTextRouter) : ICommandRelayServer, IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public void Start()
    {
        if (_listenerTask is not null || !stationRepository.Communication.EnableLoopbackRelay)
        {
            TraydioTrace.Debug("LoopbackRelay", "Start skipped.");
            return;
        }

        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        TraydioTrace.Info("LoopbackRelay", "Listener started.");
    }

    public void Stop()
    {
        TraydioTrace.Debug("LoopbackRelay", "Stopping listener.");
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
        TraydioTrace.Info("LoopbackRelay", "Listener stopped.");
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        var address = ResolveAddress(stationRepository.Communication.LoopbackHost);
        var port = stationRepository.Communication.LoopbackPort;

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
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var dispatched = commandTextRouter.TryDispatch(line);
                        TraydioTrace.Debug("LoopbackRelay", "Received command, dispatched=" + dispatched + ": " + line);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TraydioTrace.Warn("LoopbackRelay", "Listener loop exception: " + ex.Message);
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    client?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            // Start failures are non-fatal; named pipe relay is still available.
            TraydioTrace.Warn("LoopbackRelay", "Listener start failed: " + ex.Message);
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

