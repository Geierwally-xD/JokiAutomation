using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Text;
namespace JokiAutomation
{
    static class Program
    {
        private static Form1 JA;
        private static SingleInstanceCommandServer _commandServer;
        private static ICommandDispatcher _commandDispatcher;
        private static readonly SemaphoreSlim _commandLock = new SemaphoreSlim(1, 1);
        private static readonly AsyncLocal<string> _activeRequestId = new AsyncLocal<string>();
        private static int _pendingExternalCommands;
        private static int _shutdownInProgress;

        public static bool IsShutdownInProgress
        {
            get { return Interlocked.CompareExchange(ref _shutdownInProgress, 0, 0) == 1; }
        }

        public static bool BeginShutdown()
        {
            int previous = Interlocked.CompareExchange(ref _shutdownInProgress, 1, 0);
            bool started = previous == 0;

            LogData.LogLifecycle(
                "GUI",
                "SHUTDOWN_REQUESTED",
                $"Started={started} PID={Process.GetCurrentProcess().Id}");

            return started;
        }

        [STAThread]
        static void Main(string[] args)
        {
            string[] safeArgs = args ?? new string[0];
            bool isCliMode = safeArgs.Length >= 1;
            string mode = isCliMode ? "CLI" : "GUI";

            LogData.LogLifecycle(
                mode,
                "MAIN_ENTER",
                $"Args=\"{FormatArgsForLifecycle(safeArgs)}\" Cwd=\"{EscapeForLifecycle(Environment.CurrentDirectory)}\" Exe=\"{EscapeForLifecycle(Application.ExecutablePath)}\"");

            if (TryRunShutdownTestHostMode(safeArgs, out int shutdownTestExitCode))
            {
                Environment.ExitCode = shutdownTestExitCode;
                return;
            }

            Mutex singleInstanceMutex = null;
            bool mutexAcquired = false;
            string singleInstanceMutexName = SingleInstanceCommandConstants.ResolveMutexName();

            try
            {
                LogData.LogLifecycle(mode, "MUTEX_WAIT_BEGIN", $"Name=\"{singleInstanceMutexName}\"");
                singleInstanceMutex = new Mutex(false, singleInstanceMutexName);

                try
                {
                    mutexAcquired = singleInstanceMutex.WaitOne(0, false);
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }

                LogData.LogLifecycle(mode, "MUTEX_ACQUIRED", $"Acquired={mutexAcquired}");

                if (!mutexAcquired)
                {
                    LogData.LogLifecycle("FORWARDER", "FORWARDER_INSTANCE_SELECTED", $"Args=\"{FormatArgsForLifecycle(safeArgs)}\"");

                    PipeCommandResponse forwardResult = ForwardToPrimaryInstance(safeArgs);

                    if (forwardResult?.ExitCode == CommandExitCodes.PipeConnectFailed)
                    {
                        // The mutex owner's pipe is unreachable even though the mutex is still held.
                        // The mutex only signals "a process currently owns the single-instance resource",
                        // not "a usable Primary pipe is guaranteed" - the owner may be mid-shutdown
                        // (pipe already stopped, mutex release still pending). Re-evaluate deterministically
                        // with a single bounded wait instead of surfacing a hard connect-timeout failure.
                        LogData.LogLifecycle("FORWARDER", "PRIMARY_UNREACHABLE_REEVALUATING", $"RequestId={forwardResult.RequestId}");

                        bool reacquired = singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(5), false);
                        LogData.LogLifecycle("FORWARDER", "MUTEX_REEVALUATION_RESULT", $"Reacquired={reacquired}");

                        if (reacquired)
                        {
                            mutexAcquired = true;
                        }
                    }

                    if (!mutexAcquired)
                    {
                        Environment.ExitCode = forwardResult?.ExitCode ?? CommandExitCodes.PipeConnectFailed;

                        LogData.LogLifecycle("FORWARDER", "FORWARDER_EXIT", $"RequestId={forwardResult?.RequestId} ExitCode={Environment.ExitCode}");
                        return;
                    }
                }

                LogData.LogLifecycle(mode, "PRIMARY_INSTANCE_SELECTED");

                if (isCliMode)
                {
                    Environment.ExitCode = CommandInterpreter(safeArgs);
                }
                else
                {
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.EnableVisualStyles();

                    // TODO: DLL-Check — nach Test entfernen!
                    try
                    {
                        object discovery = new BMDSwitcherAPI.CBMDSwitcherDiscovery();
                        Debug.WriteLine("DLL Check: COM-Aktivierung OK - BMDSwitcherAPI64.dll korrekt registriert.");
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(discovery);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DLL Check: COM-Aktivierung FEHLGESCHLAGEN - {ex.Message} (HRESULT: 0x{ex.HResult:X8})");
                    }

                    JA = new Form1();
                    _commandDispatcher = CreateDispatcher(JA);
                    _commandServer = new SingleInstanceCommandServer((incomingArgs, requestId, senderPid, token) =>
                        ExecuteCommandQueuedAsync(JA, incomingArgs, requestId, "PIPE", senderPid, token),
                        _commandDispatcher);
                    _commandServer.Start();

                    LogData.LogLifecycle("GUI", "APPLICATION_RUN_ENTER", "Target=Form1");
                    Application.Run(JA);
                    LogData.LogLifecycle("GUI", "APPLICATION_RUN_RETURNED", $"Target=Form1 ShutdownInProgress={IsShutdownInProgress}");
                }

                LogData.LogLifecycle(mode, "MAIN_NORMAL_END", $"ExitCode={Environment.ExitCode}");
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle(
                    mode,
                    "MAIN_EXCEPTION",
                    $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\" StackTrace=\"{EscapeForLifecycle(ex.StackTrace)}\" ExitCode={Environment.ExitCode}");

                if (safeArgs.Length >= 1)
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
                    File.AppendAllText(logPath, $"\n{DateTime.Now}: KRITISCHER FEHLER - {ex.Message}\n{ex.StackTrace}\n");
                }
                else
                {
                    MessageBox.Show($"Kritischer Fehler in Main(): {ex.Message}\n\n{ex.StackTrace}",
                                   "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                LogData.LogLifecycle(mode, "MAIN_FINALLY_ENTER", $"MutexAcquired={mutexAcquired}");

                Task dispatcherStopTask = null;
                bool dispatcherStopCompleted = false;
                string dispatcherStopState = "NotStarted";
                const int dispatcherStopTimeoutMs = 5000;

                if (_commandDispatcher != null)
                {
                    try
                    {
                        LogData.LogLifecycle(mode, "DISPATCHER_STOP_BEGIN");
                        dispatcherStopTask = _commandDispatcher.StopAsync();
                        dispatcherStopCompleted = dispatcherStopTask.Wait(TimeSpan.FromMilliseconds(dispatcherStopTimeoutMs));

                        dispatcherStopState = dispatcherStopTask == null
                            ? "None"
                            : dispatcherStopTask.Status.ToString();

                        LogData.LogLifecycle(
                            mode,
                            dispatcherStopCompleted ? "DISPATCHER_STOP_END" : "DISPATCHER_STOP_TIMEOUT",
                            dispatcherStopCompleted ? string.Empty : $"TimeoutMs={dispatcherStopTimeoutMs}");
                    }
                    catch (Exception ex)
                    {
                        dispatcherStopState = dispatcherStopTask == null
                            ? "FaultedBeforeTask"
                            : dispatcherStopTask.Status.ToString();
                        LogData.LogLifecycle(mode, "DISPATCHER_STOP_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                    }

                    if (dispatcherStopCompleted)
                    {
                        try
                        {
                            LogData.LogLifecycle(mode, "DISPATCHER_DISPOSE_BEGIN");
                            if (_commandDispatcher is IDisposable disposableDispatcher)
                            {
                                disposableDispatcher.Dispose();
                            }
                            LogData.LogLifecycle(mode, "DISPATCHER_DISPOSE_END");
                        }
                        catch (Exception ex)
                        {
                            LogData.LogLifecycle(mode, "DISPATCHER_DISPOSE_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                        }
                        finally
                        {
                            _commandDispatcher = null;
                        }
                    }
                }

                Task commandServerStopTask = null;
                bool commandServerStopCompleted = false;
                string commandServerStopState = "NotStarted";
                string commandServerListenerState = "NotStarted";
                const int pipeStopTimeoutMs = 5000;

                if (_commandServer != null)
                {
                    try
                    {
                        LogData.LogLifecycle(mode, "COMMAND_SERVER_STOP_BEGIN");
                        commandServerStopTask = _commandServer.StopAsync();
                        commandServerStopCompleted = commandServerStopTask.Wait(TimeSpan.FromMilliseconds(pipeStopTimeoutMs));

                        commandServerStopState = commandServerStopTask == null
                            ? "None"
                            : commandServerStopTask.Status.ToString();
                        commandServerListenerState = _commandServer.GetListenerTaskState();

                        LogData.LogLifecycle(
                            mode,
                            commandServerStopCompleted ? "COMMAND_SERVER_STOP_END" : "PIPE_SERVER_STOP_TIMEOUT",
                            commandServerStopCompleted ? string.Empty : $"TimeoutMs={pipeStopTimeoutMs}");
                    }
                    catch (Exception ex)
                    {
                        commandServerStopState = commandServerStopTask == null
                            ? "FaultedBeforeTask"
                            : commandServerStopTask.Status.ToString();
                        commandServerListenerState = _commandServer.GetListenerTaskState();
                        LogData.LogLifecycle(mode, "PIPE_SERVER_STOP_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                    }

                    if (commandServerStopCompleted)
                    {
                        try
                        {
                            LogData.LogLifecycle(mode, "COMMAND_SERVER_DISPOSE_BEGIN");
                            _commandServer.Dispose();
                            LogData.LogLifecycle(mode, "COMMAND_SERVER_DISPOSE_END");
                        }
                        catch (Exception ex)
                        {
                            LogData.LogLifecycle(mode, "PIPE_SERVER_DISPOSE_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                        }
                        finally
                        {
                            commandServerListenerState = _commandServer.GetListenerTaskState();
                            _commandServer = null;
                        }
                    }
                }

                if (dispatcherStopCompleted && commandServerStopCompleted && JA != null)
                {
                    try
                    {
                        LogData.LogLifecycle(mode, "FORM_DISPOSE_BEGIN", $"Target={JA.Name}");
                        JA.Dispose();
                        LogData.LogLifecycle(mode, "FORM_DISPOSE_END", $"Target={JA.Name}");
                    }
                    catch (Exception ex)
                    {
                        LogData.LogLifecycle(mode, "FORM_DISPOSE_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                    }
                    finally
                    {
                        JA = null;
                    }
                }

                bool mutexReleased = false;
                if (mutexAcquired && singleInstanceMutex != null)
                {
                    try
                    {
                        string pipeServerState = _commandServer == null ? "DisposedOrNull" : "Alive";
                        LogData.LogLifecycle(
                            mode,
                            "MUTEX_RELEASE_BEGIN",
                            $"ShutdownInProgress={IsShutdownInProgress} PipeServerState={pipeServerState} StopTaskState={commandServerStopState} ListenerTaskState={commandServerListenerState} StopCompleted={commandServerStopCompleted} DispatcherStopTaskState={dispatcherStopState} DispatcherStopCompleted={dispatcherStopCompleted}");

                        if (commandServerStopCompleted && dispatcherStopCompleted)
                        {
                            singleInstanceMutex.ReleaseMutex();
                            mutexReleased = true;
                            LogData.LogLifecycle(mode, "MUTEX_RELEASED", $"ShutdownInProgress={IsShutdownInProgress}");
                        }
                        else
                        {
                            if (Environment.ExitCode == 0)
                            {
                                Environment.ExitCode = CommandExitCodes.CommandFailed;
                            }

                            LogData.LogLifecycle(
                                mode,
                                "MUTEX_RELEASE_BLOCKED",
                                $"PipeServerStopCompleted={commandServerStopCompleted} DispatcherStopCompleted={dispatcherStopCompleted}");
                            LogData.LogLifecycle(
                                mode,
                                "SHUTDOWN_FAIL",
                                $"PipeServerStopCompleted={commandServerStopCompleted} DispatcherStopCompleted={dispatcherStopCompleted} ListenerTaskState={commandServerListenerState} DispatcherStopTaskState={dispatcherStopState} ExitCode={Environment.ExitCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogData.LogLifecycle(
                            mode,
                            "MUTEX_RELEASE_EXCEPTION",
                            $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                    }
                }

                if (singleInstanceMutex != null && mutexReleased)
                {
                    try
                    {
                        LogData.LogLifecycle(mode, "MUTEX_DISPOSE_BEGIN");
                        singleInstanceMutex.Dispose();
                        LogData.LogLifecycle(mode, "MUTEX_DISPOSED");
                    }
                    catch (Exception ex)
                    {
                        LogData.LogLifecycle(
                            mode,
                            "MUTEX_DISPOSE_EXCEPTION",
                            $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                    }
                }

                LogData.LogLifecycle(mode, "PROCESS_END_REACHED", $"ExitCode={Environment.ExitCode} ShutdownInProgress={IsShutdownInProgress}");
                LogData.LogLifecycle(mode, "MAIN_EXIT", $"ExitCode={Environment.ExitCode}");
            }
        }

        private static int CommandInterpreter(string[] args)
        {
            LogData logData = null;
            string[] safeArgs = args ?? new string[0];
            string commandName = safeArgs.Length > 0 ? safeArgs[0] : string.Empty;
            bool cliModeExitLogged = false;

            LogData.LogLifecycle("CLI", "CLI_MODE_ENTER", $"Args=\"{FormatArgsForLifecycle(safeArgs)}\"");

            try
            {
                logData = new LogData();
                logData.initLogData(null);

                logData.sendInfoMessage("=================================================");
                logData.sendInfoMessage("=== JokiAutomation Kommandozeilen-Modus START ===");
                logData.sendInfoMessage($"Argumente: {string.Join(" ", safeArgs)}");
                logData.sendInfoMessage($"Anzahl Argumente: {safeArgs.Length}");

                for (int i = 0; i < safeArgs.Length; i++)
                {
                    logData.sendInfoMessage($"  args[{i}] = '{safeArgs[i]}'");
                }

                logData.sendInfoMessage($"Arbeitsverzeichnis: {AppDomain.CurrentDomain.BaseDirectory}");
                logData.sendInfoMessage($"Build-Konfiguration: {GetBuildConfiguration()}");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Form1 form = new Form1();
                JA = form;
                LogData.LogLifecycle("CLI", "CLI_FORM_CREATED");

                _commandDispatcher = CreateDispatcher(form);
                _commandServer = new SingleInstanceCommandServer((incomingArgs, requestId, senderPid, token) =>
                    ExecuteCommandQueuedAsync(form, incomingArgs, requestId, "PIPE", senderPid, token),
                    _commandDispatcher);
                _commandServer.Start();

                logData.sendInfoMessage(">>> Form1 erstellt");
                logData.sendInfoMessage(">>> Starte Message Loop und warte auf Shown-Event...");

                bool commandExecuted = false;
                bool commandFailed = false;
                bool commandSucceeded = false;
                int commandExitCode = CommandExitCodes.Success;
                string commandError = "";

                form.Shown += async (sender, e) =>
                {
                    if (commandExecuted) return;
                    commandExecuted = true;

                    logData.sendInfoMessage(">>> Form.Shown-Event empfangen, Message Loop aktiv");
                    LogData.LogLifecycle("CLI", "CLI_FORM_SHOWN");

                    try
                    {
                        logData.sendInfoMessage(">>> Warte auf PTZ-Initialisierung (max. 15s)...");

                        int maxWaitMs = 15000;
                        int waitedMs = 0;
                        int checkIntervalMs = 200;

                        LogData.LogLifecycle("CLI", "PTZ_WAIT_BEGIN", $"MaxWaitMs={maxWaitMs}");

                        while (waitedMs < maxWaitMs)
                        {
                            bool isInitialized = form.IsPtzInitialized();

                            if (isInitialized)
                            {
                                logData.sendInfoMessage($">>> PTZ-Kamera erfolgreich initialisiert nach {waitedMs}ms");
                                break;
                            }

                            await Task.Delay(checkIntervalMs);
                            waitedMs += checkIntervalMs;

                            if (waitedMs % 1000 == 0)
                            {
                                logData.sendInfoMessage($">>> Warte auf PTZ-Initialisierung... ({waitedMs}ms)");
                            }
                        }

                        bool initialized = form.IsPtzInitialized();
                        if (!initialized)
                        {
                            logData.sendInfoMessage($">>> WARNUNG: PTZ-Kamera nicht initialisiert nach {maxWaitMs}ms");
                        }

                        LogData.LogLifecycle("CLI", "PTZ_WAIT_END", $"Initialized={initialized} WaitedMs={waitedMs}");

                        string[] formArgs = new string[safeArgs.Length + 1];
                        formArgs[0] = "JokiAutomation.exe";
                        Array.Copy(safeArgs, 0, formArgs, 1, safeArgs.Length);

                        logData.sendInfoMessage(">>> Führe Kommando aus...");
                        string cliRequestId = Guid.NewGuid().ToString("N");

                        LogData.LogLifecycle("CLI", "COMMAND_BEGIN", $"RequestId={cliRequestId} Command=\"{EscapeForLifecycle(commandName)}\"");
                        commandExitCode = await ExecuteCommandQueuedAsync(form, safeArgs, cliRequestId, "CLI", Process.GetCurrentProcess().Id, CancellationToken.None);

                        commandSucceeded = commandExitCode == CommandExitCodes.Success;
                        LogData.LogLifecycle("CLI", "COMMAND_END", $"RequestId={cliRequestId} Command=\"{EscapeForLifecycle(commandName)}\" Success={commandSucceeded} ExitCode={commandExitCode}");
                        logData.sendInfoMessage(">>> Kommando abgeschlossen");
                    }
                    catch (Exception ex)
                    {
                        logData.sendInfoMessage($"FEHLER beim Ausführen des Kommandos: {ex.Message}");
                        logData.sendInfoMessage($"StackTrace:\n{ex.StackTrace}");
                        commandFailed = true;
                        commandExitCode = CommandExitCodes.CommandFailed;
                        commandError = ex.Message;

                        LogData.LogLifecycle(
                            "CLI",
                            "COMMAND_EXCEPTION",
                            $"Command=\"{EscapeForLifecycle(commandName)}\" Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\" StackTrace=\"{EscapeForLifecycle(ex.StackTrace)}\"");
                        LogData.LogLifecycle("CLI", "COMMAND_END", $"Command=\"{EscapeForLifecycle(commandName)}\" Success=False");
                    }
                    finally
                    {
                        try
                        {
                            // Keep the message loop and Form alive while accepted work drains.
                            LogData.LogLifecycle("CLI", "CLI_TECHNICAL_DRAIN_BEGIN");
                            if (_commandDispatcher != null)
                            {
                                await _commandDispatcher.StopAsync();
                            }
                            if (_commandServer != null)
                            {
                                await _commandServer.StopAsync();
                            }
                            LogData.LogLifecycle("CLI", "CLI_TECHNICAL_DRAIN_END");
                        }
                        catch (Exception ex)
                        {
                            commandFailed = true;
                            commandExitCode = CommandExitCodes.CommandFailed;
                            commandError = ex.Message;
                            LogData.LogLifecycle("CLI", "CLI_TECHNICAL_DRAIN_FAIL",
                                $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                        }

                        logData.sendInfoMessage(">>> Schließe Anwendung...");
                        LogData.LogLifecycle("CLI", "CLI_FORM_CLOSE_REQUEST");
                        Application.Exit();
                    }
                };

                form.Hide();
                form.ShowInTaskbar = false;
                form.Opacity = 0;

                logData.sendInfoMessage(">>> Starte Application.Run() - Message Loop aktiv");
                LogData.LogLifecycle("CLI", "APPLICATION_RUN_ENTER", "Target=CliHiddenForm");
                Application.Run(form);
                LogData.LogLifecycle("CLI", "APPLICATION_RUN_RETURNED", "Target=CliHiddenForm");
                LogData.LogLifecycle("CLI", "CLI_APPLICATION_RUN_RETURNED");

                Thread.Sleep(500);

                if (commandFailed)
                {
                    logData.sendInfoMessage($"=== Kommando FEHLGESCHLAGEN: {commandError} ===");
                }
                else
                {
                    logData.sendInfoMessage("=== Kommando erfolgreich abgeschlossen ===");
                }

                logData.sendInfoMessage("=================================================");

                Environment.ExitCode = commandExitCode;
                LogData.LogLifecycle("CLI", "CLI_MODE_EXIT", $"ExitCode={Environment.ExitCode}");
                cliModeExitLogged = true;
                return commandExitCode;
            }
            catch (Exception ex)
            {
                if (logData != null)
                {
                    logData.sendInfoMessage($"KRITISCHER FEHLER: {ex.Message}");
                    logData.sendInfoMessage($"StackTrace: {ex.StackTrace}");
                }
                else
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
                    File.AppendAllText(logPath, $"\n{DateTime.Now}: FEHLER - {ex.Message}\n{ex.StackTrace}\n");
                }

                if (!cliModeExitLogged)
                {
                    LogData.LogLifecycle("CLI", "CLI_MODE_EXIT", $"ExitCode={Environment.ExitCode}");
                }
            }

            return CommandExitCodes.CommandFailed;
        }

        private static PipeCommandResponse ForwardToPrimaryInstance(string[] args)
        {
            try
            {
                using (var cts = new CancellationTokenSource(SingleInstanceCommandConstants.PipeCompletionTimeoutMs))
                {
                    var client = new SingleInstanceCommandClient();
                    PipeCommandResponse response = client.ForwardAsync(args, cts.Token).GetAwaiter().GetResult();
                    return response;
                }
            }
            catch (OperationCanceledException ex)
            {
                LogData.LogLifecycle("FORWARDER", "PIPE_FORWARD_CANCELED", $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                return new PipeCommandResponse
                {
                    RequestId = string.Empty,
                    Accepted = false,
                    Completed = false,
                    State = PipeCommandStates.Rejected,
                    ExitCode = CommandExitCodes.PipeTimeout,
                    Message = ex.Message
                };
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle("FORWARDER", "PIPE_FORWARD_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                return new PipeCommandResponse
                {
                    RequestId = string.Empty,
                    Accepted = false,
                    Completed = false,
                    State = PipeCommandStates.Rejected,
                    ExitCode = CommandExitCodes.PipeConnectFailed,
                    Message = ex.Message
                };
            }
        }

        private static async Task<int> ExecuteCommandQueuedAsync(Form1 form, string[] args, string requestId, string source, int senderPid, CancellationToken cancellationToken)
        {
            string normalizedSource = string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source;
            string[] safeArgs = args ?? new string[0];
            string argsText = FormatArgsForLifecycle(safeArgs);

            int queueDepth = Interlocked.Increment(ref _pendingExternalCommands);
            LogData.LogLifecycle(normalizedSource, "COMMAND_QUEUE_ENTER", $"RequestId={requestId} SenderPid={senderPid} Args=\"{argsText}\"");
            LogData.LogLifecycle(normalizedSource, "COMMAND_QUEUE_POSITION", $"RequestId={requestId} Position={Math.Max(0, queueDepth - 1)}");

            if (queueDepth > 1)
            {
                LogData.LogLifecycle(normalizedSource, "COMMAND_BUSY", $"RequestId={requestId} QueueDepth={queueDepth - 1}");
            }

            if (queueDepth > SingleInstanceCommandConstants.MaxQueuedCommands)
            {
                Interlocked.Decrement(ref _pendingExternalCommands);
                LogData.LogLifecycle(normalizedSource, "COMMAND_QUEUE_FULL", $"RequestId={requestId} QueueDepth={queueDepth}");
                return CommandExitCodes.QueueRejected;
            }

            bool lockAcquired = false;
            try
            {
                LogData.LogLifecycle(normalizedSource, "COMMAND_LOCK_WAIT", $"RequestId={requestId} Args=\"{argsText}\"");
                await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                lockAcquired = true;

                LogData.LogLifecycle(normalizedSource, "COMMAND_DEQUEUED", $"RequestId={requestId}");
                LogData.LogLifecycle(normalizedSource, "COMMAND_LOCK_ACQUIRED", $"RequestId={requestId}");

                int result = await ExecuteOnUiThreadAsync(form, safeArgs, requestId, normalizedSource, cancellationToken).ConfigureAwait(false);
                LogData.LogLifecycle(normalizedSource, "COMMAND_QUEUE_EXIT", $"RequestId={requestId} ExitCode={result}");
                return result;
            }
            catch (OperationCanceledException)
            {
                LogData.LogLifecycle(normalizedSource, "COMMAND_EXECUTION_CANCELED", $"RequestId={requestId}");
                return CommandExitCodes.CommandCanceled;
            }
            finally
            {
                if (lockAcquired)
                {
                    _commandLock.Release();
                    LogData.LogLifecycle(normalizedSource, "COMMAND_LOCK_RELEASED", $"RequestId={requestId}");
                }

                Interlocked.Decrement(ref _pendingExternalCommands);
            }
        }

        private static async Task<int> ExecuteOnUiThreadAsync(Form1 form, string[] safeArgs, string requestId, string source, CancellationToken cancellationToken)
        {
            if (form == null)
            {
                LogData.LogLifecycle(source, "UI_DISPATCH_FAILED", $"RequestId={requestId} Reason=FormNull");
                return CommandExitCodes.UiDispatchFailed;
            }

            if (form.IsDisposed || form.Disposing)
            {
                LogData.LogLifecycle(source, "UI_DISPATCH_FAILED", $"RequestId={requestId} Reason=FormDisposed");
                return CommandExitCodes.UiDispatchFailed;
            }

            if (!form.IsHandleCreated)
            {
                LogData.LogLifecycle(source, "UI_DISPATCH_WAIT_HANDLE", $"RequestId={requestId}");
                bool handleReady = await WaitForHandleCreatedAsync(form, cancellationToken).ConfigureAwait(false);
                if (!handleReady)
                {
                    LogData.LogLifecycle(source, "UI_DISPATCH_FAILED", $"RequestId={requestId} Reason=HandleNotCreated");
                    return CommandExitCodes.UiDispatchFailed;
                }
            }

            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                LogData.LogLifecycle(source, "UI_DISPATCH_BEGIN", $"RequestId={requestId}");
                form.BeginInvoke(new Action(() =>
                {
                    _ = ExecuteDispatchedCommandAsync(form, safeArgs, requestId, source, completion);
                }));
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle(source, "UI_DISPATCH_FAILED", $"RequestId={requestId} Reason=BeginInvokeFailed Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                return CommandExitCodes.UiDispatchFailed;
            }

            return await completion.Task.ConfigureAwait(false);
        }

        private static async Task ExecuteDispatchedCommandAsync(Form1 form, string[] safeArgs, string requestId, string source, TaskCompletionSource<int> completion)
        {
            try
            {
                LogData.LogLifecycle(source, "UI_DISPATCH_EXECUTING", $"RequestId={requestId}");
                _activeRequestId.Value = requestId;
                form._activeCommandRequestId.Value = requestId;

                string[] formArgs = new string[safeArgs.Length + 1];
                formArgs[0] = "JokiAutomation.exe";
                Array.Copy(safeArgs, 0, formArgs, 1, safeArgs.Length);

                LogData.LogLifecycle(source, "COMMAND_EXECUTION_BEGIN", $"RequestId={requestId} Args=\"{FormatArgsForLifecycle(safeArgs)}\"");
                int exitCode = await form.CommandInterpreterAsync(formArgs).ConfigureAwait(true);
                LogData.LogLifecycle(source, "COMMAND_EXECUTION_END", $"RequestId={requestId} ExitCode={exitCode}");
                LogData.LogLifecycle(source, "UI_DISPATCH_END", $"RequestId={requestId} ExitCode={exitCode}");
                completion.TrySetResult(exitCode);
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle(source, "UI_DISPATCH_FAILED", $"RequestId={requestId} Reason=ExecutionException Exception={ex.GetType().Name} Message=\"{EscapeForLifecycle(ex.Message)}\"");
                completion.TrySetResult(CommandExitCodes.CommandFailed);
            }
            finally
            {
                form._activeCommandRequestId.Value = null;
                _activeRequestId.Value = null;
            }
        }

        private static async Task<bool> WaitForHandleCreatedAsync(Form form, CancellationToken cancellationToken)
        {
            if (form == null)
            {
                return false;
            }

            if (form.IsHandleCreated)
            {
                return true;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler onHandleCreated = null;
            onHandleCreated = (s, e) =>
            {
                form.HandleCreated -= onHandleCreated;
                tcs.TrySetResult(true);
            };

            form.HandleCreated += onHandleCreated;

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(3000);
                Task delayTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                Task completed = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);
                form.HandleCreated -= onHandleCreated;
                return completed == tcs.Task && tcs.Task.Result;
            }
        }

        private static void LogStartupMessage(string message)
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
            File.AppendAllText(logPath, $"{DateTime.Now}: {message}\n");
        }

        private static string FormatArgsForLifecycle(string[] args)
        {
            if (args == null || args.Length == 0)
                return string.Empty;

            return EscapeForLifecycle(string.Join(" | ", args));
        }

        private static string EscapeForLifecycle(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static string GetBuildConfiguration()
        {
            #if DEBUG
                return "DEBUG";
            #else
                return "RELEASE";
            #endif
        }

        private static ICommandDispatcher CreateDispatcher(Form1 form)
        {
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            return new CommandDispatcher(
                CreateLegacyScenePlan,
                command => ExecuteCommandQueuedAsync(form, ToDispatchArguments(command), command.RequestId.ToString("N"), "PIPE", Process.GetCurrentProcess().Id, command.CancellationToken),
                command => ExecuteCommandQueuedAsync(form, ToDispatchArguments(command), command.RequestId.ToString("N"), "PIPE", Process.GetCurrentProcess().Id, command.CancellationToken),
                command => ExecuteCommandQueuedAsync(form, ToDispatchArguments(command), command.RequestId.ToString("N"), "PIPE", Process.GetCurrentProcess().Id, command.CancellationToken),
                command => ExecuteCommandQueuedAsync(form, ToDispatchArguments(command), command.RequestId.ToString("N"), "PIPE", Process.GetCurrentProcess().Id, command.CancellationToken));
        }

        private static string ResolveSceneCommandName(SceneDispatchRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(request.CommandName))
            {
                return request.CommandName.Trim();
            }

            if (request.Arguments != null && request.Arguments.Count > 0 && !string.IsNullOrWhiteSpace(request.Arguments[0]))
            {
                return request.Arguments[0].Trim();
            }

            return string.Empty;
        }

        private static string NormalizeSceneCommandName(string commandName)
        {
            return string.IsNullOrWhiteSpace(commandName)
                ? string.Empty
                : commandName.Trim();
        }

        private static CommandPipeline ResolvePhaseOneTargetPipeline(string normalizedCommandName)
        {
            if (string.IsNullOrWhiteSpace(normalizedCommandName))
            {
                return CommandPipeline.Audio;
            }

            if (string.Equals(normalizedCommandName, "Altar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedCommandName, "Predigt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedCommandName, "Gebet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedCommandName, "GoPro", StringComparison.OrdinalIgnoreCase))
            {
                return CommandPipeline.Position;
            }

            if (string.Equals(normalizedCommandName, "LesungMulti", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedCommandName, "Band", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedCommandName, "BandMulti", StringComparison.OrdinalIgnoreCase))
            {
                return CommandPipeline.Video;
            }

            return CommandPipeline.Audio;
        }

        private static PipelineCommand CreateLegacyPipelineCommand(
            Guid sceneId,
            Guid requestId,
            CommandPipeline pipeline,
            string commandName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            return new PipelineCommand
            {
                RequestId = requestId,
                SceneId = sceneId,
                Pipeline = pipeline,
                CommandName = commandName,
                Arguments = arguments ?? Array.Empty<string>(),
                CreatedAt = DateTimeOffset.UtcNow,
                CancellationToken = cancellationToken
            };
        }

        private static ScenePlan CreateLegacyScenePlan(SceneDispatchRequest request, CancellationToken cancellationToken)
        {
            Guid sceneId = Guid.NewGuid();
            Guid requestId;
            if (!Guid.TryParse(request.ExternalRequestId, out requestId))
            {
                requestId = Guid.NewGuid();
            }

            string resolvedCommandName = ResolveSceneCommandName(request);
            string normalizedCommandName = NormalizeSceneCommandName(resolvedCommandName);
            IReadOnlyList<string> dispatchArguments = request.Arguments ?? Array.Empty<string>();

            CommandPipeline targetPipeline = ResolvePhaseOneTargetPipeline(normalizedCommandName);

            PipelineCommand legacyCommand = CreateLegacyPipelineCommand(
                sceneId,
                requestId,
                targetPipeline,
                normalizedCommandName,
                dispatchArguments,
                cancellationToken);

            LogData.LogLifecycle("DISPATCHER", "SCENE_PLAN_MAPPED",
                $"SceneId={sceneId} ExternalRequestId={request.ExternalRequestId} Scene={normalizedCommandName} Pipeline={targetPipeline} Command={normalizedCommandName}");

            return new ScenePlan
            {
                SceneId = sceneId,
                SceneName = normalizedCommandName,
                AudioCommands = targetPipeline == CommandPipeline.Audio ? new[] { legacyCommand } : Array.Empty<PipelineCommand>(),
                VideoCommands = targetPipeline == CommandPipeline.Video ? new[] { legacyCommand } : Array.Empty<PipelineCommand>(),
                PositionCommands = targetPipeline == CommandPipeline.Position ? new[] { legacyCommand } : Array.Empty<PipelineCommand>(),
                UiCommands = Array.Empty<PipelineCommand>()
            };
        }

        private static string[] ToDispatchArguments(PipelineCommand command)
        {
            if (command?.Arguments == null)
            {
                return Array.Empty<string>();
            }

            return command.Arguments.ToArray();
        }

        private static Dictionary<string, NetworkDevice> LoadNetworkConfig(LogData logData)
        {
            var config = new Dictionary<string, NetworkDevice>();
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Network.cfg");
            
            logData.sendInfoMessage($"Suche Network.cfg: {configPath}");
            
            if (!File.Exists(configPath))
            {
                logData.sendInfoMessage($"WARNUNG: Network.cfg nicht gefunden!");
                return config;
            }
            
            try
            {
                string[] lines = File.ReadAllLines(configPath);
                logData.sendInfoMessage($"Network.cfg gelesen: {lines.Length} Zeilen");
                
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;
                    
                    string[] parts = line.Split(';');
                    if (parts.Length >= 2)
                    {
                        string name = parts[0].Trim();
                        string ip = parts[1].Trim();
                        
                        if (ip.Contains("//"))
                        {
                            ip = ip.Substring(0, ip.IndexOf("//")).Trim();
                        }
                        
                        int port = 9910;
                        if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out int p))
                        {
                            port = p;
                        }
                        
                        config[name] = new NetworkDevice { IPAddress = ip, Port = port };
                        logData.sendInfoMessage($"  -> {name}: {ip}:{port}");
                    }
                }
                
                logData.sendInfoMessage($"Network.cfg erfolgreich geladen: {config.Count} Geräte");
            }
            catch (Exception ex)
            {
                logData.sendInfoMessage($"FEHLER beim Laden von Network.cfg: {ex.Message}");
            }
            
            return config;
        }

        private static bool RequiresATEM(string command)
        {
            string[] atemCommands = { "Altar", "Predigt", "GoPro", "Band", "Text", "ATEM_Init", 
                                     "ATEM_MIC1_On", "ATEM_MIC1_Off", "ATEM_MIC2_On", "ATEM_MIC2_Off" };
            return Array.Exists(atemCommands, cmd => cmd.Equals(command, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RequiresRaspberryPi(string command)
        {
            string[] raspiCommands = { "Pause", "Timer", "Backup_Start", 
                                      "Backup_Stop", "BEAMER_ON", "BEAMER_Mute" };
            return Array.Exists(raspiCommands, cmd => cmd.Equals(command, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RequiresRoku(string command)
        {
            return command.StartsWith("ROKU_", StringComparison.OrdinalIgnoreCase);
        }

        private static ATEMControl InitializeATEM(Dictionary<string, NetworkDevice> config, LogData logData)
        {
            if (!config.ContainsKey("ATEM_MiniPro"))
            {
                logData.sendInfoMessage("FEHLER: ATEM_MiniPro nicht in Network.cfg gefunden");
                logData.sendInfoMessage($"Verfügbare Geräte: {string.Join(", ", config.Keys)}");
                return null;
            }
            
            string ip = config["ATEM_MiniPro"].IPAddress;
            logData.sendInfoMessage($">>> Verbinde mit ATEM Mini Pro: {ip}:9910");
            
            try
            {
                var atem = new ATEMControl(ip, message => logData.sendInfoMessage(message));
                logData.sendInfoMessage(">>> ATEMControl Objekt erstellt");
                
                bool connected = atem.Connect();
                logData.sendInfoMessage($">>> Connect() Ergebnis: {connected}");
                
                if (connected)
                {
                    logData.sendInfoMessage(">>> ATEM ERFOLGREICH VERBUNDEN!");
                    return atem;
                }
                else
                {
                    logData.sendInfoMessage(">>> ATEM Verbindung FEHLGESCHLAGEN");
                    logData.sendInfoMessage("Prüfen Sie:");
                    logData.sendInfoMessage("  - ATEM ist eingeschaltet");
                    logData.sendInfoMessage("  - Netzwerkkabel ist verbunden");
                    logData.sendInfoMessage($"  - IP-Adresse {ip} ist korrekt");
                    logData.sendInfoMessage("  - Firewall blockiert Port 9910 nicht");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logData.sendInfoMessage($">>> AUSNAHME beim ATEM Connect: {ex.Message}");
                logData.sendInfoMessage($">>> StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        private static RasPi InitializeRaspberryPi(Dictionary<string, NetworkDevice> config, LogData logData)
        {
            logData.sendInfoMessage(">>> RaspberryPi Initialisierung (TODO)");
            return null;
        }

        private static ROKU_TV_Remote InitializeRoku(Dictionary<string, NetworkDevice> config, LogData logData)
        {
            if (!config.ContainsKey("Roku_TV"))
            {
                logData.sendInfoMessage("FEHLER: Roku_TV nicht in Network.cfg gefunden");
                return null;
            }
            
            string ip = config["Roku_TV"].IPAddress;
            logData.sendInfoMessage($">>> Verbinde mit Roku TV: {ip}");
            return new ROKU_TV_Remote(ip);
        }

        private static void ExecuteATEMCommand(ATEMControl atem, string command, string[] args, LogData logData)
        {
            if (atem == null || !atem.IsConnected)
            {
                logData.sendInfoMessage(">>> FEHLER: ATEM nicht verbunden!");
                return;
            }
            
            logData.sendInfoMessage($">>> Führe ATEM-Kommando aus: {command}");
            
            try
            {
                switch (command.ToLower())
                {
                    case "altar":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 3 (Altar)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input3);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf Altar (HDMI 3) umgeschaltet");
                        break;
                        
                    case "predigt":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 4 (Predigt)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input4);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf Predigt (HDMI 4) umgeschaltet");
                        break;
                        
                    case "gopro":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 1 (GoPro)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input2);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf GoPro (HDMI 2) umgeschaltet");
                        break;
                        
                    case "band":
                    case "text":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 2 (Laptop)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input1);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf Laptop (HDMI 1) umgeschaltet");
                        break;
                        
                    case "atem_init":
                        logData.sendInfoMessage(">>> Initialisiere ATEM zu Default-Zustand...");
                        atem.InitializeToDefaultState();
                        logData.sendInfoMessage(">>> ERFOLG: ATEM Initialisierung abgeschlossen");
                        break;
                        
                    case "atem_mic1_on":
                        logData.sendInfoMessage(">>> Aktiviere MIC 1...");
                        atem.SetAudioMixerInput(1, true);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 1 aktiviert");
                        break;
                        
                    case "atem_mic1_off":
                        logData.sendInfoMessage(">>> Deaktiviere MIC 1...");
                        atem.SetAudioMixerInput(1, false);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 1 deaktiviert");
                        break;
                    case "atem_mic2_on":
                        logData.sendInfoMessage(">>> Aktiviere MIC 2...");
                        atem.SetAudioMixerInput(2, true);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 2 aktiviert");
                        break;
                    case "atem_mic2_off":
                        logData.sendInfoMessage(">>> Deaktiviere MIC 2...");
                        atem.SetAudioMixerInput(2, false);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 2 deaktiviert");
                        break;
                        
                    default:
                        logData.sendInfoMessage($">>> WARNUNG: ATEM-Kommando '{command}' nicht implementiert");
                        break;
                }
                
            }
            catch (Exception ex)
            {
                logData.sendInfoMessage($">>> FEHLER bei ATEM-Kommando: {ex.Message}");
                logData.sendInfoMessage($">>> StackTrace: {ex.StackTrace}");
            }
        }

        private static void ExecuteRaspberryPiCommand(RasPi raspi, string command, string[] args, LogData logData)
        {
            logData.sendInfoMessage($">>> RaspberryPi-Kommando '{command}' (noch nicht implementiert)");
        }

        private static void ExecuteRokuCommand(ROKU_TV_Remote roku, string command, string[] args, LogData logData)
        {
            logData.sendInfoMessage($">>> Roku-Kommando '{command}' (noch nicht implementiert)");
        }

        private static bool TryRunShutdownTestHostMode(string[] args, out int exitCode)
        {
            exitCode = 0;
            string[] safeArgs = args ?? Array.Empty<string>();
            if (safeArgs.Length == 0)
            {
                return false;
            }

            string mode = safeArgs[0];
            switch (mode)
            {
                case "--run-blocked-server-shutdown":
                    exitCode = RunBlockedServerShutdownHost(safeArgs);
                    return true;
                case "--try-acquire-mutex":
                    exitCode = RunTryAcquireMutexHost(safeArgs);
                    return true;
                case "--double-stop":
                    exitCode = RunDoubleStopHost(safeArgs);
                    return true;
                case "--dispose-during-stop":
                    exitCode = RunDisposeDuringStopHost(safeArgs);
                    return true;
                case "--stop-without-client":
                    exitCode = RunStopWithoutClientHost(safeArgs);
                    return true;
                case "--stop-with-silent-client":
                    exitCode = RunStopWithSilentClientHost(safeArgs);
                    return true;
                case "--pipe-concurrency-smoke":
                    exitCode = RunPipeConcurrencySmokeHost(safeArgs);
                    return true;
                case "--forward-connect-timeout":
                    exitCode = RunForwardConnectTimeoutHost(safeArgs);
                    return true;
                case "--gated-primary-shutdown":
                    exitCode = RunGatedPrimaryShutdownHost(safeArgs);
                    return true;
                default:
                    return false;
            }
        }

        private static int RunBlockedServerShutdownHost(string[] args)
        {
            return RunServerShutdownScenario(args, connectSilentClient: true, modeName: "BLOCKED_SERVER_SHUTDOWN");
        }

        private static int RunStopWithoutClientHost(string[] args)
        {
            return RunServerShutdownScenario(args, connectSilentClient: false, modeName: "STOP_WITHOUT_CLIENT");
        }

        private static int RunStopWithSilentClientHost(string[] args)
        {
            return RunServerShutdownScenario(args, connectSilentClient: true, modeName: "STOP_WITH_SILENT_CLIENT");
        }

        private static int RunDoubleStopHost(string[] args)
        {
            string mutexName = GetArgumentValue(args, "--mutex=") ?? @"Global\JokiAutomation_ShutdownTest_Mutex";
            string pipeName = GetArgumentValue(args, "--pipe=") ?? "JokiAutomation.ShutdownTest.Pipe";
            int timeoutMs = GetHostTimeoutMs(args, 5000);

            Mutex mutex = null;
            bool acquired = false;
            try
            {
                mutex = new Mutex(false, mutexName);
                acquired = mutex.WaitOne(0, false);
                if (!acquired)
                {
                    Console.WriteLine("TEST_MUTEX_NOT_ACQUIRED");
                    return 10;
                }

                Console.WriteLine("TEST_MUTEX_ACQUIRED");
                var dispatcher = new NoOpCommandDispatcher();
                var server = new SingleInstanceCommandServer(
                    (incomingArgs, requestId, senderPid, token) => Task.FromResult(CommandExitCodes.Success),
                    dispatcher,
                    pipeName);

                server.Start();
                Console.WriteLine("TEST_PIPE_SERVER_STARTED");

                Task firstStop = server.StopAsync();
                Task secondStop = server.StopAsync();
                Console.WriteLine(object.ReferenceEquals(firstStop, secondStop)
                    ? "TEST_STOP_TASK_SAME"
                    : "TEST_STOP_TASK_DIFFERENT");

                bool completed = firstStop.Wait(timeoutMs);
                if (!completed)
                {
                    Console.WriteLine("TEST_STOP_TIMEOUT");
                    server.Dispose();
                    return 11;
                }

                server.Dispose();
                Console.WriteLine("TEST_PIPE_SERVER_STOPPED");
                return 0;
            }
            catch (AbandonedMutexException)
            {
                Console.WriteLine("TEST_MUTEX_ABANDONED");
                return 12;
            }
            catch (Exception ex)
            {
                Console.WriteLine("TEST_HOST_EXCEPTION " + ex.GetType().Name + " " + ex.Message);
                return 13;
            }
            finally
            {
                if (acquired && mutex != null)
                {
                    Console.WriteLine("TEST_MUTEX_RELEASE_BEGIN");
                    try
                    {
                        mutex.ReleaseMutex();
                        Console.WriteLine("TEST_MUTEX_RELEASED");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("TEST_MUTEX_RELEASE_EXCEPTION " + ex.GetType().Name);
                    }
                }

                mutex?.Dispose();
                Console.WriteLine("TEST_PROCESS_EXIT");
            }
        }

        private static int RunDisposeDuringStopHost(string[] args)
        {
            string mutexName = GetArgumentValue(args, "--mutex=") ?? @"Global\JokiAutomation_ShutdownTest_Mutex";
            string pipeName = GetArgumentValue(args, "--pipe=") ?? "JokiAutomation.ShutdownTest.Pipe";
            int timeoutMs = GetHostTimeoutMs(args, 5000);

            Mutex mutex = null;
            bool acquired = false;
            NamedPipeClientStream silentClient = null;
            try
            {
                mutex = new Mutex(false, mutexName);
                acquired = mutex.WaitOne(0, false);
                if (!acquired)
                {
                    Console.WriteLine("TEST_MUTEX_NOT_ACQUIRED");
                    return 20;
                }

                Console.WriteLine("TEST_MUTEX_ACQUIRED");
                var server = new SingleInstanceCommandServer(
                    (incomingArgs, requestId, senderPid, token) => Task.FromResult(CommandExitCodes.Success),
                    new NoOpCommandDispatcher(),
                    pipeName);
                server.Start();
                Console.WriteLine("TEST_PIPE_SERVER_STARTED");

                silentClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                silentClient.Connect(Math.Max(1000, timeoutMs));
                Console.WriteLine("TEST_PIPE_CLIENT_CONNECTED");

                Task stopTask = server.StopAsync();
                server.Dispose();
                Console.WriteLine("TEST_DISPOSE_RETURNED");

                stopTask.Wait(Math.Max(1000, timeoutMs));
                return 0;
            }
            catch (AbandonedMutexException)
            {
                Console.WriteLine("TEST_MUTEX_ABANDONED");
                return 21;
            }
            catch (Exception ex)
            {
                Console.WriteLine("TEST_HOST_EXCEPTION " + ex.GetType().Name + " " + ex.Message);
                return 22;
            }
            finally
            {
                try
                {
                    silentClient?.Dispose();
                }
                catch { }

                if (acquired && mutex != null)
                {
                    Console.WriteLine("TEST_MUTEX_RELEASE_BEGIN");
                    try
                    {
                        mutex.ReleaseMutex();
                        Console.WriteLine("TEST_MUTEX_RELEASED");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("TEST_MUTEX_RELEASE_EXCEPTION " + ex.GetType().Name);
                    }
                }

                mutex?.Dispose();
                Console.WriteLine("TEST_PROCESS_EXIT");
            }
        }

        private static int RunTryAcquireMutexHost(string[] args)
        {
            string mutexName = GetArgumentValue(args, "--mutex=") ?? @"Global\JokiAutomation_ShutdownTest_Mutex";
            int timeoutMs = GetHostTimeoutMs(args, 2000);
            Mutex mutex = null;
            bool acquired = false;

            try
            {
                mutex = new Mutex(false, mutexName);
                acquired = mutex.WaitOne(timeoutMs, false);
                if (!acquired)
                {
                    Console.WriteLine("TEST_MUTEX_REACQUIRE_FAILED");
                    return 30;
                }

                Console.WriteLine("TEST_MUTEX_REACQUIRED");
                return 0;
            }
            catch (AbandonedMutexException)
            {
                Console.WriteLine("TEST_MUTEX_ABANDONED");
                return 31;
            }
            catch (Exception ex)
            {
                Console.WriteLine("TEST_HOST_EXCEPTION " + ex.GetType().Name + " " + ex.Message);
                return 32;
            }
            finally
            {
                if (acquired && mutex != null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch { }
                }

                mutex?.Dispose();
            }
        }

        private static int RunServerShutdownScenario(string[] args, bool connectSilentClient, string modeName)
        {
            string mutexName = GetArgumentValue(args, "--mutex=") ?? @"Global\JokiAutomation_ShutdownTest_Mutex";
            string pipeName = GetArgumentValue(args, "--pipe=") ?? "JokiAutomation.ShutdownTest.Pipe";
            int timeoutMs = GetHostTimeoutMs(args, 5000);

            Mutex mutex = null;
            bool acquired = false;
            SingleInstanceCommandServer server = null;
            NamedPipeClientStream silentClient = null;

            try
            {
                Console.WriteLine("TEST_MODE " + modeName);
                mutex = new Mutex(false, mutexName);
                acquired = mutex.WaitOne(0, false);
                if (!acquired)
                {
                    Console.WriteLine("TEST_MUTEX_NOT_ACQUIRED");
                    return 40;
                }

                Console.WriteLine("TEST_MUTEX_ACQUIRED");

                var dispatcher = new NoOpCommandDispatcher();
                server = new SingleInstanceCommandServer(
                    (incomingArgs, requestId, senderPid, token) => Task.FromResult(CommandExitCodes.Success),
                    dispatcher,
                    pipeName);
                server.Start();
                Console.WriteLine("TEST_PIPE_SERVER_STARTED");

                if (connectSilentClient)
                {
                    silentClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    silentClient.Connect(Math.Max(1000, timeoutMs));
                    Console.WriteLine("TEST_PIPE_CLIENT_CONNECTED");
                }

                Console.WriteLine("TEST_SHUTDOWN_BEGIN");
                Task stopTask = server.StopAsync();
                bool stopped = stopTask.Wait(timeoutMs);
                if (stopped)
                {
                    Console.WriteLine("TEST_PIPE_SERVER_STOPPED");
                }
                else
                {
                    Console.WriteLine("TEST_PIPE_SERVER_STOP_TIMEOUT");
                    return 43;
                }

                var disposeTask = Task.Run(() => server.Dispose());
                bool disposeCompleted = disposeTask.Wait(timeoutMs);
                if (disposeCompleted)
                {
                    Console.WriteLine("TEST_PIPE_SERVER_DISPOSED");
                }
                else
                {
                    Console.WriteLine("TEST_PIPE_SERVER_DISPOSE_TIMEOUT");
                    return 44;
                }

                return 0;
            }
            catch (AbandonedMutexException)
            {
                Console.WriteLine("TEST_MUTEX_ABANDONED");
                return 41;
            }
            catch (Exception ex)
            {
                Console.WriteLine("TEST_HOST_EXCEPTION " + ex.GetType().Name + " " + ex.Message);
                return 42;
            }
            finally
            {
                try
                {
                    silentClient?.Dispose();
                }
                catch { }

                if (acquired && mutex != null)
                {
                    Console.WriteLine("TEST_MUTEX_RELEASE_BEGIN");
                    try
                    {
                        mutex.ReleaseMutex();
                        Console.WriteLine("TEST_MUTEX_RELEASED");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("TEST_MUTEX_RELEASE_EXCEPTION " + ex.GetType().Name);
                    }
                }

                mutex?.Dispose();
                Console.WriteLine("TEST_PROCESS_EXIT");
            }
        }

        private static int RunPipeConcurrencySmokeHost(string[] args)
        {
            string pipeName = GetArgumentValue(args, "--pipe=") ?? ("JokiAutomation.PipeConcurrency." + Guid.NewGuid().ToString("N"));
            int clientCount = Math.Max(6, ParseIntArg(args, "--clients=", 6));
            int commandDelayMs = ParseIntArg(args, "--commandDelayMs=", 350);
            int launchDelayMs = ParseIntArg(args, "--launchDelayMs=", 50);
            int timeoutMs = GetHostTimeoutMs(args, 20000);

            var dispatcher = new SerialTestCommandDispatcher(commandDelayMs);
            var server = new SingleInstanceCommandServer(
                (incomingArgs, requestId, senderPid, token) => Task.FromResult(CommandExitCodes.Success),
                dispatcher,
                pipeName);

            try
            {
                Console.WriteLine("TEST_MODE PIPE_CONCURRENCY_SMOKE");
                server.Start();
                Console.WriteLine("TEST_PIPE_SERVER_STARTED");

                string[] commands = Enumerable.Range(1, clientCount)
                    .Select(i => $"BurstTest_{i:00}")
                    .ToArray();

                var clientTasks = new Task<PipeCommandResponse>[commands.Length];
                for (int i = 0; i < commands.Length; i++)
                {
                    string command = commands[i];
                    clientTasks[i] = Task.Run(async () =>
                    {
                        using (var cts = new CancellationTokenSource(timeoutMs))
                        {
                            var client = new SingleInstanceCommandClient(pipeName);
                            return await client.ForwardAsync(new[] { command }, cts.Token).ConfigureAwait(false);
                        }
                    });

                    if (i < commands.Length - 1)
                    {
                        Thread.Sleep(launchDelayMs);
                    }
                }

                if (!Task.WhenAll(clientTasks).Wait(timeoutMs))
                {
                    Console.WriteLine("TEST_CLIENT_TIMEOUT");
                    return 60;
                }

                for (int i = 0; i < commands.Length; i++)
                {
                    PipeCommandResponse response = clientTasks[i].Result;
                    Console.WriteLine(
                        $"TEST_CLIENT_RESULT Command={commands[i]} ExitCode={response.ExitCode} Accepted={response.Accepted} Completed={response.Completed} State={response.State}");
                }

                foreach (KeyValuePair<string, int> pair in dispatcher.GetCommandCounts())
                {
                    Console.WriteLine($"TEST_COMMAND_COUNT Command={pair.Key} Count={pair.Value}");
                }

                foreach (KeyValuePair<string, int> pair in dispatcher.GetRequestCounts())
                {
                    Console.WriteLine($"TEST_REQUEST_COUNT RequestId={pair.Key} Count={pair.Value}");
                }

                bool allSucceeded = clientTasks.All(t => t.Result != null && t.Result.ExitCode == CommandExitCodes.Success);
                bool allCommandsProcessedOnce = dispatcher.GetCommandCounts().All(p => p.Value == 1) && dispatcher.GetCommandCounts().Count == commands.Length;
                bool allRequestsProcessedOnce = dispatcher.GetRequestCounts().All(p => p.Value == 1) && dispatcher.GetRequestCounts().Count == commands.Length;

                if (!allSucceeded)
                {
                    return 61;
                }

                if (!allCommandsProcessedOnce || !allRequestsProcessedOnce)
                {
                    return 62;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("TEST_HOST_EXCEPTION " + ex.GetType().Name + " " + ex.Message);
                return 63;
            }
            finally
            {
                try
                {
                    server.StopAsync().GetAwaiter().GetResult();
                }
                catch
                {
                }

                server.Dispose();
                Console.WriteLine("TEST_PROCESS_EXIT");
            }
        }

        private static int RunForwardConnectTimeoutHost(string[] args)
        {
            string pipeName = GetArgumentValue(args, "--pipe=") ?? ("JokiAutomation.NoServer." + Guid.NewGuid().ToString("N"));
            int timeoutMs = GetHostTimeoutMs(args, 15000);

            try
            {
                Console.WriteLine("TEST_MODE FORWARD_CONNECT_TIMEOUT");

                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    var client = new SingleInstanceCommandClient(pipeName);
                    PipeCommandResponse response = client.ForwardAsync(new[] { "ConnectTimeoutProbe" }, cts.Token).GetAwaiter().GetResult();
                    Console.WriteLine("TEST_FORWARD_EXITCODE " + response.ExitCode);
                    Console.WriteLine("TEST_FORWARD_STATE " + response.State);
                    return response.ExitCode == CommandExitCodes.PipeConnectFailed ? 0 : 64;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("TEST_HOST_EXCEPTION " + ex.GetType().Name + " " + ex.Message);
                return 65;
            }
            finally
            {
                Console.WriteLine("TEST_PROCESS_EXIT");
            }
        }

        /// <summary>
        /// Deterministic shutdown-race test host. Acts as a "Primary" instance that stops its
        /// Named Pipe server and then blocks, with the mutex still held, until the test explicitly
        /// signals that the mutex may be released. This lets a test deterministically launch a
        /// Forwarder exactly in the window where the mutex is held but the pipe is already gone,
        /// instead of relying on arbitrary Task.Delay timing.
        /// Gate names are derived from --gate=&lt;suffix&gt; and use two named EventWaitHandles:
        ///   &lt;suffix&gt;.PipeStopped      - signaled by this host once the pipe server has stopped.
        ///   &lt;suffix&gt;.AllowMutexRelease - waited on by this host before releasing the mutex.
        /// </summary>
        private static int RunGatedPrimaryShutdownHost(string[] args)
        {
            string mutexName = GetArgumentValue(args, "--mutex=") ?? @"Global\JokiAutomation_ShutdownTest_Mutex";
            string pipeName = GetArgumentValue(args, "--pipe=") ?? "JokiAutomation.ShutdownTest.Pipe";
            string gateSuffix = GetArgumentValue(args, "--gate=") ?? Guid.NewGuid().ToString("N");
            int timeoutMs = GetHostTimeoutMs(args, 5000);

            string pipeStoppedEventName = "Global\\" + gateSuffix + ".PipeStopped";
            string allowMutexReleaseEventName = "Global\\" + gateSuffix + ".AllowMutexRelease";

            Mutex mutex = null;
            bool acquired = false;
            SingleInstanceCommandServer server = null;
            EventWaitHandle pipeStoppedEvent = null;
            EventWaitHandle allowMutexReleaseEvent = null;

            try
            {
                Console.WriteLine("TEST_MODE GATED_PRIMARY_SHUTDOWN");

                pipeStoppedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, pipeStoppedEventName);
                allowMutexReleaseEvent = new EventWaitHandle(false, EventResetMode.ManualReset, allowMutexReleaseEventName);

                mutex = new Mutex(false, mutexName);
                acquired = mutex.WaitOne(0, false);
                if (!acquired)
                {
                    Console.WriteLine("TEST_MUTEX_NOT_ACQUIRED");
                    return 50;
                }

                Console.WriteLine("TEST_MUTEX_ACQUIRED");

                var dispatcher = new NoOpCommandDispatcher();
                server = new SingleInstanceCommandServer(
                    (incomingArgs, requestId, senderPid, token) => Task.FromResult(CommandExitCodes.Success),
                    dispatcher,
                    pipeName);
                server.Start();
                Console.WriteLine("TEST_PIPE_SERVER_STARTED");

                Console.WriteLine("TEST_SHUTDOWN_BEGIN");
                Task stopTask = server.StopAsync();
                bool stopped = stopTask.Wait(timeoutMs);
                if (!stopped)
                {
                    Console.WriteLine("TEST_PIPE_SERVER_STOP_TIMEOUT");
                    return 53;
                }

                Console.WriteLine("TEST_PIPE_SERVER_STOPPED");

                // Mutex is still held here while the pipe is already gone - this is the race window.
                Console.WriteLine("TEST_MUTEX_STILL_HELD");
                pipeStoppedEvent.Set();
                Console.WriteLine("TEST_GATE_PIPE_STOPPED_SIGNALED");

                bool allowed = allowMutexReleaseEvent.WaitOne(timeoutMs);
                if (!allowed)
                {
                    Console.WriteLine("TEST_GATE_ALLOW_MUTEX_RELEASE_TIMEOUT");
                    return 54;
                }

                Console.WriteLine("TEST_GATE_ALLOW_MUTEX_RELEASE_RECEIVED");
                return 0;
            }
            catch (AbandonedMutexException)
            {
                Console.WriteLine("TEST_MUTEX_ABANDONED");
                return 51;
            }
            catch (Exception ex)
            {
                Console.WriteLine("TEST_HOST_EXCEPTION " + ex.GetType().Name + " " + ex.Message);
                return 52;
            }
            finally
            {
                try
                {
                    server?.Dispose();
                }
                catch { }

                if (acquired && mutex != null)
                {
                    Console.WriteLine("TEST_MUTEX_RELEASE_BEGIN");
                    try
                    {
                        mutex.ReleaseMutex();
                        Console.WriteLine("TEST_MUTEX_RELEASED");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("TEST_MUTEX_RELEASE_EXCEPTION " + ex.GetType().Name);
                    }
                }

                mutex?.Dispose();
                pipeStoppedEvent?.Dispose();
                allowMutexReleaseEvent?.Dispose();
                Console.WriteLine("TEST_PROCESS_EXIT");
            }
        }

        private static string GetArgumentValue(string[] args, string prefix)
        {
            if (args == null || string.IsNullOrEmpty(prefix))
            {
                return null;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? string.Empty;
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(prefix.Length);
                }
            }

            return null;
        }

        private static int ParseIntArg(string[] args, string prefix, int fallback)
        {
            string raw = GetArgumentValue(args, prefix);
            if (int.TryParse(raw, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            return fallback;
        }

        private static int GetHostTimeoutMs(string[] args, int fallback)
        {
            int parsed = ParseIntArg(args, "--timeoutMs=", fallback);
            if (parsed < 1000)
            {
                return 1000;
            }

            if (parsed > 30000)
            {
                return 30000;
            }

            return parsed;
        }

        private sealed class NoOpCommandDispatcher : ICommandDispatcher
        {
            public Task<SceneDispatchResult> DispatchAsync(SceneDispatchRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new SceneDispatchResult
                {
                    SceneId = Guid.NewGuid(),
                    ExternalRequestId = request?.ExternalRequestId,
                    Accepted = true,
                    Completed = true,
                    State = SceneExecutionState.Completed,
                    ExitCode = CommandExitCodes.Success
                });
            }

            public Task StopAsync()
            {
                return Task.CompletedTask;
            }
        }

        private sealed class SerialTestCommandDispatcher : ICommandDispatcher
        {
            private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
            private readonly ConcurrentDictionary<string, int> _requestCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly ConcurrentDictionary<string, int> _commandCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly int _delayMs;

            public SerialTestCommandDispatcher(int delayMs)
            {
                _delayMs = Math.Max(1, delayMs);
            }

            public async Task<SceneDispatchResult> DispatchAsync(SceneDispatchRequest request, CancellationToken cancellationToken)
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    string requestId = request?.ExternalRequestId ?? string.Empty;
                    string commandName = request?.CommandName ?? string.Empty;
                    int requestCount = _requestCounts.AddOrUpdate(requestId, 1, (_, current) => current + 1);
                    int commandCount = _commandCounts.AddOrUpdate(commandName, 1, (_, current) => current + 1);

                    Console.WriteLine($"TEST_DISPATCH_BEGIN RequestId={requestId} Command={commandName} RequestCount={requestCount} CommandCount={commandCount}");
                    await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
                    Console.WriteLine($"TEST_DISPATCH_END RequestId={requestId} Command={commandName}");

                    return new SceneDispatchResult
                    {
                        SceneId = Guid.NewGuid(),
                        ExternalRequestId = requestId,
                        Accepted = true,
                        Completed = true,
                        State = SceneExecutionState.Completed,
                        ExitCode = CommandExitCodes.Success
                    };
                }
                finally
                {
                    _gate.Release();
                }
            }

            public Task StopAsync()
            {
                return Task.CompletedTask;
            }

            public IReadOnlyCollection<KeyValuePair<string, int>> GetRequestCounts()
            {
                return _requestCounts.ToArray();
            }

            public IReadOnlyCollection<KeyValuePair<string, int>> GetCommandCounts()
            {
                return _commandCounts.ToArray();
            }
        }
    }

    internal static class SingleInstanceCommandConstants
    {
        public const string SingleInstanceMutexName = @"Global\JokiAutomation_SingleInstance";
        public const string CommandPipeName = "JokiAutomation.CommandPipe";
        public const string SingleInstanceMutexEnvironmentVariable = "JOKI_SINGLE_INSTANCE_MUTEX";
        public const string CommandPipeEnvironmentVariable = "JOKI_COMMAND_PIPE";
        public const int PipeConnectTimeoutMs = 3000;
        public const int PipeAckTimeoutMs = 45000;
        public const int PipeCompletionTimeoutMs = 90000;
        public const int MaxPipeServerInstances = 16;
        public const int MaxMessageBytes = 64 * 1024;
        public const int MaxQueuedCommands = 100;

        public static string ResolveMutexName()
        {
            string configured = Environment.GetEnvironmentVariable(SingleInstanceMutexEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured)
                ? SingleInstanceMutexName
                : configured.Trim();
        }

        public static string ResolvePipeName()
        {
            string configured = Environment.GetEnvironmentVariable(CommandPipeEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured)
                ? CommandPipeName
                : configured.Trim();
        }
    }

    internal static class CommandExitCodes
    {
        public const int Success = 0;
        public const int CommandFailed = 1;
        public const int InvalidArguments = 3;
        public const int PipeConnectFailed = 4;
        public const int PipeTimeout = 5;
        public const int CommandCanceled = 6;
        public const int QueueRejected = 7;
        public const int EventTimerAlreadyRunning = 8;
        public const int UiDispatchFailed = 9;
    }

    [Serializable]
    internal sealed class PipeCommandRequest
    {
        public string RequestId { get; set; }
        public int SenderProcessId { get; set; }
        public string[] Arguments { get; set; }
        public string Source { get; set; }
        public string WaitMode { get; set; }
    }

    internal static class PipeCommandStates
    {
        public const string Queued = "Queued";
        public const string Executing = "Executing";
        public const string Completed = "Completed";
        public const string Rejected = "Rejected";
    }

    [Serializable]
    internal sealed class PipeCommandResponse
    {
        public string RequestId { get; set; }
        public bool Accepted { get; set; }
        public bool Completed { get; set; }
        public string State { get; set; }
        public int ExitCode { get; set; }
        public string Message { get; set; }
    }

    internal static class SingleInstanceCommandProtocol
    {
        public static async Task WriteMessageAsync(Stream stream, byte[] payload, CancellationToken token)
        {
            if (payload == null)
            {
                payload = new byte[0];
            }

            if (payload.Length > SingleInstanceCommandConstants.MaxMessageBytes)
            {
                throw new InvalidOperationException("Pipe payload exceeds maximum size.");
            }

            byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);
            await WriteAllAsync(stream, lengthPrefix, token).ConfigureAwait(false);

            if (payload.Length > 0)
            {
                await WriteAllAsync(stream, payload, token).ConfigureAwait(false);
            }

            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        public static async Task<byte[]> ReadMessageAsync(Stream stream, CancellationToken token)
        {
            byte[] lengthPrefix = await ReadExactAsync(stream, 4, token).ConfigureAwait(false);
            int length = BitConverter.ToInt32(lengthPrefix, 0);

            if (length < 0 || length > SingleInstanceCommandConstants.MaxMessageBytes)
            {
                throw new InvalidOperationException("Invalid message length.");
            }

            if (length == 0)
            {
                return new byte[0];
            }

            return await ReadExactAsync(stream, length, token).ConfigureAwait(false);
        }

        public static byte[] Serialize<T>(T value)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                if (value is PipeCommandRequest request)
                {
                    writer.Write((byte)1);
                    WriteString(writer, request.RequestId);
                    writer.Write(request.SenderProcessId);
                    WriteStringArray(writer, request.Arguments);
                    WriteString(writer, request.Source);
                    WriteString(writer, request.WaitMode);
                }
                else if (value is PipeCommandResponse response)
                {
                    writer.Write((byte)2);
                    WriteString(writer, response.RequestId);
                    writer.Write(response.Accepted);
                    writer.Write(response.Completed);
                    WriteString(writer, response.State);
                    writer.Write(response.ExitCode);
                    WriteString(writer, response.Message);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported payload type: {typeof(T).FullName}");
                }

                writer.Flush();
                return ms.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] payload)
        {
            using (var ms = new MemoryStream(payload ?? new byte[0]))
            using (var reader = new BinaryReader(ms, Encoding.UTF8, true))
            {
                byte typeTag = reader.ReadByte();

                object value;
                if (typeTag == 1)
                {
                    string requestId = ReadString(reader);
                    int senderProcessId = reader.ReadInt32();
                    string[] arguments = ReadStringArray(reader);

                    string source = null;
                    string waitMode = null;

                    if (ms.Position < ms.Length)
                    {
                        source = ReadString(reader);
                    }

                    if (ms.Position < ms.Length)
                    {
                        waitMode = ReadString(reader);
                    }

                    value = new PipeCommandRequest
                    {
                        RequestId = requestId,
                        SenderProcessId = senderProcessId,
                        Arguments = arguments,
                        Source = source,
                        WaitMode = waitMode
                    };
                }
                else if (typeTag == 2)
                {
                    value = new PipeCommandResponse
                    {
                        RequestId = ReadString(reader),
                        Accepted = reader.ReadBoolean(),
                        Completed = reader.ReadBoolean(),
                        State = ReadString(reader),
                        ExitCode = reader.ReadInt32(),
                        Message = ReadString(reader)
                    };
                }
                else
                {
                    throw new InvalidOperationException($"Unknown payload type tag: {typeTag}");
                }

                return (T)value;
            }
        }

        public static string FormatArgs(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            return Escape(string.Join(" | ", args));
        }

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static async Task WriteAllAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return;
            }

            await stream.WriteAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
        {
            byte[] buffer = new byte[count];
            int offset = 0;

            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Pipe stream closed before full message was read.");
                }

                offset += read;
            }

            return buffer;
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            bool hasValue = value != null;
            writer.Write(hasValue);
            if (!hasValue)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader)
        {
            bool hasValue = reader.ReadBoolean();
            if (!hasValue)
            {
                return null;
            }

            int length = reader.ReadInt32();
            if (length < 0 || length > SingleInstanceCommandConstants.MaxMessageBytes)
            {
                throw new InvalidOperationException("Invalid string length in payload.");
            }

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException("Unexpected end of payload while reading string.");
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private static void WriteStringArray(BinaryWriter writer, string[] values)
        {
            if (values == null)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                WriteString(writer, values[i]);
            }
        }

        private static string[] ReadStringArray(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0)
            {
                return null;
            }

            if (length > 512)
            {
                throw new InvalidOperationException("Arguments array too large.");
            }

            string[] values = new string[length];
            for (int i = 0; i < length; i++)
            {
                values[i] = ReadString(reader);
            }

            return values;
        }
    }

    internal sealed class SingleInstanceCommandClient
    {
        private readonly string _pipeName;

        public SingleInstanceCommandClient(string pipeName = null)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName)
                ? SingleInstanceCommandConstants.ResolvePipeName()
                : pipeName;
        }

        public async Task<PipeCommandResponse> ForwardAsync(string[] args, CancellationToken token)
        {
            string requestId = Guid.NewGuid().ToString("N");
            string argsText = SingleInstanceCommandProtocol.FormatArgs(args);

            CommandWaitMode waitMode = ResolveWaitModeFromEnvironment();
            string source = ResolveSourceFromEnvironment();
            int responseTimeoutMs = waitMode == CommandWaitMode.HandoffOnly
                ? SingleInstanceCommandConstants.PipeAckTimeoutMs
                : SingleInstanceCommandConstants.PipeCompletionTimeoutMs;

            LogData.LogLifecycle("FORWARDER", "PIPE_FORWARD_BEGIN", $"RequestId={requestId} Source={source} WaitMode={waitMode} TimeoutMs={responseTimeoutMs} Args=\"{argsText}\"");

            var request = new PipeCommandRequest
            {
                RequestId = requestId,
                SenderProcessId = Process.GetCurrentProcess().Id,
                Arguments = args ?? new string[0],
                Source = source,
                WaitMode = waitMode.ToString()
            };

            Exception lastException = null;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                LogData.LogLifecycle("FORWARDER", "PIPE_CONNECT_ATTEMPT", $"RequestId={requestId} Attempt={attempt}");

                using (var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    try
                    {
                        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            connectCts.CancelAfter(SingleInstanceCommandConstants.PipeConnectTimeoutMs);
                            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                    {
                        lastException = ex;
                        LogData.LogLifecycle("FORWARDER", "PIPE_CONNECT_TIMEOUT", $"RequestId={requestId} Attempt={attempt}");

                        if (attempt < 3)
                        {
                            LogData.LogLifecycle("FORWARDER", "PIPE_CONNECT_RETRY", $"RequestId={requestId} Attempt={attempt}");
                            await Task.Delay(150, token).ConfigureAwait(false);
                            continue;
                        }

                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (attempt < 3)
                        {
                            LogData.LogLifecycle("FORWARDER", "PIPE_CONNECT_RETRY", $"RequestId={requestId} Attempt={attempt}");
                            await Task.Delay(150, token).ConfigureAwait(false);
                            continue;
                        }

                        break;
                    }

                    LogData.LogLifecycle("FORWARDER", "PIPE_CONNECTED", $"RequestId={requestId}");

                    byte[] requestPayload = SingleInstanceCommandProtocol.Serialize(request);
                    await SingleInstanceCommandProtocol.WriteMessageAsync(pipe, requestPayload, token).ConfigureAwait(false);
                    LogData.LogLifecycle("FORWARDER", "PIPE_REQUEST_SENT", $"RequestId={requestId}");

                    PipeCommandResponse response;
                    try
                    {
                        using (var responseCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            responseCts.CancelAfter(responseTimeoutMs);
                            byte[] responsePayload = await SingleInstanceCommandProtocol.ReadMessageAsync(pipe, responseCts.Token).ConfigureAwait(false);
                            response = SingleInstanceCommandProtocol.Deserialize<PipeCommandResponse>(responsePayload);
                        }
                    }
                    catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                    {
                        LogData.LogLifecycle("FORWARDER", "PIPE_ACK_TIMEOUT", $"RequestId={requestId}");
                        return new PipeCommandResponse
                        {
                            RequestId = requestId,
                            Accepted = false,
                            Completed = false,
                            State = PipeCommandStates.Rejected,
                            ExitCode = CommandExitCodes.PipeTimeout,
                            Message = ex.Message
                        };
                    }

                    if (response == null || !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                    {
                        LogData.LogLifecycle("FORWARDER", "PIPE_RESPONSE_INVALID", $"RequestId={requestId}");
                        return new PipeCommandResponse { RequestId = requestId, ExitCode = CommandExitCodes.CommandFailed, Accepted = false, Completed = false, State = PipeCommandStates.Rejected, Message = "Invalid pipe response" };
                    }

                    LogData.LogLifecycle("FORWARDER", "PIPE_ACK_RECEIVED", $"RequestId={requestId} Accepted={response.Accepted} Completed={response.Completed} State={response.State} ExitCode={response.ExitCode}");
                    LogData.LogLifecycle("FORWARDER", "PIPE_FORWARD_SUCCESS", $"RequestId={requestId} ExitCode={response.ExitCode}");
                    return response;
                }
            }

            LogData.LogLifecycle("FORWARDER", "PIPE_CONNECT_FAILED", $"RequestId={requestId} Exception={lastException?.GetType().Name} Message=\"{SingleInstanceCommandProtocol.Escape(lastException?.Message)}\"");
            return new PipeCommandResponse { RequestId = requestId, ExitCode = CommandExitCodes.PipeConnectFailed, Accepted = false, Completed = false, State = PipeCommandStates.Rejected, Message = lastException?.Message ?? "Pipe connect failed" };
        }

        private static CommandWaitMode ResolveWaitModeFromEnvironment()
        {
            string raw = Environment.GetEnvironmentVariable("JOKI_COMMAND_WAIT_MODE");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return CommandWaitMode.WaitForScene;
            }

            if (Enum.TryParse(raw, true, out CommandWaitMode parsed))
            {
                return parsed;
            }

            return CommandWaitMode.WaitForScene;
        }

        private static string ResolveSourceFromEnvironment()
        {
            string source = Environment.GetEnvironmentVariable("JOKI_COMMAND_SOURCE");
            if (string.IsNullOrWhiteSpace(source))
            {
                return "FORWARDER";
            }

            return source.Trim();
        }
    }

    internal sealed class SingleInstanceCommandServer : IDisposable
    {
        private readonly Func<string[], string, int, CancellationToken, Task<int>> _queueExecutor;
        private readonly ICommandDispatcher _dispatcher;
        private readonly string _pipeName;
        private readonly ConcurrentDictionary<string, Task<SceneDispatchResult>> _activeRequests = new ConcurrentDictionary<string, Task<SceneDispatchResult>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, Task> _activeClientHandlers = new ConcurrentDictionary<int, Task>();
        private int _nextClientHandlerId;
        private readonly object _stopSync = new object();
        private CancellationTokenSource _serverCts;
        private Task _serverTask;
        private Task _stopTask;

        public SingleInstanceCommandServer(
            Func<string[], string, int, CancellationToken, Task<int>> queueExecutor,
            ICommandDispatcher dispatcher,
            string pipeName = null)
        {
            _queueExecutor = queueExecutor ?? throw new ArgumentNullException(nameof(queueExecutor));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _pipeName = string.IsNullOrWhiteSpace(pipeName)
                ? SingleInstanceCommandConstants.ResolvePipeName()
                : pipeName;
        }

        public void Start()
        {
            if (_serverTask != null)
            {
                return;
            }

            _serverCts = new CancellationTokenSource();
            LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_START_BEGIN", $"PipeName=\"{_pipeName}\"");
            _serverTask = Task.Run(() => RunAsync(_serverCts.Token));
            LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_STARTED", $"PipeName=\"{_pipeName}\"");
        }

        public Task StopAsync()
        {
            lock (_stopSync)
            {
                if (_stopTask != null)
                {
                    LogData.LogLifecycle(
                        "PIPE_SERVER",
                        "PIPE_SERVER_STOP_ALREADY_STARTED",
                        $"Completed={_stopTask.IsCompleted} Status={_stopTask.Status}");
                    return _stopTask;
                }

                if (_serverCts == null)
                {
                    _stopTask = Task.CompletedTask;
                    return _stopTask;
                }

                _stopTask = StopCoreAsync();
                return _stopTask;
            }
        }

        private async Task StopCoreAsync()
        {
            LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_CANCEL_BEGIN");
            _serverCts.Cancel();
            bool stoppedSuccessfully = false;

            try
            {
                if (_serverTask != null)
                {
                    await _serverTask.ConfigureAwait(false);
                }

                Task[] activeClientHandlers = _activeClientHandlers.Values.ToArray();
                if (activeClientHandlers.Length > 0)
                {
                    await Task.WhenAll(activeClientHandlers).ConfigureAwait(false);
                }

                stoppedSuccessfully = true;
            }
            catch (OperationCanceledException) when (_serverCts != null && _serverCts.IsCancellationRequested)
            {
                stoppedSuccessfully = true;
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_STOP_FAIL",
                    $"Exception={ex.GetType().Name} Message=\"{SingleInstanceCommandProtocol.Escape(ex.Message)}\"");
                throw;
            }
            finally
            {
                _serverTask = null;
                if (_serverCts != null)
                {
                    _serverCts.Dispose();
                    _serverCts = null;
                }

                if (stoppedSuccessfully)
                {
                    LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_STOPPED");
                }
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        SingleInstanceCommandConstants.MaxPipeServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_WAITING");
                    await WaitForConnectionAsync(pipe, token).ConfigureAwait(false);

                    if (!pipe.IsConnected)
                    {
                        pipe.Dispose();
                        continue;
                    }

                    int handlerId = Interlocked.Increment(ref _nextClientHandlerId);
                    Task clientHandlerTask = HandleClientAsync(pipe, CancellationToken.None);
                    _activeClientHandlers[handlerId] = clientHandlerTask;
                    _ = clientHandlerTask.ContinueWith(t =>
                    {
                        _activeClientHandlers.TryRemove(handlerId, out _);
                    }, TaskScheduler.Default);
                }
                catch (OperationCanceledException)
                {
                    pipe?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    pipe?.Dispose();
                    LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{SingleInstanceCommandProtocol.Escape(ex.Message)}\"");
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            string requestId = string.Empty;
            try
            {
                byte[] requestPayload = await SingleInstanceCommandProtocol.ReadMessageAsync(pipe, token).ConfigureAwait(false);
                PipeCommandRequest request = SingleInstanceCommandProtocol.Deserialize<PipeCommandRequest>(requestPayload);
                requestId = request?.RequestId ?? string.Empty;
                int senderPid = request?.SenderProcessId ?? 0;

                LogData.LogLifecycle("PIPE_SERVER", "PIPE_CLIENT_CONNECTED", $"RequestId={requestId} SenderPid={senderPid}");

                if (request == null || string.IsNullOrWhiteSpace(request.RequestId) || request.Arguments == null || request.Arguments.Length == 0)
                {
                    await SendResponseAsync(pipe, new PipeCommandResponse { RequestId = requestId, Accepted = false, Completed = true, State = PipeCommandStates.Rejected, ExitCode = CommandExitCodes.InvalidArguments, Message = "Invalid request" }, CancellationToken.None).ConfigureAwait(false);
                    return;
                }

                string argsText = SingleInstanceCommandProtocol.FormatArgs(request.Arguments);
                string requestSource = string.IsNullOrWhiteSpace(request.Source) ? "PIPE" : request.Source.Trim();
                CommandWaitMode waitMode = ParseWaitMode(request.WaitMode);

                LogData.LogLifecycle("PIPE_SERVER", "PIPE_REQUEST_RECEIVED", $"RequestId={request.RequestId} Source={requestSource} WaitMode={waitMode} Args=\"{argsText}\"");
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_REQUEST_VALIDATED", $"RequestId={request.RequestId} Source={requestSource} WaitMode={waitMode}");

                Task<SceneDispatchResult> dispatchTask;
                bool createdNewTask = false;
                if (!_activeRequests.TryGetValue(request.RequestId, out dispatchTask))
                {
                    LogData.LogLifecycle("PIPE_SERVER", "PIPE_COMMAND_QUEUED", $"RequestId={request.RequestId} Source={requestSource} WaitMode={waitMode}");

                    var dispatchRequest = new SceneDispatchRequest
                    {
                        ExternalRequestId = request.RequestId,
                        Source = requestSource,
                        CommandName = request.Arguments[0],
                        Arguments = request.Arguments,
                        WaitMode = waitMode,
                        CreatedAt = DateTimeOffset.UtcNow
                    };

                    dispatchTask = _dispatcher.DispatchAsync(dispatchRequest, CancellationToken.None);

                    if (_activeRequests.TryAdd(request.RequestId, dispatchTask))
                    {
                        createdNewTask = true;
                    }
                    else
                    {
                        _activeRequests.TryGetValue(request.RequestId, out dispatchTask);
                    }
                }
                else
                {
                    LogData.LogLifecycle("PIPE_SERVER", "DUPLICATE_REQUEST_ID", $"RequestId={request.RequestId}");
                }

                if (waitMode == CommandWaitMode.HandoffOnly)
                {
                    if (dispatchTask.IsCompleted)
                    {
                        SceneDispatchResult immediateResult = await dispatchTask.ConfigureAwait(false);
                        _activeRequests.TryRemove(request.RequestId, out _);
                        await SendResponseAsync(pipe, ToPipeResponse(request.RequestId, immediateResult), CancellationToken.None).ConfigureAwait(false);
                        LogData.LogLifecycle("PIPE_SERVER", "PIPE_RESPONSE_SENT", $"RequestId={request.RequestId} Accepted={immediateResult.Accepted} Completed={immediateResult.Completed} State={immediateResult.State} ExitCode={immediateResult.ExitCode}");
                        return;
                    }

                    if (createdNewTask)
                    {
                        _ = TrackBackgroundDispatchCompletionAsync(request.RequestId, requestSource, waitMode, dispatchTask);
                    }

                    var handoffResponse = new PipeCommandResponse
                    {
                        RequestId = request.RequestId,
                        Accepted = true,
                        Completed = false,
                        State = PipeCommandStates.Executing,
                        ExitCode = CommandExitCodes.Success,
                        Message = "Dispatched"
                    };

                    await SendResponseAsync(pipe, handoffResponse, CancellationToken.None).ConfigureAwait(false);
                    LogData.LogLifecycle("PIPE_SERVER", "PIPE_RESPONSE_SENT", $"RequestId={request.RequestId} Accepted={handoffResponse.Accepted} Completed={handoffResponse.Completed} State={handoffResponse.State} ExitCode={handoffResponse.ExitCode}");
                    return;
                }

                LogData.LogLifecycle("PIPE_SERVER", "PIPE_COMMAND_BEGIN", $"RequestId={request.RequestId} Source={requestSource} WaitMode={waitMode} Args=\"{argsText}\"");
                SceneDispatchResult dispatchResult = await dispatchTask.ConfigureAwait(false);
                _activeRequests.TryRemove(request.RequestId, out _);

                PipeCommandResponse response = ToPipeResponse(request.RequestId, dispatchResult);
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_COMMAND_END", $"RequestId={request.RequestId} Source={requestSource} WaitMode={waitMode} ExitCode={response.ExitCode} State={response.State}");
                await SendResponseAsync(pipe, response, CancellationToken.None).ConfigureAwait(false);
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_RESPONSE_SENT", $"RequestId={request.RequestId} Accepted={response.Accepted} Completed={response.Completed} State={response.State} ExitCode={response.ExitCode}");
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_EXCEPTION", $"RequestId={requestId} Exception={ex.GetType().Name} Message=\"{SingleInstanceCommandProtocol.Escape(ex.Message)}\"");
            }
            finally
            {
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_CLIENT_DISCONNECTED", $"RequestId={requestId}");
                try
                {
                    if (pipe.IsConnected)
                    {
                        pipe.Disconnect();
                    }
                }
                catch { }
                pipe.Dispose();
            }
        }

        private static PipeCommandResponse ToPipeResponse(string requestId, SceneDispatchResult dispatchResult)
        {
            if (dispatchResult == null)
            {
                return new PipeCommandResponse
                {
                    RequestId = requestId,
                    Accepted = false,
                    Completed = true,
                    State = PipeCommandStates.Rejected,
                    ExitCode = CommandExitCodes.CommandFailed,
                    Message = "Dispatch returned no result"
                };
            }

            string state;
            switch (dispatchResult.State)
            {
                case SceneExecutionState.Dispatched:
                case SceneExecutionState.Running:
                    state = PipeCommandStates.Executing;
                    break;
                case SceneExecutionState.Completed:
                case SceneExecutionState.PartiallyFailed:
                case SceneExecutionState.Cancelled:
                case SceneExecutionState.Failed:
                case SceneExecutionState.ShuttingDown:
                    state = dispatchResult.Accepted ? PipeCommandStates.Completed : PipeCommandStates.Rejected;
                    break;
                default:
                    state = dispatchResult.Accepted ? PipeCommandStates.Completed : PipeCommandStates.Rejected;
                    break;
            }

            return new PipeCommandResponse
            {
                RequestId = requestId,
                Accepted = dispatchResult.Accepted,
                Completed = dispatchResult.Completed,
                State = state,
                ExitCode = dispatchResult.ExitCode,
                Message = string.IsNullOrWhiteSpace(dispatchResult.ErrorMessage)
                    ? (dispatchResult.Completed ? "Completed" : "Dispatched")
                    : dispatchResult.ErrorMessage
            };
        }

        private async Task TrackBackgroundDispatchCompletionAsync(
            string requestId,
            string requestSource,
            CommandWaitMode waitMode,
            Task<SceneDispatchResult> dispatchTask)
        {
            SceneDispatchResult completedResult;

            try
            {
                completedResult = await dispatchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                completedResult = new SceneDispatchResult
                {
                    SceneId = Guid.Empty,
                    ExternalRequestId = requestId,
                    Accepted = true,
                    Completed = true,
                    State = SceneExecutionState.Cancelled,
                    ExitCode = CommandExitCodes.CommandCanceled,
                    ErrorType = nameof(OperationCanceledException),
                    ErrorMessage = "Dispatch canceled"
                };
            }
            catch (Exception ex)
            {
                completedResult = new SceneDispatchResult
                {
                    SceneId = Guid.Empty,
                    ExternalRequestId = requestId,
                    Accepted = false,
                    Completed = true,
                    State = SceneExecutionState.Failed,
                    ExitCode = CommandExitCodes.CommandFailed,
                    ErrorType = ex.GetType().Name,
                    ErrorMessage = ex.Message
                };
            }

            _activeRequests.TryRemove(requestId, out _);
            LogData.LogLifecycle("PIPE_SERVER", "PIPE_COMMAND_END", $"RequestId={requestId} Source={requestSource} WaitMode={waitMode} ExitCode={completedResult.ExitCode} Background=True State={completedResult.State}");
        }

        private static async Task SendResponseAsync(Stream pipe, PipeCommandResponse response, CancellationToken token)
        {
            byte[] payload = SingleInstanceCommandProtocol.Serialize(response);
            await SingleInstanceCommandProtocol.WriteMessageAsync(pipe, payload, token).ConfigureAwait(false);
        }

        private static CommandWaitMode ParseWaitMode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return CommandWaitMode.WaitForScene;
            }

            if (Enum.TryParse(raw, true, out CommandWaitMode parsed))
            {
                return parsed;
            }

            return CommandWaitMode.WaitForScene;
        }

        private static async Task WaitForConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            Task waitTask = pipe.WaitForConnectionAsync();
            Task cancelTask = Task.Delay(Timeout.Infinite, token);
            Task completed = await Task.WhenAny(waitTask, cancelTask).ConfigureAwait(false);
            if (completed == cancelTask)
            {
                pipe.Dispose();
                token.ThrowIfCancellationRequested();
            }

            await waitTask.ConfigureAwait(false);
        }

        public void Dispose()
        {
            LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_DISPOSE_BEGIN");

            try
            {
                Task stopTask = StopAsync();
                bool stopped = stopTask.Wait(TimeSpan.FromSeconds(2));
                if (!stopped)
                {
                    LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_DISPOSE_TIMEOUT", "TimeoutMs=2000");
                }
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_DISPOSE_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{SingleInstanceCommandProtocol.Escape(ex.Message)}\"");
            }
            finally
            {
                LogData.LogLifecycle("PIPE_SERVER", "PIPE_SERVER_DISPOSE_END");
            }
        }

