using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace modtermTE
{
    internal static class ConfigurationReloadClient
    {
        public const string PipeName = "modterm-config-reload";
        private const int ConnectTimeoutMs = 250;

        public static void TrySignalReload()
        {
            _ = Task.Run(SignalReloadCore);
        }

        private static void SignalReloadCore()
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                client.Connect(ConnectTimeoutMs);
                client.WriteByte(1);
                client.Flush();
            }
            catch (TimeoutException)
            {
                // modterm is not running.
            }
            catch (IOException)
            {
                // modterm is not listening yet or closed the pipe.
            }
        }
    }
}
