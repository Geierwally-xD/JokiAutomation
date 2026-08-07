using CanonPtzCommon;
using System;
using System.Windows;
using System.Windows.Threading;

namespace CanonRemoteControl
{
    public partial class PositionOverlay : Window
    {
        private PositionPollingService _pollingService;

        public PositionOverlay()
        {
            InitializeComponent();

            Left = 10;
            Top = 10;
            Topmost = true;
            ShowInTaskbar = false;
        }

        public void StartPolling(ICanonPtzController controller)
        {
            if (_pollingService != null)
            {
                return;
            }

            _pollingService = new PositionPollingService(controller, 250);
            _pollingService.PositionUpdated += OnPositionUpdated;
            _pollingService.StatusUpdated += OnStatusUpdated;
            _pollingService.Start();
        }

        public void StopPolling()
        {
            if (_pollingService != null)
            {
                _pollingService.Stop();
                _pollingService.PositionUpdated -= OnPositionUpdated;
                _pollingService.StatusUpdated -= OnStatusUpdated;
                _pollingService.Dispose();
                _pollingService = null;
            }
        }

        private void OnPositionUpdated(object sender, CameraPosition position)
        {
            Dispatcher.Invoke(() =>
            {
                PanText.Text = $"PAN  : {position.Pan}";
                TiltText.Text = $"TILT : {position.Tilt}";
                ZoomText.Text = $"ZOOM : {position.Zoom}";
            });
        }

        private void OnStatusUpdated(object sender, CameraStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                string statusStr = status.IsMoving ? "MOVING" : "IDLE";
                StatusText.Text = $"STATUS: {statusStr}";
                StatusText.Foreground = status.IsMoving ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.Cyan;
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopPolling();
            base.OnClosed(e);
        }
    }
}
