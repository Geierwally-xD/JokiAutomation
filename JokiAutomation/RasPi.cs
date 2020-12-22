using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Renci.SshNet.Common;
using System.Threading;
using System.Windows.Forms;
using System.IO;

namespace JokiAutomation
{
    class RasPi
    {

        public void initRasPi(Form1 winForm)
        {
            _rasPiForm = winForm;
            if (File.Exists(_JokiAutomationPath + "RaspberryPi.cfg"))
            {
                _rasPiConfig = System.IO.File.ReadAllLines(_JokiAutomationPath + "RaspberryPi.cfg");
            }
            //System.IO.File.WriteAllLines(_JokiAutomationPath + "RaspberryPi.cfg", _rasPiConfig);
        }


        private static void HandleKeyEvent(Object sender, Renci.SshNet.Common.AuthenticationPromptEventArgs e)
        {
            foreach (Renci.SshNet.Common.AuthenticationPrompt prompt in e.Prompts)
            {
                if (prompt.Request.IndexOf("Password:", StringComparison.InvariantCultureIgnoreCase) != -1)
                {
                    prompt.Response = _rasPiConfig[2];
                }
            }
        }

        public void rasPiStop()
        {
            SshClient sshClient;
            SshCommand sshConsole;
            _rasPiForm._logDat.sendInfoMessage("stop RasPi-Automation-application");
            for (UInt16 i = 0; i < 5; i++)
            {
                _threadResultString[i] = "";
            }
            try
            {

                KeyboardInteractiveAuthenticationMethod keybAuth = new KeyboardInteractiveAuthenticationMethod(_rasPiConfig[1]);
                PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(_rasPiConfig[1], _rasPiConfig[2]);
                keybAuth.AuthenticationPrompt += new EventHandler<Renci.SshNet.Common.AuthenticationPromptEventArgs>(HandleKeyEvent);
                ConnectionInfo connectionInfo = new ConnectionInfo(_rasPiConfig[0], 22, _rasPiConfig[1], pauth, keybAuth);
                sshClient = new SshClient(connectionInfo);
                sshClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
                sshClient.Connect();
                sshConsole = sshClient.RunCommand("sudo ./PSD.sh 0");
                _threadResultString[1] = sshConsole.CommandText;
                _threadResultString[2] = sshConsole.Result;
                sshClient.Disconnect();
                if (_RasPiThread != null)
                {
                    _RasPiThread.Abort();
                    _RasPiThread = null;
                }
            }
            catch (Exception e)
            {
                _threadResultString[0] = "Error during stop Raspberry Pi!/n" + e.Message;
            }

        }


        public void rasPiExecute(int command, int ID)
        {
            _commandString = command.ToString();
            _idString = ID.ToString();
            for (UInt16 i = 0; i < 5; i++)
            {
                _threadResultString[i] = "";
            }
            try
            {
                if (_threadResultString[0] != "")
                {
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
                }

                _RasPiThread = new Thread(new ThreadStart(rasPiThreadStart));
                _rasPiForm._logDat.sendInfoMessage("start Raspberry Pi RasPi-Automation-application " + _commandString + " " + _idString);
                _RasPiThread.Start();
                if (_threadResultString[0] != "")
                {
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[3]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[4]);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("Error during Start Raspberry Pi!/n" + e.Message);
                if (_threadResultString[0] != "")
                {
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[3]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[4]);
                }
            }
        }


        public static void rasPiThreadStart()
        {
            SshCommand sshConsole;
            lock (_threadResultString)
            {
                try
                {
                    SshClient sshClient;
                    KeyboardInteractiveAuthenticationMethod keybAuth = new KeyboardInteractiveAuthenticationMethod(_rasPiConfig[1]);
                    PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(_rasPiConfig[1], _rasPiConfig[2]);
                    keybAuth.AuthenticationPrompt += new EventHandler<Renci.SshNet.Common.AuthenticationPromptEventArgs>(HandleKeyEvent);
                    ConnectionInfo connectionInfo = new ConnectionInfo(_rasPiConfig[0], 22, _rasPiConfig[1], pauth, keybAuth);
                    sshClient = new SshClient(connectionInfo);
                    sshClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    _threadResultString[1] = "connect ssh client to Raspberry Pi";
                    sshClient.Connect();
                    _threadResultString[2] = "ssh client connected";
                    String commandString = "sudo ./RasPiAutomation.sh " + _commandString + " " + _idString + " /dev/null";
                    sshConsole = sshClient.RunCommand(commandString);
                    _threadResultString[3] = sshConsole.CommandText;
                    _threadResultString[4] = sshConsole.Result;
                    sshClient.Disconnect();
                }
                catch (Exception e)
                {
                    _threadResultString[0] = "Error in Raspberry Pi thread!/n" + e.Message;
                }
            }
        }

        public Thread _RasPiThread = null;
        private static string _commandString = null;
        private static string _idString = null;
        private Form1 _rasPiForm;
        private static String[] _threadResultString = new String[5];
        private string _JokiAutomationPath = Environment.GetEnvironmentVariable("JokiAutomation");
        static private string[] _rasPiConfig = { "192.168.178.70", "pi", "raspberry" }; // default login data
    }
}
