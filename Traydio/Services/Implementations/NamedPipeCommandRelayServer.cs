using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Traydio.Commands;

namespace Traydio.Services.Implementations;

public sealed class NamedPipeCommandRelayServer(IStationRepository stationRepository, ICommandTextRouter commandTextRouter) : ICommandRelayServer, IDisposable
{
    private const string _PIPE_NAME = "Traydio.CommandRelay.v1";

    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public void Start()
    {
        if (_listenerTask is not null || !stationRepository.Communication.EnableNamedPipeRelay)
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
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _PIPE_NAME,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    commandTextRouter.TryDispatch(line);
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
        }
    }
}
