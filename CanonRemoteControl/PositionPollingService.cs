using CanonPtzCommon;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CanonRemoteControl
{
    public sealed class PositionPollingService : IDisposable
    {
        private readonly ICanonPtzController _controller;
        private readonly int _intervalMs;
        private CancellationTokenSource _cts;
        private Task _pollingTask;
        private bool _isRunning;

        public event EventHandler<CameraPosition> PositionUpdated;
        public event EventHandler<CameraStatus> StatusUpdated;

        public PositionPollingService(ICanonPtzController controller, int intervalMs)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _intervalMs = intervalMs <= 0 ? 250 : intervalMs;
        }

        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            try
            {
                _cts?.Cancel();
                _pollingTask?.Wait(1000);
            }
            catch
            {
                // Best effort shutdown.
            }
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    CameraPosition position = await _controller.GetPositionAsync();

                    if (position != null)
                    {
                        PositionUpdated?.Invoke(this, position);
                    }

                    CameraStatus status = await _controller.GetStatusAsync();

                    if (status != null)
                    {
                        StatusUpdated?.Invoke(this, status);
                    }
                }
                catch
                {
                    // Polling darf die Anwendung nicht beenden.
                    // Optional später Logging ergänzen.
                }

                try
                {
                    await Task.Delay(_intervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
            _pollingTask = null;
        }
    }
}
