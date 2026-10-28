using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace JokiAutomation.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class LifecycleRegressionTests
    {
        private string _exePath = string.Empty;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _exePath = ResolveExePath();
            Assert.That(File.Exists(_exePath), Is.True,
                "JokiAutomation.exe not found. Set environment variable JOKI_EXE to the built executable.");
        }

        [Test]
        [Category("Lifecycle")]
        public async Task PipeServer_StopWithoutClient_Completes()
        {
            string suffix = Guid.NewGuid().ToString("N");
            ProcessResult result = await RunAsync(
                $"--stop-without-client --mutex=Global\\JokiAutomation_Test_{suffix} --pipe=JokiAutomation.Test.{suffix} --timeoutMs=5000",
                TimeSpan.FromSeconds(15));

            Assert.That(result.ExitCode, Is.EqualTo(0), result.Combined);
            AssertContainsInOrder(result.Combined,
                "TEST_MUTEX_ACQUIRED",
                "TEST_PIPE_SERVER_STARTED",
                "TEST_SHUTDOWN_BEGIN",
                "TEST_PIPE_SERVER_STOPPED",
                "TEST_PIPE_SERVER_DISPOSED",
                "TEST_MUTEX_RELEASED",
                "TEST_PROCESS_EXIT");
            Assert.That(result.Combined, Does.Not.Contain("TEST_PIPE_SERVER_STOP_TIMEOUT"));
        }

        [Test]
        [Category("Lifecycle")]
        public async Task PipeServer_StopWithSilentClient_CompletesOrFailsExplicitly()
        {
            string suffix = Guid.NewGuid().ToString("N");
            ProcessResult result = await RunAsync(
                $"--stop-with-silent-client --mutex=Global\\JokiAutomation_Test_{suffix} --pipe=JokiAutomation.Test.{suffix} --timeoutMs=5000",
                TimeSpan.FromSeconds(15));

            Assert.That(result.TimedOut, Is.False, result.Combined);
            Assert.That(result.Combined, Does.Contain("TEST_PROCESS_EXIT"));
        }

        [Test]
        [Category("Lifecycle")]
        public async Task PipeServer_DoubleStop_ReturnsSameStopTask()
        {
            string suffix = Guid.NewGuid().ToString("N");
            ProcessResult result = await RunAsync(
                $"--double-stop --mutex=Global\\JokiAutomation_Test_{suffix} --pipe=JokiAutomation.Test.{suffix} --timeoutMs=5000",
                TimeSpan.FromSeconds(15));

            Assert.That(result.ExitCode, Is.EqualTo(0), result.Combined);
            Assert.That(result.Combined, Does.Contain("TEST_STOP_TASK_SAME"));
            Assert.That(result.Combined, Does.Contain("TEST_PIPE_SERVER_STOPPED"));
        }

        [Test]
        [Category("Lifecycle")]
        public async Task Mutex_CanBeAcquiredAfterCleanTestHostExit()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string mutex = $"Global\\JokiAutomation_Test_{suffix}";
            string pipe = $"JokiAutomation.Test.{suffix}";

            ProcessResult first = await RunAsync(
                $"--stop-without-client --mutex={mutex} --pipe={pipe} --timeoutMs=5000",
                TimeSpan.FromSeconds(15));
            Assert.That(first.ExitCode, Is.EqualTo(0), first.Combined);

            ProcessResult second = await RunAsync(
                $"--try-acquire-mutex --mutex={mutex} --timeoutMs=3000",
                TimeSpan.FromSeconds(10));

            Assert.That(second.ExitCode, Is.EqualTo(0), second.Combined);
            Assert.That(second.Combined, Does.Contain("TEST_MUTEX_REACQUIRED"));
        }

        [Test]
        [Category("Lifecycle")]
        public async Task PipeServer_MultipleConcurrentForwarders_AllReceiveResponses()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string pipe = $"JokiAutomation.Test.Multi.{suffix}";
            string exeDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty;
            string logPath = Path.Combine(exeDirectory, "RasPiAutomationLog.txt");
            long beforeLength = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

            ProcessResult result = await RunAsync(
                $"--pipe-concurrency-smoke --pipe={pipe} --clients=6 --commandDelayMs=350 --launchDelayMs=50 --timeoutMs=20000",
                TimeSpan.FromSeconds(30));

            Assert.That(result.TimedOut, Is.False, result.Combined);
            Assert.That(result.ExitCode, Is.EqualTo(0), result.Combined);
            Assert.That(CountOccurrences(result.Combined, "TEST_CLIENT_RESULT Command="), Is.EqualTo(6), result.Combined);

            for (int i = 1; i <= 6; i++)
            {
                Assert.That(result.Combined, Does.Contain($"TEST_COMMAND_COUNT Command=BurstTest_{i:00} Count=1"), result.Combined);
            }

            Assert.That(CountOccurrences(result.Combined, "TEST_REQUEST_COUNT RequestId="), Is.EqualTo(6), result.Combined);

            string newLog = ReadAppendedLog(logPath, beforeLength);
            Assert.That(CountOccurrences(newLog, "Event=PIPE_CONNECTED"), Is.EqualTo(6), newLog);
            Assert.That(newLog, Does.Not.Contain("Event=PIPE_CONNECT_TIMEOUT"), newLog);
            Assert.That(newLog, Does.Not.Contain("Event=PIPE_ACK_TIMEOUT"), newLog);
            Assert.That(newLog, Does.Not.Contain("Event=PIPE_CONNECT_FAILED"), newLog);
        }

        [Test]
        [Category("Lifecycle")]
        public async Task Forwarder_ConnectTimeout_IsClassifiedAsConnectFailure()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string pipe = $"JokiAutomation.Test.NoServer.{suffix}";
            string exeDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty;
            string logPath = Path.Combine(exeDirectory, "RasPiAutomationLog.txt");
            long beforeLength = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

            ProcessResult result = await RunAsync(
                $"--forward-connect-timeout --pipe={pipe} --timeoutMs=15000",
                TimeSpan.FromSeconds(20));

            Assert.That(result.TimedOut, Is.False, result.Combined);
            Assert.That(result.ExitCode, Is.EqualTo(0), result.Combined);
            Assert.That(result.Combined, Does.Contain("TEST_FORWARD_EXITCODE 4"), result.Combined);

            string newLog = ReadAppendedLog(logPath, beforeLength);
            Assert.That(newLog, Does.Contain("Event=PIPE_CONNECT_TIMEOUT"), newLog);
            Assert.That(newLog, Does.Contain("Event=PIPE_CONNECT_FAILED"), newLog);
            Assert.That(newLog, Does.Not.Contain("Event=PIPE_ACK_TIMEOUT"), newLog);
            Assert.That(newLog, Does.Not.Contain("Event=PIPE_RESPONSE_TIMEOUT"), newLog);
        }

        [Test]
        [Category("Lifecycle")]
        public async Task Forwarder_DuringPrimaryShutdown_DoesNotEndWithConnectTimeout()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string mutexName = $@"Global\JokiAutomation_Test_{suffix}";
            string pipeName = $"JokiAutomation.Test.Shutdown.{suffix}";
            string gateSuffix = $"JokiAutomation.Test.Gate.{suffix}";

            string pipeStoppedEventName = "Global\\" + gateSuffix + ".PipeStopped";
            string allowMutexReleaseEventName = "Global\\" + gateSuffix + ".AllowMutexRelease";

            EventWaitHandle pipeStoppedEvent = null;
            EventWaitHandle allowMutexReleaseEvent = null;
            Process primaryProcess = null;
            Process forwarderProcess = null;

            try
            {
                pipeStoppedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, pipeStoppedEventName);
                allowMutexReleaseEvent = new EventWaitHandle(false, EventResetMode.ManualReset, allowMutexReleaseEventName);

                string primaryArgs = $"--gated-primary-shutdown --mutex={mutexName} --pipe={pipeName} --gate={gateSuffix} --timeoutMs=10000";
                var primaryStartInfo = new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = primaryArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                primaryProcess = new Process { StartInfo = primaryStartInfo, EnableRaisingEvents = true };
                var primaryStdOut = new StringBuilder();
                var primaryStdErr = new StringBuilder();
                primaryProcess.OutputDataReceived += (s, e) => { if (e.Data != null) primaryStdOut.AppendLine(e.Data); };
                primaryProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) primaryStdErr.AppendLine(e.Data); };
                primaryProcess.Start();
                primaryProcess.BeginOutputReadLine();
                primaryProcess.BeginErrorReadLine();

                bool pipeStoppedSignaled = pipeStoppedEvent.WaitOne(TimeSpan.FromSeconds(10));
                Assert.That(pipeStoppedSignaled, Is.True,
                    "Primary host did not signal PipeStopped in time." + Environment.NewLine + primaryStdOut + primaryStdErr);

                // Deterministic race window: mutex is still held by the primary host, but its pipe
                // server has already stopped. A forwarder launched now must not surface a blind
                // connect-timeout failure (ExitCode 4) just because the primary happens to be mid-shutdown.
                // We only observe the forwarder's own lifecycle markers up to the point where it has
                // decided it is no longer blindly failing (PIPE_CONNECT_FAILED -> FORWARDER_EXIT with
                // ExitCode 4), or has successfully re-evaluated and selected itself as Primary. We do not
                // let it run a full CLI/Primary session (which would touch real hardware/PTZ/ATEM).
                var forwarderStartInfo = new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = "ConnectDuringShutdownProbe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                forwarderStartInfo.EnvironmentVariables["JOKI_SINGLE_INSTANCE_MUTEX"] = mutexName;
                forwarderStartInfo.EnvironmentVariables["JOKI_COMMAND_PIPE"] = pipeName;

                var forwarderStdOut = new StringBuilder();
                var forwarderStdErr = new StringBuilder();
                var decisionReached = new ManualResetEventSlim(false);
                object syncRoot = new object();

                forwarderProcess = new Process { StartInfo = forwarderStartInfo, EnableRaisingEvents = true };
                forwarderProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (syncRoot)
                    {
                        forwarderStdOut.AppendLine(e.Data);
                        if (e.Data.Contains("FORWARDER_EXIT") || e.Data.Contains("Event=PRIMARY_INSTANCE_SELECTED"))
                        {
                            decisionReached.Set();
                        }
                    }
                };
                forwarderProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (syncRoot) { forwarderStdErr.AppendLine(e.Data); } } };
                forwarderProcess.Start();
                forwarderProcess.BeginOutputReadLine();
                forwarderProcess.BeginErrorReadLine();

                bool decided = decisionReached.Wait(TimeSpan.FromSeconds(15));

                allowMutexReleaseEvent.Set();

                string combinedForwarderOutput;
                lock (syncRoot)
                {
                    combinedForwarderOutput = forwarderStdOut + Environment.NewLine + forwarderStdErr;
                }

                Assert.That(decided, Is.True,
                    "Forwarder did not reach a FORWARDER_EXIT or PRIMARY_INSTANCE_SELECTED decision in time." +
                    Environment.NewLine + combinedForwarderOutput);

                Assert.That(primaryProcess.WaitForExit(10000), Is.True, "Primary host did not exit in time.");

                bool endedAsBlindConnectFailure = combinedForwarderOutput.Contains("FORWARDER_EXIT") &&
                    combinedForwarderOutput.Contains("ExitCode=" + CommandExitCodes.PipeConnectFailed) &&
                    !combinedForwarderOutput.Contains("Event=MUTEX_REEVALUATION_RESULT Reacquired=True");

                Assert.That(endedAsBlindConnectFailure, Is.False,
                    "Forwarder must not fail with a blind connect-timeout (ExitCode 4) while the primary is mid-shutdown." +
                    Environment.NewLine + combinedForwarderOutput);
            }
            finally
            {
                try { allowMutexReleaseEvent?.Set(); } catch { }
                try
                {
                    if (forwarderProcess != null && !forwarderProcess.HasExited)
                    {
                        forwarderProcess.Kill();
                    }
                }
                catch { }
                try
                {
                    if (primaryProcess != null && !primaryProcess.HasExited)
                    {
                        primaryProcess.Kill();
                    }
                }
                catch { }

                forwarderProcess?.Dispose();
                primaryProcess?.Dispose();
                pipeStoppedEvent?.Dispose();
                allowMutexReleaseEvent?.Dispose();
            }
        }

        [Test]
        [Explicit("Starts normal JokiAutomation scene commands and can control real hardware.")]
        [Category("Integration.Burst")]
        public async Task Burst_PrimaryAndForwarders_DoNotProduceLifecycleTimeoutMarkers()
        {
            string raw = Environment.GetEnvironmentVariable("JOKI_BURST_COMMANDS");
            if (string.IsNullOrWhiteSpace(raw))
            {
                Assert.Ignore("Set JOKI_BURST_COMMANDS explicitly before running the hardware burst test.");
            }

            string[] commands = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(x => x.Trim())
                                   .Where(x => x.Length > 0)
                                   .ToArray();
            Assert.That(commands.Length, Is.GreaterThan(1));

            AssertSingleInstanceMutexAvailableOrIgnore();

            string exeDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty;
            string logPath = Path.Combine(exeDirectory, "RasPiAutomationLog.txt");
            long beforeLength = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
            string suffix = Guid.NewGuid().ToString("N");
            var environmentVariables = new Dictionary<string, string>
            {
                ["JOKI_SINGLE_INSTANCE_MUTEX"] = $"Global\\JokiAutomation_Burst_{suffix}",
                ["JOKI_COMMAND_PIPE"] = $"JokiAutomation.Burst.{suffix}"
            };

            TimeSpan burstProcessTimeout = TimeSpan.FromSeconds(Math.Max(120, commands.Length * 120));

            var running = new List<Task<ProcessResult>>();
            foreach (string command in commands)
            {
                running.Add(RunAsync(Quote(command), burstProcessTimeout, environmentVariables));
                await Task.Delay(150);
            }

            ProcessResult[] results = await Task.WhenAll(running);
            Assert.That(results.Any(r => r.TimedOut), Is.False,
                string.Join(Environment.NewLine, results.Select(r => r.Combined)));

            string newLog = ReadAppendedLog(logPath, beforeLength);
            AssertNoLifecycleFailureMarkers(newLog);
        }

        [Test]
        [Explicit("Starts real JokiAutomation hardware commands. Observe the physical devices while this test runs.")]
        [Category("Hardware.Smoke")]
        public async Task Hardware_SmokeTest_ExecutesConfiguredCommandsWithoutLifecycleErrors()
        {
            string raw = Environment.GetEnvironmentVariable("JOKI_SMOKE_COMMANDS");
            if (string.IsNullOrWhiteSpace(raw))
            {
                Assert.Ignore(
                    "Set JOKI_SMOKE_COMMANDS explicitly before running the hardware smoke test, " +
                    "for example to a reviewed pipe-separated list of safe commands.");
            }

            string[] commands = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(x => x.Trim())
                                   .Where(x => x.Length > 0)
                                   .ToArray();
            Assert.That(commands.Length, Is.GreaterThan(0));

            string exeDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty;
            string logPath = Path.Combine(exeDirectory, "RasPiAutomationLog.txt");
            long beforeLength = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

            var results = new List<ProcessResult>();
            foreach (string command in commands)
            {
                TestContext.Progress.WriteLine($"SMOKE: executing {command}");
                ProcessResult result = await RunAsync(Quote(command), TimeSpan.FromSeconds(120));
                results.Add(result);

                Assert.That(result.TimedOut, Is.False,
                    $"Smoke command timed out: {command}{Environment.NewLine}{result.Combined}");
                Assert.That(result.ExitCode, Is.EqualTo(0),
                    $"Smoke command failed: {command}{Environment.NewLine}{result.Combined}");
            }

            string newLog = ReadAppendedLog(logPath, beforeLength);
            AssertNoLifecycleFailureMarkers(newLog);

            TestContext.Progress.WriteLine(
                "SMOKE: software-side execution finished. Manually verify the physical hardware state " +
                "(Audio, PTZ, ATEM, Beamer, Backup recorder, Raspberry Pi and any shutdown sequence included in JOKI_SMOKE_COMMANDS)." );
        }

        private static void AssertNoLifecycleFailureMarkers(string newLog)
        {
            string[] forbidden =
            {
                "PIPE_CONNECT_TIMEOUT",
                "PIPE_ACK_TIMEOUT",
                "PIPE_SERVER_STOP_TIMEOUT",
                "PIPE_SERVER_DISPOSE_TIMEOUT",
                "DISPATCHER_STOP_TIMEOUT",
                "UI_DISPATCH_FAILED",
                "MUTEX_RELEASE_BLOCKED"
            };

            foreach (string marker in forbidden)
            {
                Assert.That(newLog, Does.Not.Contain(marker),
                    $"Forbidden lifecycle marker found: {marker}{Environment.NewLine}{newLog}");
            }
        }

        private static string ResolveExePath()
        {
            string testDirectory = TestContext.CurrentContext.TestDirectory;
            var candidates = new List<string>();

            string configured = Environment.GetEnvironmentVariable("JOKI_EXE");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                candidates.Add(Path.GetFullPath(configured));
            }

            candidates.Add(Path.Combine(testDirectory, "JokiAutomation.exe"));

            string solutionRoot = FindSolutionRoot(testDirectory);
            if (solutionRoot != null)
            {
                candidates.Add(Path.Combine(solutionRoot, "JokiAutomation", "bin", "x64", "Release", "JokiAutomation.exe"));
                candidates.Add(Path.Combine(solutionRoot, "JokiAutomation", "bin", "x64", "Debug", "JokiAutomation.exe"));
            }

            string existingCandidate = candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(existingCandidate))
            {
                return existingCandidate;
            }

            return Path.GetFullPath(Path.Combine(testDirectory, "JokiAutomation.exe"));
        }

        private static string FindSolutionRoot(string startDirectory)
        {
            DirectoryInfo directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                if (directory.GetFiles("*.sln").Length > 0)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static void AssertSingleInstanceMutexAvailableOrIgnore()
        {
            Mutex mutex = null;
            bool acquired = false;

            try
            {
                mutex = new Mutex(false, @"Global\JokiAutomation_SingleInstance");
                acquired = mutex.WaitOne(0, false);
                if (!acquired)
                {
                    Assert.Ignore(
                        "Burst test requires an idle JokiAutomation single-instance slot. " +
                        "Close other JokiAutomation.exe processes and rerun the explicit hardware test.");
                }
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            finally
            {
                if (acquired && mutex != null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }

                if (mutex != null)
                {
                    mutex.Dispose();
                }
            }
        }

        private async Task<ProcessResult> RunAsync(string arguments, TimeSpan timeout, IReadOnlyDictionary<string, string> environmentVariables = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (environmentVariables != null)
            {
                foreach (KeyValuePair<string, string> pair in environmentVariables)
                {
                    psi.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                Task exitTask = Task.Run(() => process.WaitForExit());
                Task winner = await Task.WhenAny(exitTask, Task.Delay(timeout));
                bool timedOut = winner != exitTask;

                if (timedOut)
                {
                    try { process.Kill(); } catch { }
                    await exitTask;
                }

                return new ProcessResult(
                    timedOut ? -999 : process.ExitCode,
                    await stdout,
                    await stderr,
                    timedOut);
            }
        }

        private static void AssertContainsInOrder(string text, params string[] markers)
        {
            int position = -1;
            foreach (string marker in markers)
            {
                int next = text.IndexOf(marker, position + 1, StringComparison.Ordinal);
                Assert.That(next, Is.GreaterThan(position),
                    $"Marker missing or out of order: {marker}{Environment.NewLine}{text}");
                position = next;
            }
        }

        private static string ReadAppendedLog(string path, long start)
        {
            if (!File.Exists(path)) return string.Empty;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (start > stream.Length) start = 0;
                stream.Seek(start, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string stdOut, string stdErr, bool timedOut)
            {
                ExitCode = exitCode;
                StdOut = stdOut;
                StdErr = stdErr;
                TimedOut = timedOut;
            }

            public int ExitCode { get; }
            public string StdOut { get; }
            public string StdErr { get; }
            public bool TimedOut { get; }
            public string Combined => StdOut + Environment.NewLine + StdErr;
        }
    }
}
