using System;
using System.Windows;
using System.Windows.Threading;

namespace CanonRemoteControl
{
    public partial class StatusOverlay : Window
    {
        private DispatcherTimer _hideTimer;

        public StatusOverlay()
        {
            InitializeComponent();

            // Setze Position nach dem Laden
            this.Loaded += StatusOverlay_Loaded;

            _hideTimer = new DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromSeconds(3);
            _hideTimer.Tick += HideTimer_Tick;

            // Initial SICHTBAR für Tests
            this.Visibility = Visibility.Visible;
        }

        private void StatusOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            // Positioniere weiter links mit mehr Abstand vom Rand
            this.Left = SystemParameters.PrimaryScreenWidth - this.ActualWidth - 200;
            this.Top = 50;

            System.Diagnostics.Debug.WriteLine($"StatusOverlay geladen an Position: Left={this.Left}, Top={this.Top}, Width={this.ActualWidth}");
        }

        public void ShowStatus(string message, bool persistent = false)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;

                // Ändere Farbe basierend auf Fehler
                if (message.StartsWith("FEHLER:", StringComparison.OrdinalIgnoreCase))
                {
                    StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(244, 67, 54)); // Rot für Fehler
                }
                else
                {
                    StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(76, 175, 80)); // Grün für Erfolg
                }

                this.Visibility = Visibility.Visible;
                this.Activate();

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
                this.Visibility = Visibility.Collapsed;
                _hideTimer.Stop();
            });
        }

        private void HideTimer_Tick(object sender, EventArgs e)
        {
            _hideTimer.Stop();
            this.Visibility = Visibility.Collapsed;
        }
    }
}
