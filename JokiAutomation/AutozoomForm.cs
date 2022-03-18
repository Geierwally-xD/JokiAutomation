using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JokiAutomation
{
    public partial class Autozoom : Form
    {
        public Autozoom(Form1 winForm)
        {
            InitializeComponent();
            _AZForm = winForm;
        }

        private void zoomValue_TextChanged(object sender, EventArgs e)
        {
            try
            {
                byte num_var = byte.Parse(zoomValue.Text);
                if ((num_var >= 0) && (num_var < 101))
                {
                    _AZForm.setZoomValue(num_var);
                }
                else
                {
                    zoomValue.Text = "";
                }
            }
            catch (Exception)
            {
                zoomValue.Text = "";
                _AZForm._logDat.sendInfoMessage("JokiAutomation\nFormatfehler Zahlenwert in Autozoom \n");
                zoomValue.Focus();
            }
        }

        private void buttonZoom_Click(object sender, EventArgs e)
        {
            _AZForm.moveZoom();
        }

        private static Form1 _AZForm;
    }
}
