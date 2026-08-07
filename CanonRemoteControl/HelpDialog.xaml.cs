using System.Reflection;
using System.Windows;
using System; // <-- Add this line

namespace CanonRemoteControl
{
    public partial class HelpDialog : Window
    {
        public HelpDialog()
        {
            InitializeComponent();
            LoadVersion();
        }

        private void LoadVersion()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionTextBlock.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
