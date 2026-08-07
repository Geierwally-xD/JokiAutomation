using System;
using System.Windows;
using System.Windows.Threading;

namespace CanonRemoteControl
{
    public partial class StatusOverlay : Window
    {
        private readonly DispatcherTimer _hideTimer;

        public StatusOverlay()
        {
            InitializeComponent();

            Loaded += StatusOverlay_Loaded;

            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _hideTimer.Tick += HideTimer_Tick;

            Visibility = Visibility.Collapsed;
        }

        private void StatusOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            Left = SystemParameters.PrimaryScreenWidth - ActualWidth - 200;
            Top = 50;
        }

        public void ShowStatus(string message, bool persistent = false)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;

                if (message.StartsWith("FEHLER:", StringComparison.OrdinalIgnoreCase))
                {
                    StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
                }
                else if (message.StartsWith("WARNUNG:", StringComparison.OrdinalIgnoreCase))
                {
                    StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0));
                }
                else
                {
                    StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                }

                Visibility = Visibility.Visible;

                _hideTimer.Stop();
                if (!persistent)
                {
                    _hideTimer.Start();
                }
            });
        }

        public void HideStatus()
        {
            Dispatcher.Invoke(() =>
            {
                Visibility = Visibility.Collapsed;
                _hideTimer.Stop();
            });
        }

        private void HideTimer_Tick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            Visibility = Visibility.Collapsed;
        }
    }
}
