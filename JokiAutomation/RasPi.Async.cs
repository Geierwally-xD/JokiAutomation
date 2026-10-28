using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace JokiAutomation
{
    partial class RasPi
    {
        private static int _activeRasPiCommands;

        /// <summary>
        /// Executes a Raspberry Pi command over SSH and awaits full completion
        /// (connect, execute, read exit status, stdout/stderr).
        /// </summary>
        public async Task RasPiExecuteAsync(
            int command,
            int id,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            Stopwatch stopwatch = Stopwatch.StartNew();

            int activeCommands = Interlocked.Increment(ref _activeRasPiCommands);

            LogRasPiMessage(
                $"JokiAutomation\n[RASPI_ASYNC_ENTER] Active={activeCommands} Command={command} Position={id} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");

            if (activeCommands > 1)
            {
                LogRasPiMessage(
                    $"JokiAutomation\n[RASPI_CONCURRENCY_WARNING] Active={activeCommands} Command={command} Position={id} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            }

            LogRasPiMessage(
                $"JokiAutomation\n[RASPI_ASYNC_BEGIN] Command={command} Position={id} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            LogRasPiMessage(
                $"JokiAutomation\n[RASPI_REQUEST] Command={command} ID={id} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string ipAddress = ResolveRaspberryPiIpAddress();
                if (string.IsNullOrWhiteSpace(ipAddress))
                {
                    throw new InvalidOperationException("RaspberryPi_Main IP nicht in Network.cfg gefunden.");
                }

                string username = ResolveRaspberryPiUsername();
                string password = ResolveRaspberryPiPassword();
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    throw new InvalidOperationException("Raspberry Pi Login-Daten fehlen (RaspberryPi.cfg).");
                }

                string escapedPassword = password.Replace("'", "'\"'\"'");
                string remoteCommand = "echo '" + escapedPassword + "' | sudo -S /home/pi/JokiAutomation/RasPiAutomation " + command + " " + id;

                LogRasPiMessage(
                    $"JokiAutomation\n[RASPI_SSH_SEND] Host='{ipAddress}' Command={command} ID={id}");

                RasPiCommandExecutionResult result = await _commandExecutor.ExecuteAsync(
                    ipAddress,
                    22,
                    username,
                    password,
                    remoteCommand,
                    effectiveTimeout,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(result?.StandardOutput))
                {
                    LogRasPiMessage(result.StandardOutput);
                }

                if (!string.IsNullOrWhiteSpace(result?.StandardError))
                {
                    LogRasPiMessage(result.StandardError);
                }

                string exitText = result?.ExitCode.HasValue == true
                    ? result.ExitCode.Value.ToString()
                    : "<missing>";

                LogRasPiMessage(
                    $"JokiAutomation\n[RASPI_SSH_RESULT] Command={command} ID={id} ExitCode={exitText}");

                if (result == null || !result.ExitCode.HasValue)
                {
                    throw new RasPiCommandFailedException(command, id, "RasPi command returned no exit status.");
                }

                if (result.ExitCode.Value != 0)
                {
                    throw new RasPiCommandFailedException(command, id, result.ExitCode.Value);
                }

                LogRasPiMessage(
                    $"JokiAutomation\n[RASPI_ASYNC_END] Command={command} Position={id} ExitCode=0 DurationMs={stopwatch.ElapsedMilliseconds} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            }
            catch (OperationCanceledException)
            {
                LogRasPiMessage(
                    $"RasPiExecuteAsync CANCELED: command={command}, id={id}, elapsed={stopwatch.ElapsedMilliseconds}ms");
                throw;
            }
            catch (Exception ex)
            {
                string exitCodePart = ex is RasPiCommandFailedException failed && failed.ExitCode.HasValue
                    ? $" ExitCode={failed.ExitCode.Value}"
                    : string.Empty;

                LogRasPiMessage(
                    $"JokiAutomation\n[RASPI_ASYNC_FAIL] Command={command} Position={id}{exitCodePart} Exception={ex.GetType().Name} Message={ex.Message} DurationMs={stopwatch.ElapsedMilliseconds} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
                LogRasPiMessage(
                    $"RasPiExecuteAsync ERROR: {ex.GetType().Name} - {ex.Message}");
                throw;
            }
            finally
            {
                stopwatch.Stop();

                activeCommands = Interlocked.Decrement(ref _activeRasPiCommands);

                if (activeCommands < 0)
                {
                    LogRasPiMessage(
                        $"JokiAutomation\n[RASPI_ACTIVE_COUNTER_ERROR] Active={activeCommands} Command={command} Position={id} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
                }

                LogRasPiMessage(
                    $"JokiAutomation\n[RASPI_ASYNC_EXIT] Active={activeCommands} Command={command} Position={id} DurationMs={stopwatch.ElapsedMilliseconds} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            }
        }

        /// <summary>
        /// Executes the productive Raspberry Pi reset sequence with bounded SSH steps.
        /// The reset keeps the hardware reset actions required by the current workflow,
        /// but intentionally does not perform any named-process kill or service stop.
        /// TODO: kontrollierten RasPiAutomation-Service-/PID-basierten Stop separat implementieren.
        /// </summary>
        public async Task RasPiResetAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            Stopwatch stopwatch = Stopwatch.StartNew();

            await _rasPiResetSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _rasPiForm?._logDat?.sendInfoMessage(
                    $"JokiAutomation\n[RASPI_RESET_BEGIN] PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId} TimeoutMs={(int)effectiveTimeout.TotalMilliseconds}");

                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string ipAddress = GetRaspberryPiIpFromNetwork();
                    if (string.IsNullOrWhiteSpace(ipAddress))
                    {
                        throw new InvalidOperationException("RaspberryPi_Main IP nicht in Network.cfg gefunden.");
                    }

                    string username = _rasPiConfig[1];
                    string password = _rasPiConfig[2];
                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        throw new InvalidOperationException("Raspberry Pi Login-Daten fehlen (RaspberryPi.cfg).");
                    }

                    string[] resetSteps = new[]
                    {
                        "gpio export 5 out",
                        "gpio -g write 5 0",
                        "gpio export 6 out",
                        "gpio -g write 6 0",
                        "gpio export 7 out",
                        "gpio -g write 7 0",
                        "gpio export 13 out",
                        "gpio -g write 13 0",
                        "gpio export 19 out",
                        "gpio -g write 19 0",
                        "gpio export 26 out",
                        "gpio -g write 26 0",
                        "gpio export 16 out",
                        "gpio -g write 16 0",
                        "gpio export 20 out",
                        "gpio -g write 20 0",
                        "gpio export 21 out",
                        "gpio -g write 21 0",
                        "stty -F /dev/ttyUSB0 9600 raw -echo",
                        "echo -n '(MX*:RES!)' > /dev/ttyUSB0"
                    };

                    var pauth = new PasswordAuthenticationMethod(username, password);
                    var connectionInfo = new ConnectionInfo(ipAddress, 22, username, pauth)
                    {
                        Timeout = effectiveTimeout
                    };

                    using (var sshClient = new SshClient(connectionInfo))
                    {
                        sshClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
                        sshClient.Connect();

                        foreach (string stepCommand in resetSteps)
                        {
                            string stepName = GetResetStepName(stepCommand);
                            ExecuteResetStep(sshClient, stepName, stepCommand, effectiveTimeout, cancellationToken);
                        }

                        sshClient.Disconnect();
                    }
                }, cancellationToken).ConfigureAwait(false);

                _rasPiForm?._logDat?.sendInfoMessage(
                    $"JokiAutomation\n[RASPI_RESET_END] Result=Success DurationMs={stopwatch.ElapsedMilliseconds} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            }
            catch (OperationCanceledException)
            {
                _rasPiForm?._logDat?.sendInfoMessage(
                    $"JokiAutomation\n[RASPI_RESET_FAIL] ErrorType=OperationCanceledException Error=Reset cancelled DurationMs={stopwatch.ElapsedMilliseconds}");
                throw;
            }
            catch (Exception ex)
            {
                _rasPiForm?._logDat?.sendInfoMessage(
                    $"JokiAutomation\n[RASPI_RESET_FAIL] ErrorType={ex.GetType().Name} Error={ex.Message} DurationMs={stopwatch.ElapsedMilliseconds}");
                throw;
            }
            finally
            {
                stopwatch.Stop();
                _rasPiResetSemaphore.Release();
            }
        }

        private static string GetResetStepName(string stepCommand)
        {
            switch (stepCommand)
            {
                case "gpio export 5 out":
                    return "GPIO_5_EXPORT";
                case "gpio -g write 5 0":
                    return "GPIO_5_LOW";
                case "gpio export 6 out":
                    return "GPIO_6_EXPORT";
                case "gpio -g write 6 0":
                    return "GPIO_6_LOW";
                case "gpio export 7 out":
                    return "GPIO_7_EXPORT";
                case "gpio -g write 7 0":
                    return "GPIO_7_LOW";
                case "gpio export 13 out":
                    return "GPIO_13_EXPORT";
                case "gpio -g write 13 0":
                    return "GPIO_13_LOW";
                case "gpio export 19 out":
                    return "GPIO_19_EXPORT";
                case "gpio -g write 19 0":
                    return "GPIO_19_LOW";
                case "gpio export 26 out":
                    return "GPIO_26_EXPORT";
                case "gpio -g write 26 0":
                    return "GPIO_26_LOW";
                case "gpio export 16 out":
                    return "GPIO_16_EXPORT";
                case "gpio -g write 16 0":
                    return "GPIO_16_LOW";
                case "gpio export 20 out":
                    return "GPIO_20_EXPORT";
                case "gpio -g write 20 0":
                    return "GPIO_20_LOW";
                case "gpio export 21 out":
                    return "GPIO_21_EXPORT";
                case "gpio -g write 21 0":
                    return "GPIO_21_LOW";
                case "stty -F /dev/ttyUSB0 9600 raw -echo":
                    return "TTYUSB0_CONFIG";
                case "echo -n '(MX*:RES!)' > /dev/ttyUSB0":
                    return "TTYUSB0_RESET";
                default:
                    return stepCommand;
            }
        }

        private void ExecuteResetStep(SshClient sshClient, string stepName, string commandText, TimeSpan stepTimeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\n[RASPI_RESET_STEP_BEGIN] Step={stepName}");

            using (SshCommand cmd = sshClient.CreateCommand(commandText))
            {
                cmd.CommandTimeout = stepTimeout;
                cmd.Execute();

                int exit = cmd.ExitStatus.HasValue ? cmd.ExitStatus.Value : -1;
                string stderr = cmd.Error;

                if (exit != 0)
                {
                    _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\n[RASPI_RESET_STEP_FAIL] Step={stepName} ExitCode={exit} Error={stderr}");
                    throw new InvalidOperationException($"RasPi reset step '{stepName}' failed (exit={exit})");
                }

                _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\n[RASPI_RESET_STEP_END] Step={stepName} ExitCode=0");
            }
        }

        public async Task<bool> ShutdownRaspberryPiAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string ipAddress = GetRaspberryPiIpFromNetwork();
                if (string.IsNullOrWhiteSpace(ipAddress))
                {
                    _rasPiForm?._logDat?.sendInfoMessage("JokiAutomation\nAusschaltsequenz: RaspberryPi_Main IP nicht in Network.cfg gefunden.");
                    return false;
                }

                string username = _rasPiConfig[1];
                string password = _rasPiConfig[2];
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    _rasPiForm?._logDat?.sendInfoMessage("JokiAutomation\nAusschaltsequenz: RaspberryPi Login-Daten fehlen (RaspberryPi.cfg).");
                    return false;
                }

                string escapedPassword = password.Replace("'", "'\"'\"'");
                string commandText = $"echo '{escapedPassword}' | sudo -S shutdown -h now";

                var pauth = new PasswordAuthenticationMethod(username, password);
                var connectionInfo = new ConnectionInfo(ipAddress, 22, username, pauth)
                {
                    Timeout = effectiveTimeout
                };

                try
                {
                    using (var sshClient = new SshClient(connectionInfo))
                    {
                        sshClient.Connect();
                        var cmd = sshClient.CreateCommand(commandText);
                        cmd.CommandTimeout = effectiveTimeout;
                        cmd.Execute();

                        int exit = cmd.ExitStatus.HasValue ? cmd.ExitStatus.Value : -1;
                        string stderr = cmd.Error;

                        sshClient.Disconnect();

                        if (exit != 0)
                        {
                            _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: Raspberry Pi shutdown fehlgeschlagen (exit={exit}) {stderr}");
                            return false;
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: Raspberry Pi shutdown Fehler: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        public async Task<bool> SyncDateTimeWithWindowsAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string ipAddress = GetRaspberryPiIpFromNetwork();
                if (string.IsNullOrWhiteSpace(ipAddress))
                {
                    _rasPiForm?._logDat?.sendInfoMessage("JokiAutomation\nInitialisierung: RaspberryPi_Main IP nicht in Network.cfg gefunden.");
                    return false;
                }

                string username = _rasPiConfig[1];
                string password = _rasPiConfig[2];
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    _rasPiForm?._logDat?.sendInfoMessage("JokiAutomation\nInitialisierung: RaspberryPi Login-Daten fehlen (RaspberryPi.cfg).");
                    return false;
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string escapedPassword = password.Replace("'", "'\"'\"'");
                string commandText = $"echo '{escapedPassword}' | sudo -S date -s \"{timestamp}\" && echo '{escapedPassword}' | sudo -S hwclock -w";

                _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\nInitialisierung: Übernehme Windows-Zeit auf Raspberry Pi ({ipAddress})...");

                var pauth = new PasswordAuthenticationMethod(username, password);
                var connectionInfo = new ConnectionInfo(ipAddress, 22, username, pauth)
                {
                    Timeout = TimeSpan.FromSeconds(20)
                };

                using (var sshClient = new SshClient(connectionInfo))
                {
                    sshClient.Connect();
                    var cmd = sshClient.CreateCommand(commandText);
                    cmd.CommandTimeout = TimeSpan.FromSeconds(20);
                    cmd.Execute();

                    int exit = cmd.ExitStatus.HasValue ? cmd.ExitStatus.Value : -1;
                    if (exit != 0)
                    {
                        _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\nInitialisierung: Zeit-Sync fehlgeschlagen (exit={exit}) {cmd.Error}");
                        sshClient.Disconnect();
                        return false;
                    }

                    sshClient.Disconnect();
                }

                _rasPiForm?._logDat?.sendInfoMessage($"JokiAutomation\nInitialisierung: Raspberry Pi Zeit auf {timestamp} gesetzt.");
                return true;
            }, cancellationToken);
        }

        /// <summary>
        /// Downloads a single remote text file via SFTP and returns its content.
        /// Used by hardware regression tests to read logs such as AudioControl.log
        /// without introducing a second independent SSH stack.
        /// </summary>
        public async Task<string> DownloadRemoteTextFileAsync(string remoteFilePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(remoteFilePath))
            {
                throw new ArgumentException("remoteFilePath must not be empty.", nameof(remoteFilePath));
            }

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string ipAddress = ResolveRaspberryPiIpAddress();
                string username = ResolveRaspberryPiUsername();
                string password = ResolveRaspberryPiPassword();

                if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    throw new InvalidOperationException("Raspberry Pi connection settings (IP/username/password) are incomplete.");
                }

                var auth = new PasswordAuthenticationMethod(username, password);
                var connectionInfo = new ConnectionInfo(ipAddress, 22, username, auth)
                {
                    Timeout = TimeSpan.FromSeconds(20)
                };

                using (var sftp = new SftpClient(connectionInfo))
                {
                    sftp.Connect();

                    if (!sftp.Exists(remoteFilePath))
                    {
                        sftp.Disconnect();
                        throw new FileNotFoundException($"Remote file not found: {remoteFilePath}", remoteFilePath);
                    }

                    using (var stream = new MemoryStream())
                    {
                        sftp.DownloadFile(remoteFilePath, stream);
                        sftp.Disconnect();
                        stream.Position = 0;
                        using (var reader = new StreamReader(stream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads Raspberry Pi log files via SFTP into a local directory.
        /// </summary>
        public async Task<int> DownloadRaspberryPiLogsAsync(string localDirectory, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(localDirectory))
                {
                    return 0;
                }

                string ipAddress = GetRaspberryPiIpFromNetwork();
                string username = _rasPiConfig[1];
                string password = _rasPiConfig[2];

                if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    _rasPiForm?._logDat?.sendInfoMessage("JokiAutomation\nAusschaltsequenz: RasPi Log-Export übersprungen (SSH-Daten unvollständig).");
                    return 0;
                }

                Directory.CreateDirectory(localDirectory);

                var auth = new PasswordAuthenticationMethod(username, password);
                var connectionInfo = new ConnectionInfo(ipAddress, 22, username, auth)
                {
                    Timeout = TimeSpan.FromSeconds(20)
                };

                var downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var remoteCandidates = new[]
                {
                    "/home/pi/JokiAutomation/log/AudioControl.log",
                    "/home/pi/JokiAutomation/log/AudioControl.log.1",
                    "/home/pi/JokiAutomation/log/AudioControl.log.2",
                    "/tmp/gdbserver-2345.log",
                    "/tmp/gdbserver-2345.log.1",
                    "/tmp/gdbserver-2345.log.2"
                };

                int copiedCount = 0;

                using (var sftp = new SftpClient(connectionInfo))
                {
                    sftp.Connect();

                    foreach (string remoteFile in remoteCandidates)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!sftp.Exists(remoteFile))
                        {
                            continue;
                        }

                        string fileName = Path.GetFileName(remoteFile);
                        if (!downloaded.Add(fileName))
                        {
                            continue;
                        }

                        string localFilePath = Path.Combine(localDirectory, "RasPi_" + fileName);
                        using (var stream = File.Create(localFilePath))
                        {
                            sftp.DownloadFile(remoteFile, stream);
                        }

                        copiedCount++;
                    }

                    sftp.Disconnect();
                }

                return copiedCount;
            }, cancellationToken);
        }

        /// <summary>
        /// Clears (truncates) Raspberry Pi log files via SFTP so only data from the
        /// next run accumulates. Files are truncated to zero bytes instead of being
        /// deleted so the remote logging process can keep writing to the same handle.
        /// </summary>
        public async Task<int> ClearRaspberryPiLogsAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string ipAddress = GetRaspberryPiIpFromNetwork();
                string username = _rasPiConfig[1];
                string password = _rasPiConfig[2];

                if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    _rasPiForm?._logDat?.sendInfoMessage("JokiAutomation\nAusschaltsequenz: RasPi Log-Bereinigung übersprungen (SSH-Daten unvollständig).");
                    return 0;
                }

                var auth = new PasswordAuthenticationMethod(username, password);
                var connectionInfo = new ConnectionInfo(ipAddress, 22, username, auth)
                {
                    Timeout = TimeSpan.FromSeconds(20)
                };

                var remoteCandidates = new[]
                {
                    "/home/pi/JokiAutomation/log/AudioControl.log",
                    "/home/pi/JokiAutomation/log/AudioControl.log.1",
                    "/home/pi/JokiAutomation/log/AudioControl.log.2",
                    "/tmp/gdbserver-2345.log",
                    "/tmp/gdbserver-2345.log.1",
                    "/tmp/gdbserver-2345.log.2"
                };

                int clearedCount = 0;

                using (var sftp = new SftpClient(connectionInfo))
                {
                    sftp.Connect();

                    foreach (string remoteFile in remoteCandidates)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!sftp.Exists(remoteFile))
                        {
                            continue;
                        }

                        using (var stream = sftp.OpenWrite(remoteFile))
                        {
                            stream.SetLength(0);
                        }

                        clearedCount++;
                    }

                    sftp.Disconnect();
                }

                return clearedCount;
            }, cancellationToken);
        }
    }
}
