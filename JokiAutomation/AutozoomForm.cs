using System;
using System.Windows.Forms;

namespace JokiAutomation
{
    public partial class Autozoom : Form
    {
        private readonly Form1 _AZForm;

        public Autozoom(Form1 winForm)
        {
            InitializeComponent();
            _AZForm = winForm ?? throw new ArgumentNullException(nameof(winForm));
        }

        private void zoomValue_TextChanged(object sender, EventArgs e)
        {
            // Leere Eingabe ignorieren
            if (string.IsNullOrWhiteSpace(zoomValue.Text))
                return;

            // Versuche zu parsen
            if (!byte.TryParse(zoomValue.Text, out byte num_var))
            {
                zoomValue.Text = "";
                _AZForm?._logDat?.sendInfoMessage("JokiAutomation\nUngültiges Zahlenformat in Autozoom\n");
                FocusSafely(zoomValue);
                return;
            }

            // Bereichsprüfung (0-100)
            if (num_var > 100)
            {
                zoomValue.Text = "";
                _AZForm?._logDat?.sendInfoMessage("JokiAutomation\nWert muss zwischen 0 und 100 liegen\n");
                FocusSafely(zoomValue);
                return;
            }

            // Wert setzen
            _AZForm.setZoomValue(num_var);
        }

        private void buttonZoom_Click(object sender, EventArgs e)
        {
            _AZForm?.moveZoom();
        }

        /// <summary>
        /// Setzt den Fokus auf ein Control, wenn es verfügbar ist
        /// </summary>
        private void FocusSafely(Control control)
        {
            if (control != null && !control.IsDisposed && control.CanFocus)
                control.Focus();
        }
    }
}