        public string GetListenerTaskState()
        {
            Task listenerTask = _serverTask;
            if (listenerTask == null)
            {
                return "None";
            }

            return listenerTask.Status.ToString();
        }
    }

    internal class NetworkDevice
    {
        public string IPAddress { get; set; }
        public int Port { get; set; }
    }

    internal enum CommandPipeline
    {
        Audio,
        Video,
        Position,
        Ui
    }

    internal enum PipelineCommandState
    {
        Queued,
        Started,
        Completed,
        Failed,
        Cancelled,
        Superseded
    }

    internal enum SceneExecutionState
    {
        Dispatched,
        Running,
        Completed,
        PartiallyFailed,
        Failed,
        Cancelled,
        ShuttingDown
    }

    internal enum CommandWaitMode
    {
        HandoffOnly,
        WaitForScene
    }

    internal sealed class PipelineCommand
    {
        public Guid RequestId { get; set; }
        public Guid SceneId { get; set; }
        public CommandPipeline Pipeline { get; set; }
        public string CommandName { get; set; }
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();
        public DateTimeOffset CreatedAt { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }

    internal sealed class PipelineCommandResult
    {
        public Guid RequestId { get; set; }
        public Guid SceneId { get; set; }
        public CommandPipeline Pipeline { get; set; }
        public PipelineCommandState State { get; set; }
        public int ExitCode { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public long ElapsedMs { get; set; }
    }

