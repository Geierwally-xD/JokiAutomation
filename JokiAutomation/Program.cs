using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JokiAutomation
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// 
        private static Form1 JA;
        [STAThread]
        static void Main(string[] args)
        {
            Application.SetCompatibleTextRenderingDefault(false);
            JA = new Form1();
            if (args.Length > 0)
            {
                JA.CommandInterpreter(Environment.GetCommandLineArgs());
                Application.Exit();
            }
            else
            {
                Application.EnableVisualStyles();
                Application.Run(JA);
            }
        }
    }
}
