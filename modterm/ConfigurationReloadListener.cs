using Microsoft.UI.Dispatching;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace modterm
{
    internal sealed class ConfigurationReloadListener : IDisposable
    {
        public const string PipeName = "modterm-config-reload";

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Action _requestReload;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _listenTask;

        public ConfigurationReloadListener(DispatcherQueue dispatcherQueue, Action requestReload)
        {
            _dispatcherQueue = dispatcherQueue;
            _requestReload = requestReload;
            _listenTask = Task.Run(ListenLoopAsync);
        }

        private async Task ListenLoopAsync()
        {
            var cancellationToken = _cancellationTokenSource.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken);
                    DrainClient(server);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (IOException)
                {
                    // The editor closed the signal connection early.
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    _dispatcherQueue.TryEnqueue(() => _requestReload());
                }
            }
        }

        private static void DrainClient(NamedPipeServerStream server)
        {
            var buffer = new byte[64];
            while (server.CanRead && server.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
        }
    }
}
