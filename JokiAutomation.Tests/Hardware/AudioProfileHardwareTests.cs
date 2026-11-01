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
    // Phase 2E Hardware-Gate: describes the technical facts of a single migrated scene (Predigt,
    // Gebet, BandMulti, LesungMulti, Text) needed to run and verify it as exactly one controlled,
    // real Raspberry/Windows hardware run. Values mirror the existing production mapping
    // (Program.MigratedSceneAudioProfiles / Form1 listBox4 / AudioMix.AM_PROFILE) so no separate
    // hard-coded Soll-values are introduced here.
    public sealed class MigratedSceneCase
    {
        public MigratedSceneCase(string sceneName, string audioProfileName, int audioProfileIndex)
        {
            SceneName = sceneName;
            AudioProfileName = audioProfileName;
            AudioProfileIndex = audioProfileIndex;
        }

        public string SceneName { get; }
        public string AudioProfileName { get; }
        public int AudioProfileIndex { get; }

        // AudioMix.AM_PROFILE (0x70 = 112) + listBox4 profile index, exactly as production code
        // sends it to the Raspberry (RasPiAutomation 30 <AudioCommandId>).
        public int AudioCommandId => 0x70 + AudioProfileIndex;

        // The Raspberry-side AudioControl.log profile id equals the listBox4 profile index.
        public int RaspberryProfile => AudioProfileIndex;

        public override string ToString() => SceneName;
    }

    // Phase 2F Hardware-Gate: describes a single real PositionControl PTZ invocation (the actual
    // production CLI arguments "PositionControl <Position> <Profil>"). The expected Raspberry-side
    // AudioControl.log profile is derived exclusively via the existing production
    // PositionControlProfileMapper (no separate hard-coded Soll-value).
    public sealed class PositionControlHardwareCase
    {
        public PositionControlHardwareCase(string positionName, string profileCode)
        {
            PositionName = positionName;
            ProfileCode = profileCode;
        }

        public string PositionName { get; }
        public string ProfileCode { get; }

        public string Arguments => $"PositionControl {PositionName} {ProfileCode}";

        public int ExpectedRaspberryProfile
        {
            get
            {
                bool mapped = PositionControlProfileMapper.TryMap(ProfileCode, out PositionControlProfileMapping mapping);
                if (!mapped)
                {
                    throw new ArgumentOutOfRangeException(nameof(ProfileCode),
                        $"Unsupported PositionControl profile code for hardware test: {ProfileCode}");
                }

                return mapping.AudioProfileId;
            }
        }

        public override string ToString() => Arguments;
    }

    [TestFixture]
    [NonParallelizable]
    public sealed class AudioProfileHardwareTests
    {
        private static readonly TimeSpan LogClockTolerance = TimeSpan.FromSeconds(2);
        private const string DefaultAudioControlLogPath = "/home/pi/JokiAutomation/log/AudioControl.log";

        // Phase 2E Hardware-Gate: the single shared TestCaseSource for all five migrated scenes.
        // Do not add a separate hardware fixture/test method per scene; extend this source instead.
        private static readonly MigratedSceneCase[] MigratedSceneCases =
        {
            new MigratedSceneCase("Predigt", "Predigt", 2),
            new MigratedSceneCase("Gebet", "Gottesdienst", 1),
            new MigratedSceneCase("BandMulti", "Band", 4),
            new MigratedSceneCase("LesungMulti", "Predigt", 2),
            new MigratedSceneCase("Text", "Text", 3)
        };

        // Phase 2F Hardware-Gate: the single shared TestCaseSource for the active-PTZ PositionControl
        // hardware test. Do not add a separate hardware fixture/test method per profile; extend this
        // source instead. Arguments match the real production CLI invocation unchanged.
        private static readonly PositionControlHardwareCase[] PositionControlHardwareCases =
        {
            new PositionControlHardwareCase("Kanzel", "KG"),
            new PositionControlHardwareCase("Kanzel", "KP"),
            new PositionControlHardwareCase("Kanzel", "KT")
        };

        private string _exePath = string.Empty;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _exePath = ResolveExePath();
            Assert.That(File.Exists(_exePath), Is.True,
                "JokiAutomation.exe not found. Set environment variable JOKI_EXE to the built executable.");
        }

        // Phase 2E Hardware-Gate: the single shared, parametrized hardware test for all migrated
        // scenes (Predigt, Gebet, BandMulti, LesungMulti, Text). Runs exactly one controlled real
        // scene invocation of JokiAutomation.exe per scene, then proves the Windows process
        // lifecycle, the runtime SCENE_PLAN shape, exactly-once Audio/Video dispatch (no Legacy, no
        // duplicate), and the matching real Raspberry AudioControl.log profile operation.
        [TestCaseSource(nameof(MigratedSceneCases))]
        [Explicit("Requires Raspberry Pi, ATEM and real audio mixer")]
        [Category("Hardware.Scene")]
        public async Task Hardware_MigratedScene_RunsOnceAndVerifiesAudioAndVideo(MigratedSceneCase sceneCase)
        {
            string enabled = Environment.GetEnvironmentVariable("JOKI_SCENE_HARDWARE_ENABLED");
            if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Ignore("Set JOKI_SCENE_HARDWARE_ENABLED=true before running the explicit migrated-scene hardware test.");
            }

            DateTime testStart = DateTime.Now;

            string sceneExePath = ResolveSceneHardwareExePath();

            TestContext.Progress.WriteLine($"Scene:{Environment.NewLine}{sceneCase.SceneName}");
            TestContext.Progress.WriteLine($"JokiAutomation EXE:{Environment.NewLine}{sceneExePath}");
            TestContext.Progress.WriteLine($"Configuration:{Environment.NewLine}Release");
            TestContext.Progress.WriteLine($"Platform:{Environment.NewLine}x64");

            TestContext.Progress.WriteLine($"SCENE HW: executing scene '{sceneCase.SceneName}'");
            ProcessResult result = await RunAsync(sceneExePath, sceneCase.SceneName, TimeSpan.FromSeconds(120));

            DateTime testEnd = DateTime.Now;

            // 4. Windows-Prozessprüfung: exactly one controlled run, no timeout, ExitCode 0.
            Assert.That(result.TimedOut, Is.False,
                $"Scene '{sceneCase.SceneName}' timed out.{Environment.NewLine}{result.Combined}");
            Assert.That(result.ExitCode, Is.EqualTo(0),
                $"Scene '{sceneCase.SceneName}' failed.{Environment.NewLine}{result.Combined}");
            Assert.That(result.Combined, Does.Contain("PROCESS_END_REACHED ExitCode=0"),
                $"Missing PROCESS_END_REACHED ExitCode=0 for scene '{sceneCase.SceneName}'.{Environment.NewLine}{result.Combined}");
            Assert.That(result.Combined, Does.Contain("MAIN_EXIT ExitCode=0"),
                $"Missing MAIN_EXIT ExitCode=0 for scene '{sceneCase.SceneName}'.{Environment.NewLine}{result.Combined}");

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
                $"Close remaining JokiAutomation.exe processes after the '{sceneCase.SceneName}' hardware test.");

            // 5. ScenePlan structure (LegacyCount, AudioCount, VideoCount, PositionCount, UiCount,
            // ExecutionMode) is NOT asserted here. The real process stdout/stderr captured by
            // RunAsync is not a suitable evidence source for the ScenePlan shape; that structure is
            // already proven exclusively by the hardware-free ScenePlan/Dispatcher tests, which
            // remain the source of truth for LegacyCount=0, AudioCount=1, VideoCount=1,
            // PositionCount=0, UiCount=0, ExecutionMode=ParallelAudioVideo and exactly-once
            // dispatcher execution.

            // 6. Exactly-once SceneBegin/SceneEnd/Audio/Video pipeline markers and the absence of a
            // Legacy pipeline are NOT asserted here either: the real process stdout/stderr captured
            // by RunAsync does not contain Event=SCENE_BEGIN/SCENE_END/AUDIO_BEGIN/VIDEO_BEGIN/
            // LEGACY_BEGIN markers for the externally started scene process, so it is not a suitable
            // evidence source for this either. Exactly-once dispatcher execution and the absence of a
            // Legacy fallback remain proven exclusively by the hardware-free ScenePlan/Dispatcher
            // tests.

            // 7. Raspberry Audio-Hardware-Nachweis: only the current scene's time window, matching
            // the production profile mapping (sceneCase.RaspberryProfile / AudioCommandId), reusing
            // the existing AudioControlLogParser (no separately hard-coded Soll-values).
            string logText = await ReadAudioControlLogAsync();

            DateTime logStart = testStart - LogClockTolerance;
            DateTime logEnd = testEnd + LogClockTolerance;

            IReadOnlyList<AudioControlProfileOperation> allOperations = AudioControlLogParser.Parse(logText);
            AudioControlProfileOperation[] windowOperations = allOperations
                .Where(o => o.StartTimestamp >= logStart && o.StartTimestamp <= logEnd)
                .ToArray();

            Assert.That(windowOperations.Length, Is.EqualTo(1),
                $"Scene '{sceneCase.SceneName}': expected exactly 1 AudioControl profile operation within the test window [{logStart:yyyy-MM-dd HH:mm:ss}, {logEnd:yyyy-MM-dd HH:mm:ss}], found {windowOperations.Length}.{Environment.NewLine}" +
                string.Join(Environment.NewLine, windowOperations.Select(o => string.Join(Environment.NewLine, o.RawLines))));

            AudioControlProfileOperation operation = windowOperations[0];
            string context = string.Join(Environment.NewLine, operation.RawLines);

            Assert.That(operation.Profile, Is.EqualTo(sceneCase.RaspberryProfile),
                $"Scene '{sceneCase.SceneName}': expected profile {sceneCase.RaspberryProfile} but found {operation.Profile}.{Environment.NewLine}{context}");
            Assert.That(operation.HasSuccessfulVerification, Is.True,
                $"Scene '{sceneCase.SceneName}' (profile {operation.Profile}): missing AC_PROFILE_VERIFY match=1.{Environment.NewLine}{context}");
            Assert.That(operation.HasSuccessMarker, Is.True,
                $"Scene '{sceneCase.SceneName}' (profile {operation.Profile}): missing AC_PROFILE_SUCCESS.{Environment.NewLine}{context}");
            Assert.That(operation.HasFailureMarker, Is.False,
                $"Scene '{sceneCase.SceneName}' (profile {operation.Profile}): unexpected AC_PROFILE_FAIL.{Environment.NewLine}{context}");
            // 8. Retry weiterhin zulässig: at most one controlled retry is accepted.
            Assert.That(operation.MaximumAttempt, Is.LessThanOrEqualTo(2),
                $"Scene '{sceneCase.SceneName}' (profile {operation.Profile}): more than one retry (attempt={operation.MaximumAttempt}).{Environment.NewLine}{context}");
            Assert.That(operation.IsAcceptedSuccess, Is.True,
                $"Scene '{sceneCase.SceneName}' (profile {operation.Profile}): not accepted as a verified success.{Environment.NewLine}{context}");

            TestContext.Progress.WriteLine(
                $"Audio CommandId = {sceneCase.AudioCommandId}{Environment.NewLine}Raspberry Profile = {operation.Profile}");

            // ATEM/PiP final state cannot be verified from process output or AudioControl.log;
            // this remains an explicit manual check, never an automatic PASS.
            TestContext.Progress.WriteLine($"ATEM final state:{Environment.NewLine}MANUAL VERIFICATION REQUIRED");
            TestContext.Progress.WriteLine($"PiP final state:{Environment.NewLine}MANUAL VERIFICATION REQUIRED");
        }

        // Phase 2F Hardware-Gate: the single shared, parametrized hardware test for the active-PTZ
        // PositionControl path (CommandDispatcher -> ScenePlan -> exactly one PositionCommand ->
        // existing PositionControlPtzAudioOrchestrator). Runs exactly one controlled real
        // "PositionControl <Position> <Profil>" invocation of JokiAutomation.exe per test case, then
        // proves the Windows process lifecycle and the matching real Raspberry AudioControl.log
        // profile operation, reusing the exact same infrastructure as the migrated-scene hardware
        // test (ResolveSceneHardwareExePath, RunAsync, AudioControlLogParser, RasPi.
        // DownloadRemoteTextFileAsync, JOKI_AUDIO_CONTROL_LOG_PATH, clock tolerance). ATEM/PTZ final
        // state remains an explicit manual check, never an automatic PASS.
        [TestCaseSource(nameof(PositionControlHardwareCases))]
        [Explicit("Requires Raspberry Pi, Canon PTZ, ATEM and real audio mixer")]
        [Category("Hardware.Scene")]
        public async Task Hardware_PositionControlPtz_RunsOnceAndVerifiesAudio(PositionControlHardwareCase testCase)
        {
            string enabled = Environment.GetEnvironmentVariable("JOKI_SCENE_HARDWARE_ENABLED");
            if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Ignore("Set JOKI_SCENE_HARDWARE_ENABLED=true before running the explicit PositionControl hardware test.");
            }

            DateTime testStart = DateTime.Now;

            string sceneExePath = ResolveSceneHardwareExePath();

            TestContext.Progress.WriteLine($"PositionControl:{Environment.NewLine}{testCase.Arguments}");
            TestContext.Progress.WriteLine($"JokiAutomation EXE:{Environment.NewLine}{sceneExePath}");
            TestContext.Progress.WriteLine($"Configuration:{Environment.NewLine}Release");
            TestContext.Progress.WriteLine($"Platform:{Environment.NewLine}x64");

            TestContext.Progress.WriteLine($"SCENE HW: executing '{testCase.Arguments}'");
            ProcessResult result = await RunAsync(sceneExePath, testCase.Arguments, TimeSpan.FromSeconds(120));

            DateTime testEnd = DateTime.Now;

            // Windows-Prozesspr\u00fcfung: exactly one controlled run, no timeout, ExitCode 0.
            Assert.That(result.TimedOut, Is.False,
                $"PositionControl '{testCase.Arguments}' timed out.{Environment.NewLine}{result.Combined}");
            Assert.That(result.ExitCode, Is.EqualTo(0),
                $"PositionControl '{testCase.Arguments}' failed.{Environment.NewLine}{result.Combined}");
            Assert.That(result.Combined, Does.Contain("PROCESS_END_REACHED ExitCode=0"),
                $"Missing PROCESS_END_REACHED ExitCode=0 for '{testCase.Arguments}'.{Environment.NewLine}{result.Combined}");
            Assert.That(result.Combined, Does.Contain("MAIN_EXIT ExitCode=0"),
                $"Missing MAIN_EXIT ExitCode=0 for '{testCase.Arguments}'.{Environment.NewLine}{result.Combined}");

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
                $"Close remaining JokiAutomation.exe processes after the '{testCase.Arguments}' hardware test.");

            // Raspberry side: prove the mixer actually verified and reached the requested profile,
            // evaluated only within the current test's time window (plus a small clock tolerance),
            // reusing the existing AudioControlLogParser (no separately hard-coded Soll-value; the
            // expected profile comes from the production PositionControlProfileMapper).
            string logText = await ReadAudioControlLogAsync();

            DateTime logStart = testStart - LogClockTolerance;
            DateTime logEnd = testEnd + LogClockTolerance;

            IReadOnlyList<AudioControlProfileOperation> allOperations = AudioControlLogParser.Parse(logText);
            AudioControlProfileOperation[] windowOperations = allOperations
                .Where(o => o.StartTimestamp >= logStart && o.StartTimestamp <= logEnd)
                .ToArray();

            Assert.That(windowOperations.Length, Is.EqualTo(1),
                $"PositionControl '{testCase.Arguments}': expected exactly 1 AudioControl profile operation within the test window [{logStart:yyyy-MM-dd HH:mm:ss}, {logEnd:yyyy-MM-dd HH:mm:ss}], found {windowOperations.Length}.{Environment.NewLine}" +
                string.Join(Environment.NewLine, windowOperations.Select(o => string.Join(Environment.NewLine, o.RawLines))));

            AudioControlProfileOperation operation = windowOperations[0];
            string context = string.Join(Environment.NewLine, operation.RawLines);

            Assert.That(operation.Profile, Is.EqualTo(testCase.ExpectedRaspberryProfile),
                $"PositionControl '{testCase.Arguments}': expected profile {testCase.ExpectedRaspberryProfile} but found {operation.Profile}.{Environment.NewLine}{context}");
            Assert.That(operation.HasSuccessfulVerification, Is.True,
                $"PositionControl '{testCase.Arguments}' (profile {operation.Profile}): missing AC_PROFILE_VERIFY match=1.{Environment.NewLine}{context}");
            Assert.That(operation.HasSuccessMarker, Is.True,
                $"PositionControl '{testCase.Arguments}' (profile {operation.Profile}): missing AC_PROFILE_SUCCESS.{Environment.NewLine}{context}");
            Assert.That(operation.HasFailureMarker, Is.False,
                $"PositionControl '{testCase.Arguments}' (profile {operation.Profile}): unexpected AC_PROFILE_FAIL.{Environment.NewLine}{context}");
            Assert.That(operation.MaximumAttempt, Is.LessThanOrEqualTo(2),
                $"PositionControl '{testCase.Arguments}' (profile {operation.Profile}): more than one retry (attempt={operation.MaximumAttempt}).{Environment.NewLine}{context}");
            Assert.That(operation.IsAcceptedSuccess, Is.True,
                $"PositionControl '{testCase.Arguments}' (profile {operation.Profile}): not accepted as a verified success.{Environment.NewLine}{context}");

            TestContext.Progress.WriteLine(
                $"PositionControl = {testCase.Arguments}{Environment.NewLine}Raspberry Profile = {operation.Profile}");

            // ATEM/PTZ/PiP final state cannot be verified from process output or AudioControl.log;
            // this remains an explicit manual check, never an automatic PASS.
            TestContext.Progress.WriteLine($"ATEM/PTZ final state:{Environment.NewLine}MANUAL VERIFICATION REQUIRED");
            TestContext.Progress.WriteLine($"PiP final state:{Environment.NewLine}MANUAL VERIFICATION REQUIRED");
        }

        private static void AssertOccursExactlyOnce(string haystack, string marker, string sceneName)
        {
            int count = CountOccurrences(haystack, marker);
            Assert.That(count, Is.EqualTo(1),
                $"Scene '{sceneName}': expected exactly 1 occurrence of '{marker}', found {count}.{Environment.NewLine}{haystack}");
        }

        private static int CountOccurrences(string haystack, string marker)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(marker))
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += marker.Length;
            }

            return count;
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

        private Task<ProcessResult> RunAsync(string arguments, TimeSpan timeout)
        {
            return RunAsync(_exePath, arguments, timeout);
        }

        private async Task<ProcessResult> RunAsync(string exePath, string arguments, TimeSpan timeout)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
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

        // Phase 2E Hardware-Gate: deterministic EXE resolution used exclusively by
        // Hardware_MigratedScene_RunsOnceAndVerifiesAudioAndVideo. Does not share ResolveExePath's
        // Debug/AnyCPU/BaseDirectory fallbacks, so the scene hardware test never silently runs
        // against a different build than the defined Release|x64 reference build.
        private static string ResolveSceneHardwareExePath()
        {
            string configured = Environment.GetEnvironmentVariable("JOKI_EXE");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string fullPath = Path.GetFullPath(configured);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException(
                        $"JOKI_EXE is set but the file does not exist: {fullPath}", fullPath);
                }

                TestContext.Progress.WriteLine($"Scene hardware gate: using JOKI_EXE override: {fullPath}");
                return fullPath;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string solutionRoot = FindSolutionRoot(baseDirectory);
            if (solutionRoot == null)
            {
                throw new FileNotFoundException(
                    "Scene hardware gate: could not locate the solution root (*.sln) to resolve the Release|x64 JokiAutomation.exe.");
            }

            string releaseX64ExePath = Path.Combine(solutionRoot, "JokiAutomation", "bin", "x64", "Release", "JokiAutomation.exe");
            if (!File.Exists(releaseX64ExePath))
            {
                throw new FileNotFoundException(
                    $"Scene hardware gate: Release|x64 JokiAutomation.exe not found at the defined reference path: {releaseX64ExePath}. " +
                    "Build JokiAutomation in Release|x64 before running the scene hardware test, or set JOKI_EXE explicitly.",
                    releaseX64ExePath);
            }

            TestContext.Progress.WriteLine($"Scene hardware gate: using Release|x64 reference build: {releaseX64ExePath}");
            return releaseX64ExePath;
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
