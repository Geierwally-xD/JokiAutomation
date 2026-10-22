using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace JokiAutomation.Tests.Hardware
{
    [TestFixture]
    public sealed class AudioProfileHardwareTests
    {
        private static readonly TimeSpan LogClockTolerance = TimeSpan.FromSeconds(2);
        private const string DefaultAudioControlLogPath = "/home/pi/JokiAutomation/log/AudioControl.log";

        private string _exePath = string.Empty;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _exePath = ResolveExePath();
            Assert.That(File.Exists(_exePath), Is.True,
                "JokiAutomation.exe not found. Set environment variable JOKI_EXE to the built executable.");
        }

        [Test]
        [Explicit("Requires Raspberry Pi and real audio mixer")]
        [Category("Hardware.Audio")]
        public async Task Hardware_AudioProfileSequence_CompletesSuccessfully()
        {
            string enabled = Environment.GetEnvironmentVariable("JOKI_AUDIO_HARDWARE_ENABLED");
            if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Ignore("Set JOKI_AUDIO_HARDWARE_ENABLED=true before running the explicit audio hardware test.");
            }

            string rawSequence = Environment.GetEnvironmentVariable("JOKI_AUDIO_TEST_SEQUENCE");
            if (string.IsNullOrWhiteSpace(rawSequence))
            {
                Assert.Ignore("Set JOKI_AUDIO_TEST_SEQUENCE to a reviewed pipe-separated command id list such as 113|114|113|114.");
            }

            int[] commandIds = rawSequence.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(x => int.Parse(x.Trim()))
                                          .ToArray();
            Assert.That(commandIds.Length, Is.GreaterThan(0));

            int[] expectedProfiles = commandIds
                .Select(MapAudioCommandIdToExpectedProfile)
                .ToArray();

            var results = new List<ProcessResult>();

            // Windows side: prove the JokiAutomation.exe process lifecycle completes cleanly for each command.
            DateTime testStart = DateTime.Now;

            foreach (int commandId in commandIds)
            {
                string command = MapAudioCommandIdToPositionControlCommand(commandId);
                TestContext.Progress.WriteLine($"AUDIO HW: executing {command} for command id {commandId}");
                ProcessResult result = await RunAsync(command, TimeSpan.FromSeconds(120));
                results.Add(result);

                Assert.That(result.TimedOut, Is.False,
                    $"Audio hardware command timed out: {command}{Environment.NewLine}{result.Combined}");
                Assert.That(result.ExitCode, Is.EqualTo(0),
                    $"Audio hardware command failed: {command}{Environment.NewLine}{result.Combined}");
                Assert.That(result.Combined, Does.Contain("PROCESS_END_REACHED ExitCode=0"),
                    $"Missing PROCESS_END_REACHED ExitCode=0 for command {command}{Environment.NewLine}{result.Combined}");
                Assert.That(result.Combined, Does.Contain("MAIN_EXIT ExitCode=0"),
                    $"Missing MAIN_EXIT ExitCode=0 for command {command}{Environment.NewLine}{result.Combined}");
            }

            DateTime testEnd = DateTime.Now;

            int currentProcessId = Process.GetCurrentProcess().Id;
            Process[] remaining = Process.GetProcessesByName("JokiAutomation")
                .Where(p =>
                {
                    try
                    {
                        return p.Id != currentProcessId && !p.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();

            Assert.That(remaining.Length, Is.EqualTo(0),
                "Close remaining JokiAutomation.exe processes after the hardware audio test.");

            // Raspberry side: prove the mixer actually verified and reached each requested profile,
            // evaluated only within the current test's time window (plus a small clock tolerance).
            string logText = await ReadAudioControlLogAsync();

            DateTime logStart = testStart - LogClockTolerance;
            DateTime logEnd = testEnd + LogClockTolerance;

            IReadOnlyList<AudioControlProfileOperation> allOperations = AudioControlLogParser.Parse(logText);
            AudioControlProfileOperation[] windowOperations = allOperations
                .Where(o => o.StartTimestamp >= logStart && o.StartTimestamp <= logEnd)
                .ToArray();

            Assert.That(windowOperations.Length, Is.EqualTo(expectedProfiles.Length),
                $"Expected {expectedProfiles.Length} AudioControl profile operations within the test window [{logStart:yyyy-MM-dd HH:mm:ss}, {logEnd:yyyy-MM-dd HH:mm:ss}], found {windowOperations.Length}.{Environment.NewLine}" +
                string.Join(Environment.NewLine, windowOperations.Select(o => string.Join(Environment.NewLine, o.RawLines))));

            for (int i = 0; i < expectedProfiles.Length; i++)
            {
                AudioControlProfileOperation operation = windowOperations[i];
                string context = string.Join(Environment.NewLine, operation.RawLines);

                Assert.That(operation.Profile, Is.EqualTo(expectedProfiles[i]),
                    $"Operation {i + 1}: expected profile {expectedProfiles[i]} but found {operation.Profile}.{Environment.NewLine}{context}");
                Assert.That(operation.HasSuccessfulVerification, Is.True,
                    $"Operation {i + 1} (profile {operation.Profile}): missing AC_PROFILE_VERIFY match=1.{Environment.NewLine}{context}");
                Assert.That(operation.HasSuccessMarker, Is.True,
                    $"Operation {i + 1} (profile {operation.Profile}): missing AC_PROFILE_SUCCESS.{Environment.NewLine}{context}");
                Assert.That(operation.HasFailureMarker, Is.False,
                    $"Operation {i + 1} (profile {operation.Profile}): unexpected AC_PROFILE_FAIL.{Environment.NewLine}{context}");
                Assert.That(operation.MaximumAttempt, Is.LessThanOrEqualTo(2),
                    $"Operation {i + 1} (profile {operation.Profile}): more than one retry (attempt={operation.MaximumAttempt}).{Environment.NewLine}{context}");
                Assert.That(operation.IsAcceptedSuccess, Is.True,
                    $"Operation {i + 1} (profile {operation.Profile}): not accepted as a verified success.{Environment.NewLine}{context}");
            }
        }

        private static int MapAudioCommandIdToExpectedProfile(int commandId)
        {
            bool mapped = PositionControlProfileMapper.TryMap(
                MapAudioCommandIdToProfileCode(commandId),
                out PositionControlProfileMapping mapping);

            if (!mapped)
            {
                throw new ArgumentOutOfRangeException(nameof(commandId),
                    $"Unsupported audio command id for hardware test: {commandId}");
            }

            return mapping.AudioProfileId;
        }

        private static string MapAudioCommandIdToProfileCode(int commandId)
        {
            switch (commandId)
            {
                case 113:
                    return "KG";
                case 114:
                    return "KP";
                case 115:
                    return "KT";
                case 116:
                    return "KB";
                default:
                    throw new ArgumentOutOfRangeException(nameof(commandId),
                        $"Unsupported audio command id for hardware test: {commandId}");
            }
        }

        private async Task<string> ReadAudioControlLogAsync()
        {
            string configuredPath = Environment.GetEnvironmentVariable("JOKI_AUDIO_CONTROL_LOG_PATH");
            string remotePath = string.IsNullOrWhiteSpace(configuredPath) ? DefaultAudioControlLogPath : configuredPath;

            var rasPi = new RasPi();
            return await rasPi.DownloadRemoteTextFileAsync(remotePath);
        }

        private static string MapAudioCommandIdToPositionControlCommand(int commandId)
        {
            switch (commandId)
            {
                case 113:
                    return "PositionControl Kanzel KG";
                case 114:
                    return "PositionControl Kanzel KP";
                case 115:
                    return "PositionControl Kanzel KT";
                case 116:
                    return "PositionControl Kanzel KB";
                default:
                    throw new ArgumentOutOfRangeException(nameof(commandId),
                        $"Unsupported audio command id for hardware test: {commandId}");
            }
        }

        private async Task<ProcessResult> RunAsync(string arguments, TimeSpan timeout)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        stdout.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        stderr.AppendLine(e.Data);
                    }
                };

                process.Exited += (sender, e) => exitTcs.TrySetResult(process.ExitCode);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Task completed = await Task.WhenAny(exitTcs.Task, Task.Delay(timeout));
                if (completed != exitTcs.Task)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                    }

                    return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), true);
                }

                int exitCode = await exitTcs.Task;
                return new ProcessResult(exitCode, stdout.ToString(), stderr.ToString(), false);
            }
        }

        private static string ResolveExePath()
        {
            string configured = Environment.GetEnvironmentVariable("JOKI_EXE");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>();

            string solutionRoot = FindSolutionRoot(baseDirectory);
            if (solutionRoot != null)
            {
                candidates.Add(Path.Combine(solutionRoot, "JokiAutomation", "bin", "x64", "Release", "JokiAutomation.exe"));
                candidates.Add(Path.Combine(solutionRoot, "JokiAutomation", "bin", "x64", "Debug", "JokiAutomation.exe"));
            }

            return candidates.FirstOrDefault(File.Exists)
                ?? candidates.FirstOrDefault()
                ?? Path.Combine(baseDirectory, "JokiAutomation.exe");
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

        private sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string standardOutput, string standardError, bool timedOut)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
                TimedOut = timedOut;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
            public bool TimedOut { get; }
            public string Combined => StandardOutput + Environment.NewLine + StandardError;
        }
    }
}
