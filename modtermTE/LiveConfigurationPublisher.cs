using Microsoft.UI.Dispatching;
using System;

namespace modtermTE
{
    internal sealed class LiveConfigurationPublisher : IDisposable
    {
        private readonly UserConfigurationStore _store;
        private readonly DispatcherQueueTimer _timer;
        private UserAppConfiguration? _pendingConfiguration;

        public LiveConfigurationPublisher(DispatcherQueue dispatcherQueue, UserConfigurationStore store)
        {
            _store = store;
            _timer = dispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(300);
            _timer.Tick += (_, _) => PublishPending();
        }

        public void SchedulePublish(UserAppConfiguration configuration)
        {
            _pendingConfiguration = configuration;
            _timer.Stop();
            _timer.Start();
        }

        public void PublishNow(UserAppConfiguration configuration)
        {
            _timer.Stop();
            _pendingConfiguration = configuration;
            PublishPending();
        }

        private void PublishPending()
        {
            _timer.Stop();
            if (_pendingConfiguration is null)
            {
                return;
            }

            _store.Save(_pendingConfiguration);
            ConfigurationReloadClient.TrySignalReload();
        }

        public void Dispose()
        {
            _timer.Stop();
        }
    }
}
