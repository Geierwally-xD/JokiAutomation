using System;
using System.Threading;
using System.Threading.Tasks;

namespace CanonPtzCommon
{
    public sealed class PositionPollingService : IDisposable
    {
        private readonly ICanonPtzController _controller;
        private readonly int _intervalMs;
        private CancellationTokenSource _cts;
        private Task _pollingTask;

        public event EventHandler<CameraPosition> PositionUpdated;
        public event EventHandler<CameraStatus> StatusUpdated;

        public CameraPosition LastPosition { get; private set; }
        public CameraStatus LastStatus { get; private set; }

        public bool IsRunning { get; private set; }

        public PositionPollingService(ICanonPtzController controller, int intervalMs = 250)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _intervalMs = intervalMs;
        }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            _pollingTask = Task.Run(() => PollAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            _cts?.Cancel();
            IsRunning = false;

            try
            {
                _pollingTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Ignore cancellation
            }
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_controller.IsConnected)
                    {
                        var position = await _controller.GetPositionAsync();
                        if (position != null)
                        {
                            LastPosition = position;
                            PositionUpdated?.Invoke(this, position);
                        }

                        var status = await _controller.GetStatusAsync();
                        if (status != null)
                        {
                            LastStatus = status;
                            StatusUpdated?.Invoke(this, status);
                        }
                    }
                }
                catch (Exception)
                {
                    // Fehler ignorieren und weitermachen
                }

                await Task.Delay(_intervalMs, cancellationToken);
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
