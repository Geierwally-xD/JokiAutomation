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
using System.Diagnostics;

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
           PuttyRequestRasPi(0,0);
        }

        // raspberry pi request over Renci ssh.Net
        public void rasPiExecute(int command, int ID)
        {
            _commandString = command.ToString();
            _idString = ID.ToString();
            int commandLineInstances = 0;
            for (UInt16 i = 0; i < 5; i++)
            {
                _threadResultString[i] = "";
            }
            try
            {
                // count running command line instances of JoKiAutomation
                Process[] localByName = Process.GetProcessesByName("JoKiAutomation");
                for(int i = 0;i< localByName.Length; i++) 
                {
                   if(localByName[i].MainWindowTitle == "")
                    {
                        commandLineInstances++;
                    }
                }
                if (commandLineInstances < 2) // maximum one instance of JoKiAutomation from command line allowed 
                {
                    if (_threadResultString[0] != "")
                    {
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
                    }

                    _RasPiThread = new Thread(new ThreadStart(rasPiThreadStart));
                    _RasPiThread.SetApartmentState(ApartmentState.STA);
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

        // raspberry pi request over putty command line
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

        public void PuttyRequestRasPi(int command, int ID)
        {
            string cmd = null;
            _commandString = command.ToString();
            _idString = ID.ToString();
            m_szFeedback = "Feedback from: " + _rasPiConfig[0] + "\r\n";
            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = _rasPiConfig[3], // A const or a readonly string that points to the plink executable
                Arguments = String.Format("-ssh {0}@{1} -pw {2}", _rasPiConfig[1] /*userName*/, _rasPiConfig[0] /*remoteHost*/, _rasPiConfig[2] /*password*/),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process p = Process.Start(psi);

            m_objLock = new Object();
            m_blnDoRead = true;

            AsyncReadFeedback(p.StandardOutput); // start the async read of stdout
            AsyncReadFeedback(p.StandardError); // start the async read of stderr

            StreamWriter strw = p.StandardInput;
            if (command > 0)
            {
                _rasPiForm._logDat.sendInfoMessage("start Raspberry Pi RasPi-Automation-application over Putty " + _commandString + " " + _idString);
                cmd = "sudo nice --15 remote-debugging/RasPiAutomation " + _commandString + " " + _idString;
            }
            else
            {
                _rasPiForm._logDat.sendInfoMessage("reset Raspberry Pi RasPi-Automation-application over Putty ");
                cmd = "killall -SIGKILL RasPiAutomation ";
            }
            strw.WriteLine(cmd); // send commands 
            strw.WriteLine("exit"); // send exit command at the end

            p.WaitForExit(); // block thread until remote operations are done
            _rasPiForm._logDat.sendInfoMessage(m_szFeedback);
        }

        // trhread helper method for PuttyRequestRasPi
        public void AsyncReadFeedback(StreamReader strr)
        {
            Thread trdr = new Thread(new ParameterizedThreadStart(__ctReadFeedback));
            trdr.Start(strr);
        }
        // trhread helper method for PuttyRequestRasPi
        private void __ctReadFeedback(Object objStreamReader)
        {
            StreamReader strr = (StreamReader)objStreamReader;
            string line;
            while (!strr.EndOfStream && m_blnDoRead)
            {
                line = strr.ReadLine();
                // lock the feedback buffer (since we don't want some messy stdout/err mix string in the end)
                lock (m_objLock) { m_szFeedback += line + "\r\n"; }
            }
        }

        public Thread _RasPiThread = null;
        private static string _commandString = null;
        private static string _idString = null;
        private Form1 _rasPiForm;
        private static String[] _threadResultString = new String[5];
        private string _JokiAutomationPath = Environment.GetEnvironmentVariable("JokiAutomation");
        private String m_szFeedback; // hold feedback data
        private Object m_objLock;    // lock object
        private Boolean m_blnDoRead; // boolean value keeping up the read (may be used to interrupt the reading process)
        static private string[] _rasPiConfig = { "192.168.178.70", "pi", "raspberry", "C:/Program Files/PuTTY/plink" }; // default login data
    }
}