    internal sealed class ScenePlan
    {
        public Guid SceneId { get; set; }
        public string SceneName { get; set; }
        public IReadOnlyList<PipelineCommand> AudioCommands { get; set; } = Array.Empty<PipelineCommand>();
        public IReadOnlyList<PipelineCommand> VideoCommands { get; set; } = Array.Empty<PipelineCommand>();
        public IReadOnlyList<PipelineCommand> PositionCommands { get; set; } = Array.Empty<PipelineCommand>();
        public IReadOnlyList<PipelineCommand> UiCommands { get; set; } = Array.Empty<PipelineCommand>();
    }

    internal sealed class SceneDispatchRequest
    {
        public string ExternalRequestId { get; set; }
        public string Source { get; set; }
        public string CommandName { get; set; }
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();
        public CommandWaitMode WaitMode { get; set; } = CommandWaitMode.WaitForScene;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    internal sealed class SceneDispatchResult
    {
        public Guid SceneId { get; set; }
        public string ExternalRequestId { get; set; }
        public bool Accepted { get; set; }
        public bool Completed { get; set; }
        public SceneExecutionState State { get; set; }
        public int ExitCode { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal interface ICommandDispatcher
    {
        Task<SceneDispatchResult> DispatchAsync(SceneDispatchRequest request, CancellationToken cancellationToken);
        Task StopAsync();
    }

    internal sealed class CommandDispatcher : ICommandDispatcher, IDisposable
    {
        private sealed class PositionQueueItem
        {
            public PipelineCommand Command { get; set; }
            public string Source { get; set; }
            public CancellationToken CancellationToken { get; set; }
            public TaskCompletionSource<PipelineCommandResult> Completion { get; set; }
        }

        private readonly ConcurrentDictionary<Guid, SceneExecutionContext> _sceneContexts = new ConcurrentDictionary<Guid, SceneExecutionContext>();
        private readonly Func<SceneDispatchRequest, CancellationToken, ScenePlan> _scenePlanner;
        private readonly object _dispatchLifecycleSync = new object();
        private int _acceptingRequests = 1;
        private int _activeDispatchCalls;
        private TaskCompletionSource<bool> _dispatchCallsIdle = CreateCompletedIdleSource();
        private readonly Func<PipelineCommand, Task<int>> _audioExecutor;
        private readonly Func<PipelineCommand, Task<int>> _videoExecutor;
        private readonly Func<PipelineCommand, Task<int>> _positionExecutor;
        private readonly Func<PipelineCommand, Task<int>> _uiExecutor;
        private readonly BlockingCollection<PositionQueueItem> _positionQueue = new BlockingCollection<PositionQueueItem>(new ConcurrentQueue<PositionQueueItem>());
        private readonly CancellationTokenSource _dispatcherCts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Guid, Task> _backgroundSceneTasks = new ConcurrentDictionary<Guid, Task>();
        private readonly Task _positionWorker;
        private readonly object _stopSync = new object();
        private Task _stopTask;
        private int _disposed;

        public CommandDispatcher(
            Func<SceneDispatchRequest, CancellationToken, ScenePlan> scenePlanner,
            Func<PipelineCommand, Task<int>> audioExecutor,
            Func<PipelineCommand, Task<int>> videoExecutor,
            Func<PipelineCommand, Task<int>> positionExecutor,
            Func<PipelineCommand, Task<int>> uiExecutor)
        {
            _scenePlanner = scenePlanner ?? throw new ArgumentNullException(nameof(scenePlanner));
            _audioExecutor = audioExecutor ?? throw new ArgumentNullException(nameof(audioExecutor));
            _videoExecutor = videoExecutor ?? throw new ArgumentNullException(nameof(videoExecutor));
            _positionExecutor = positionExecutor ?? throw new ArgumentNullException(nameof(positionExecutor));
            _uiExecutor = uiExecutor ?? throw new ArgumentNullException(nameof(uiExecutor));

            _positionWorker = Task.Run(() => RunPositionWorkerAsync(_dispatcherCts.Token));
        }

        private static TaskCompletionSource<bool> CreateCompletedIdleSource()
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.TrySetResult(true);
            return source;
        }

        private bool TryEnterDispatch()
        {
            lock (_dispatchLifecycleSync)
            {
                if (_acceptingRequests == 0)
                {
                    return false;
                }
                if (_activeDispatchCalls == 0)
                {
                    _dispatchCallsIdle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                _activeDispatchCalls++;
                return true;
            }
        }

        private void ExitDispatch()
        {
            TaskCompletionSource<bool> idleSource = null;
            lock (_dispatchLifecycleSync)
            {
                _activeDispatchCalls--;
                if (_activeDispatchCalls < 0)
                {
                    _activeDispatchCalls = 0;
                    throw new InvalidOperationException("Active dispatcher call count became negative.");
                }
                if (_activeDispatchCalls == 0)
                {
                    idleSource = _dispatchCallsIdle;
                }
            }
            idleSource?.TrySetResult(true);
        }

        public async Task<SceneDispatchResult> DispatchAsync(SceneDispatchRequest request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CommandName))
            {
                return new SceneDispatchResult
                {
                    SceneId = Guid.Empty,
                    ExternalRequestId = request?.ExternalRequestId,
                    Accepted = false,
                    Completed = true,
                    State = SceneExecutionState.Failed,
                    ExitCode = CommandExitCodes.InvalidArguments,
                    ErrorType = nameof(ArgumentException),
                    ErrorMessage = "Invalid dispatch request"
                };
            }

            if (Program.IsShutdownInProgress || !TryEnterDispatch())
            {
                return new SceneDispatchResult
                {
                    SceneId = Guid.Empty,
                    ExternalRequestId = request.ExternalRequestId,
                    Accepted = false,
                    Completed = true,
                    State = SceneExecutionState.ShuttingDown,
                    ExitCode = CommandExitCodes.CommandFailed,
                    ErrorType = "Shutdown",
                    ErrorMessage = "Dispatcher is shutting down"
                };
            }

            try
            {
            ScenePlan scenePlan = _scenePlanner(request, cancellationToken);
            if (scenePlan == null)
            {
                return new SceneDispatchResult
                {
                    SceneId = Guid.Empty,
                    ExternalRequestId = request.ExternalRequestId,
                    Accepted = false,
                    Completed = true,
                    State = SceneExecutionState.Failed,
                    ExitCode = CommandExitCodes.InvalidArguments,
                    ErrorType = "ScenePlan",
                    ErrorMessage = "Scene plan could not be created"
                };
            }

            var context = new SceneExecutionContext(scenePlan.SceneId, request.ExternalRequestId, scenePlan.SceneName);
            _sceneContexts[scenePlan.SceneId] = context;

            int audioCount = scenePlan.AudioCommands?.Count ?? 0;
            int videoCount = scenePlan.VideoCommands?.Count ?? 0;
            int positionCount = scenePlan.PositionCommands?.Count ?? 0;
            int uiCount = scenePlan.UiCommands?.Count ?? 0;

            context.InitializePending(audioCount + videoCount + positionCount + uiCount);

            LogData.LogLifecycle(request.Source ?? "UNKNOWN", "SCENE_BEGIN",
                $"SceneId={scenePlan.SceneId} ExternalRequestId={request.ExternalRequestId} Command={scenePlan.SceneName} WaitMode={request.WaitMode} Source={request.Source ?? "UNKNOWN"} CreatedAt={request.CreatedAt:O} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            LogData.LogLifecycle(request.Source ?? "UNKNOWN", "DISPATCH_BEGIN",
                $"SceneId={scenePlan.SceneId} ExternalRequestId={request.ExternalRequestId} Command={scenePlan.SceneName} WaitMode={request.WaitMode} Source={request.Source ?? "UNKNOWN"} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            LogData.LogLifecycle(request.Source ?? "UNKNOWN", "SCENE_PLAN",
                $"SceneId={scenePlan.SceneId} ExternalRequestId={request.ExternalRequestId} AudioCount={audioCount} VideoCount={videoCount} PositionCount={positionCount} UiCount={uiCount}");

            if (request.WaitMode == CommandWaitMode.HandoffOnly)
            {
                lock (_dispatchLifecycleSync)
                {
                    // TryEnterDispatch admitted this call. Stop waits for admitted calls
                    // before taking the background-task snapshot.
                    Task backgroundSceneTask = RunBackgroundSceneAsync(scenePlan, context, request.Source, cancellationToken);
                    _backgroundSceneTasks[scenePlan.SceneId] = backgroundSceneTask;
                }

                return new SceneDispatchResult
                {
                    SceneId = scenePlan.SceneId,
                    ExternalRequestId = request.ExternalRequestId,
                    Accepted = true,
                    Completed = false,
                    State = SceneExecutionState.Dispatched,
                    ExitCode = CommandExitCodes.Success
                };
            }

            await ExecutePipelineCommandsSequentially(scenePlan, context, request.Source, cancellationToken).ConfigureAwait(false);
            SceneDispatchResult final = await context.Completion.Task.ConfigureAwait(false);
            return final;
            }
            finally
            {
                ExitDispatch();
            }
        }

        public Task StopAsync()
        {
            lock (_stopSync)
            {
                if (_stopTask != null)
                {
                    return _stopTask;
                }

                _stopTask = StopCoreAsync();
                return _stopTask;
            }
        }

        private async Task StopCoreAsync()
        {
            if (_dispatcherCts.IsCancellationRequested)
            {
                return;
            }

            Task dispatchCallsIdleTask;
            lock (_dispatchLifecycleSync)
            {
                _acceptingRequests = 0;
                dispatchCallsIdleTask = _activeDispatchCalls == 0
                    ? Task.CompletedTask
                    : _dispatchCallsIdle.Task;
            }

            await dispatchCallsIdleTask.ConfigureAwait(false);

            Task[] backgroundTasks;
            lock (_dispatchLifecycleSync)
            {
                backgroundTasks = _backgroundSceneTasks.Values.ToArray();
            }

            if (backgroundTasks.Length > 0)
            {
                await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
            }

            _positionQueue.CompleteAdding();

            if (_positionWorker != null)
            {
                try
                {
                    await _positionWorker.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _dispatcherCts.Cancel();
        }

        private async Task RunPositionWorkerAsync(CancellationToken token)
        {
            while (true)
            {
                PositionQueueItem queueItem;
                try
                {
                    queueItem = _positionQueue.Take(token);
                }
                catch (OperationCanceledException)
                {
                    if (_positionQueue.IsCompleted)
                    {
                        break;
                    }

                    throw;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                try
                {
                    LogData.LogLifecycle("DISPATCHER", "POSITION_START",
                        $"SceneId={queueItem.Command.SceneId} RequestId={queueItem.Command.RequestId} Command={queueItem.Command.CommandName} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");

                    PipelineCommandResult result = await ExecuteSingleCommandAsync(
                        queueItem.Command,
                        _positionExecutor,
                        queueItem.Source,
                        CommandPipeline.Position,
                        queueItem.CancellationToken).ConfigureAwait(false);

                    if (result.State == PipelineCommandState.Completed)
                    {
                        LogData.LogLifecycle("DISPATCHER", "POSITION_END",
                            $"SceneId={queueItem.Command.SceneId} RequestId={queueItem.Command.RequestId} Command={queueItem.Command.CommandName} ExitCode={result.ExitCode} ElapsedMs={result.ElapsedMs}");
                    }
                    else
                    {
                        LogData.LogLifecycle("DISPATCHER", "POSITION_FAIL",
                            $"SceneId={queueItem.Command.SceneId} RequestId={queueItem.Command.RequestId} Command={queueItem.Command.CommandName} ExitCode={result.ExitCode} ErrorType={result.ErrorType} Error={SingleInstanceCommandProtocol.Escape(result.ErrorMessage)} ElapsedMs={result.ElapsedMs}");
                    }

                    queueItem.Completion.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    LogData.LogLifecycle("DISPATCHER", "POSITION_FAIL",
                        $"SceneId={queueItem.Command.SceneId} RequestId={queueItem.Command.RequestId} Command={queueItem.Command.CommandName} ExitCode={CommandExitCodes.CommandCanceled} ErrorType=OperationCanceledException Error=Position command cancelled ElapsedMs=0");

                    queueItem.Completion.TrySetResult(new PipelineCommandResult
                    {
                        RequestId = queueItem.Command.RequestId,
                        SceneId = queueItem.Command.SceneId,
                        Pipeline = CommandPipeline.Position,
                        State = PipelineCommandState.Cancelled,
                        ExitCode = CommandExitCodes.CommandCanceled,
                        ErrorType = nameof(OperationCanceledException),
                        ErrorMessage = "Position command cancelled"
                    });
                }
                catch (Exception ex)
                {
                    LogData.LogLifecycle("DISPATCHER", "POSITION_FAIL",
                        $"SceneId={queueItem.Command.SceneId} RequestId={queueItem.Command.RequestId} Command={queueItem.Command.CommandName} ExitCode={CommandExitCodes.CommandFailed} ErrorType={ex.GetType().Name} Error={SingleInstanceCommandProtocol.Escape(ex.Message)} ElapsedMs=0");

                    queueItem.Completion.TrySetResult(new PipelineCommandResult
                    {
                        RequestId = queueItem.Command.RequestId,
                        SceneId = queueItem.Command.SceneId,
                        Pipeline = CommandPipeline.Position,
                        State = PipelineCommandState.Failed,
                        ExitCode = CommandExitCodes.CommandFailed,
                        ErrorType = ex.GetType().Name,
                        ErrorMessage = ex.Message
                    });
                }
            }
        }

        private async Task RunBackgroundSceneAsync(
            ScenePlan scenePlan,
            SceneExecutionContext context,
            string source,
            CancellationToken cancellationToken)
        {
            try
            {
                await ExecutePipelineCommandsSequentially(scenePlan, context, source, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle("DISPATCHER", "SCENE_EXECUTION_FAIL",
                    $"SceneId={scenePlan.SceneId} Scene={scenePlan.SceneName} ErrorType={ex.GetType().Name} Error={SingleInstanceCommandProtocol.Escape(ex.Message)}");
            }
            finally
            {
                _backgroundSceneTasks.TryRemove(scenePlan.SceneId, out _);
            }
        }

        private async Task ExecutePipelineCommandsSequentially(
            ScenePlan scenePlan,
            SceneExecutionContext context,
            string source,
            CancellationToken cancellationToken)
        {
            if (context.PendingCommands == 0)
            {
                context.TryCompleteIfDone();
                return;
            }

            await ExecuteCommandListAsync(scenePlan.AudioCommands, _audioExecutor, source, CommandPipeline.Audio, context, cancellationToken).ConfigureAwait(false);
            await ExecuteCommandListAsync(scenePlan.VideoCommands, _videoExecutor, source, CommandPipeline.Video, context, cancellationToken).ConfigureAwait(false);
            await ExecuteCommandListAsync(scenePlan.PositionCommands, _positionExecutor, source, CommandPipeline.Position, context, cancellationToken).ConfigureAwait(false);
            await ExecuteCommandListAsync(scenePlan.UiCommands, _uiExecutor, source, CommandPipeline.Ui, context, cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteCommandListAsync(
            IReadOnlyList<PipelineCommand> commands,
            Func<PipelineCommand, Task<int>> executor,
            string source,
            CommandPipeline pipeline,
            SceneExecutionContext context,
            CancellationToken cancellationToken)
        {
            if (commands == null || commands.Count == 0)
            {
                return;
            }

            foreach (PipelineCommand command in commands)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PipelineCommandResult result;
                if (pipeline == CommandPipeline.Position)
                {
                    result = await EnqueueAndWaitPositionCommandAsync(command, source, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await ExecuteSingleCommandAsync(command, executor, source, pipeline, cancellationToken).ConfigureAwait(false);
                }

                context.AddResult(result);
            }
        }

        private async Task<PipelineCommandResult> EnqueueAndWaitPositionCommandAsync(
            PipelineCommand command,
            string source,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<PipelineCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            string normalizedSource = source ?? "UNKNOWN";

            LogData.LogLifecycle("DISPATCHER", "POSITION_QUEUE",
                $"SceneId={command.SceneId} RequestId={command.RequestId} Command={command.CommandName} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");

            var item = new PositionQueueItem
            {
                Command = command,
                Source = normalizedSource,
                CancellationToken = cancellationToken,
                Completion = completion
            };

            _positionQueue.Add(item, cancellationToken);
            return await completion.Task.ConfigureAwait(false);
        }

        private async Task<PipelineCommandResult> ExecuteSingleCommandAsync(
            PipelineCommand command,
            Func<PipelineCommand, Task<int>> executor,
            string source,
            CommandPipeline pipeline,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            string normalizedSource = source ?? "UNKNOWN";
            string pipelineTag = GetPipelineLogTag(pipeline);

            LogData.LogLifecycle(normalizedSource, "PIPELINE_QUEUE",
                $"SceneId={command.SceneId} RequestId={command.RequestId} Pipeline={pipeline} Command={command.CommandName} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            LogData.LogLifecycle("DISPATCHER", "PIPELINE_START",
                $"SceneId={command.SceneId} RequestId={command.RequestId} Pipeline={pipeline} Command={command.CommandName} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            LogData.LogLifecycle("DISPATCHER", pipelineTag + "_BEGIN",
                $"SceneId={command.SceneId} RequestId={command.RequestId} Command={command.CommandName} Pipeline={pipeline} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");

            try
            {
                int exitCode = await executor(command).ConfigureAwait(false);
                stopwatch.Stop();

                PipelineCommandState state = exitCode == CommandExitCodes.Success
                    ? PipelineCommandState.Completed
                    : PipelineCommandState.Failed;

                var result = new PipelineCommandResult
                {
                    RequestId = command.RequestId,
                    SceneId = command.SceneId,
                    Pipeline = pipeline,
                    State = state,
                    ExitCode = exitCode,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    ErrorType = state == PipelineCommandState.Failed ? "ExitCode" : null,
                    ErrorMessage = state == PipelineCommandState.Failed ? $"Command returned exit code {exitCode}" : null
                };

                if (state == PipelineCommandState.Completed)
                {
                    LogData.LogLifecycle("DISPATCHER", "PIPELINE_END",
                        $"SceneId={command.SceneId} RequestId={command.RequestId} Pipeline={pipeline} State=Completed ExitCode={exitCode} ElapsedMs={stopwatch.ElapsedMilliseconds}");
                    LogData.LogLifecycle("DISPATCHER", pipelineTag + "_END",
                        $"SceneId={command.SceneId} RequestId={command.RequestId} Command={command.CommandName} ExitCode={exitCode} ElapsedMs={stopwatch.ElapsedMilliseconds}");
                }
                else
                {
                    LogData.LogLifecycle("DISPATCHER", "PIPELINE_FAIL",
                        $"SceneId={command.SceneId} RequestId={command.RequestId} Pipeline={pipeline} ExitCode={exitCode} ErrorType=ExitCode ElapsedMs={stopwatch.ElapsedMilliseconds}");
                    LogData.LogLifecycle("DISPATCHER", pipelineTag + "_FAIL",
                        $"SceneId={command.SceneId} RequestId={command.RequestId} Command={command.CommandName} ExitCode={exitCode} ErrorType=ExitCode ElapsedMs={stopwatch.ElapsedMilliseconds}");
                }

                if (pipeline == CommandPipeline.Position && exitCode == 99)
                {
                    string parameter = command.Arguments != null && command.Arguments.Count > 1 ? command.Arguments[1] : "NA";
                    string ip = command.Arguments != null && command.Arguments.Count > 2 ? command.Arguments[2] : "NA";

                    LogData.LogLifecycle("DISPATCHER", "POSITION_EXIT_99",
                        $"SceneId={command.SceneId} RequestId={command.RequestId} Command={command.CommandName} Parameter={SingleInstanceCommandProtocol.Escape(parameter)} IP={SingleInstanceCommandProtocol.Escape(ip)} ExitCode=99 StdOut=Unavailable StdErr=Unavailable");
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                LogData.LogLifecycle("DISPATCHER", "PIPELINE_FAIL",
                    $"SceneId={command.SceneId} RequestId={command.RequestId} Pipeline={pipeline} ExitCode={CommandExitCodes.CommandCanceled} ErrorType=OperationCanceledException ElapsedMs={stopwatch.ElapsedMilliseconds}");
                LogData.LogLifecycle("DISPATCHER", pipelineTag + "_FAIL",
                    $"SceneId={command.SceneId} RequestId={command.RequestId} Command={command.CommandName} ExitCode={CommandExitCodes.CommandCanceled} ErrorType=OperationCanceledException ElapsedMs={stopwatch.ElapsedMilliseconds}");

                return new PipelineCommandResult
                {
                    RequestId = command.RequestId,
                    SceneId = command.SceneId,
                    Pipeline = pipeline,
                    State = PipelineCommandState.Cancelled,
                    ExitCode = CommandExitCodes.CommandCanceled,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    ErrorType = nameof(OperationCanceledException),
                    ErrorMessage = "Pipeline command cancelled"
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                LogData.LogLifecycle("DISPATCHER", "PIPELINE_FAIL",
                    $"SceneId={command.SceneId} RequestId={command.RequestId} Pipeline={pipeline} ExitCode={CommandExitCodes.CommandFailed} ErrorType={ex.GetType().Name} Error={SingleInstanceCommandProtocol.Escape(ex.Message)} ElapsedMs={stopwatch.ElapsedMilliseconds}");
                LogData.LogLifecycle("DISPATCHER", pipelineTag + "_FAIL",
                    $"SceneId={command.SceneId} RequestId={command.RequestId} Command={command.CommandName} ExitCode={CommandExitCodes.CommandFailed} ErrorType={ex.GetType().Name} Error={SingleInstanceCommandProtocol.Escape(ex.Message)} ElapsedMs={stopwatch.ElapsedMilliseconds}");

                return new PipelineCommandResult
                {
                    RequestId = command.RequestId,
                    SceneId = command.SceneId,
                    Pipeline = pipeline,
                    State = PipelineCommandState.Failed,
                    ExitCode = CommandExitCodes.CommandFailed,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    ErrorType = ex.GetType().Name,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static string GetPipelineLogTag(CommandPipeline pipeline)
        {
            switch (pipeline)
            {
                case CommandPipeline.Audio:
                    return "AUDIO";
                case CommandPipeline.Video:
                    return "VIDEO";
                case CommandPipeline.Position:
                    return "POSITION";
                case CommandPipeline.Ui:
                    return "UI";
                default:
                    return "PIPELINE";
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                Task stopTask = StopAsync();
                if (!stopTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    LogData.LogLifecycle("DISPATCHER", "DISPATCHER_DISPOSE_STOP_TIMEOUT");
                }
            }
            catch (Exception ex)
            {
                LogData.LogLifecycle("DISPATCHER", "DISPATCHER_DISPOSE_STOP_EXCEPTION", $"Exception={ex.GetType().Name} Message=\"{SingleInstanceCommandProtocol.Escape(ex.Message)}\"");
            }
            finally
            {
                try
                {
                    _positionQueue.Dispose();
                }
                catch
                {
                }

                _dispatcherCts.Dispose();
            }
        }

        private sealed class SceneExecutionContext
        {
            private readonly object _sync = new object();
            private readonly List<PipelineCommandResult> _results = new List<PipelineCommandResult>();
            private readonly Stopwatch _sceneStopwatch = Stopwatch.StartNew();

            public SceneExecutionContext(Guid sceneId, string externalRequestId, string sceneName)
            {
                SceneId = sceneId;
                ExternalRequestId = externalRequestId;
                SceneName = sceneName ?? string.Empty;
                Completion = new TaskCompletionSource<SceneDispatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Guid SceneId { get; }
            public string ExternalRequestId { get; }
            public string SceneName { get; }
            public int PendingCommands { get; private set; }
            public TaskCompletionSource<SceneDispatchResult> Completion { get; }

            public void InitializePending(int count)
            {
                lock (_sync)
                {
                    PendingCommands = count;
                }
            }

            public void AddResult(PipelineCommandResult result)
            {
                lock (_sync)
                {
                    _results.Add(result);
                    if (PendingCommands > 0)
                    {
                        PendingCommands--;
                    }
                }

                TryCompleteIfDone();
            }

            public void TryCompleteIfDone()
            {
                lock (_sync)
                {
                    if (PendingCommands != 0 || Completion.Task.IsCompleted)
                    {
                        return;
                    }

                    _sceneStopwatch.Stop();

                    bool anyFailed = _results.Exists(r => r.State == PipelineCommandState.Failed);
                    bool anyCancelled = _results.Exists(r => r.State == PipelineCommandState.Cancelled);
                    bool anyCompleted = _results.Exists(r => r.State == PipelineCommandState.Completed);

                    SceneExecutionState state;
                    int exitCode;

                    if (anyFailed)
                    {
                        state = anyCompleted ? SceneExecutionState.PartiallyFailed : SceneExecutionState.Failed;
                        PipelineCommandResult firstFailed = _results.Find(r => r.State == PipelineCommandState.Failed);
                        exitCode = firstFailed?.ExitCode ?? CommandExitCodes.CommandFailed;
                    }
                    else if (anyCancelled)
                    {
                        state = SceneExecutionState.Cancelled;
                        exitCode = CommandExitCodes.CommandCanceled;
                    }
                    else
                    {
                        state = SceneExecutionState.Completed;
                        exitCode = CommandExitCodes.Success;
                    }

                    int? audioExitCode = GetExitCodeForPipeline(CommandPipeline.Audio);
                    int? videoExitCode = GetExitCodeForPipeline(CommandPipeline.Video);
                    int? positionExitCode = GetExitCodeForPipeline(CommandPipeline.Position);
                    int? uiExitCode = GetExitCodeForPipeline(CommandPipeline.Ui);

                    LogData.LogLifecycle("DISPATCHER", "SCENE_END",
                        $"SceneId={SceneId} ExternalRequestId={ExternalRequestId} Scene={SceneName} State={state} ExitCode={exitCode} AudioExitCode={FormatPipelineExitCode(audioExitCode)} VideoExitCode={FormatPipelineExitCode(videoExitCode)} PositionExitCode={FormatPipelineExitCode(positionExitCode)} UiExitCode={FormatPipelineExitCode(uiExitCode)} ElapsedMs={_sceneStopwatch.ElapsedMilliseconds}");

                    Completion.TrySetResult(new SceneDispatchResult
                    {
                        SceneId = SceneId,
                        ExternalRequestId = ExternalRequestId,
                        Accepted = true,
                        Completed = true,
                        State = state,
                        ExitCode = exitCode,
                        ErrorType = anyFailed ? "PipelineFailed" : null,
                        ErrorMessage = anyFailed ? "One or more pipeline commands failed" : null
                    });
                }
            }

            private int? GetExitCodeForPipeline(CommandPipeline pipeline)
            {
                PipelineCommandResult last = _results.LastOrDefault(r => r.Pipeline == pipeline);
                return last?.ExitCode;
            }

            private static string FormatPipelineExitCode(int? exitCode)
            {
                return exitCode.HasValue ? exitCode.Value.ToString() : "NA";
            }
        }
    }
}
